using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;
using System.Reflection;
using System.Collections;
using MongoDB.Bson;

namespace Infrastructure.Utilities
{
    /// <summary>
    /// SafeObject that properly handles MongoDB's automatic _t/_v polymorphic serialization
    /// FIXED VERSION - Properly handles Dictionary serialization/deserialization
    /// </summary>
    [BsonDiscriminator("SafeObject")]
    public class SafeObject
    {
        /// <summary>
        /// Type information for the stored value
        /// </summary>
        [BsonElement("_type_")]
        [JsonPropertyName("_type_")]
        public string Type { get; set; }

        /// <summary>
        /// The actual value - MongoDB will automatically wrap this with _t/_v if needed
        /// </summary>
        [BsonElement("_value_")]
        [JsonPropertyName("_value_")]
        public object Value { get; set; }

        // Circular reference tracking
        private static readonly ThreadLocal<HashSet<object>> _processingObjects = new(() => new HashSet<object>());
        private static readonly ThreadLocal<int> _recursionDepth = new(() => 0);
        private const int MaxRecursionDepth = 100;

        /// <summary>
        /// Creates a SafeObject from any value
        /// </summary>
        public static SafeObject FromValue(object? value)
        {
            // Reset tracking for new call chain
            if (_recursionDepth.Value == 0)
            {
                _processingObjects.Value.Clear();
            }

            return FromValueInternal(value);
        }

        private static SafeObject FromValueInternal(object? value)
        {
            // Check recursion depth
            if (_recursionDepth.Value >= MaxRecursionDepth)
            {
                return new SafeObject
                {
                    Type = "System.String",
                    Value = "[Max depth exceeded]"
                };
            }

            if (value == null)
                return new SafeObject { Type = "null", Value = null };

            var type = value.GetType();

            // Handle simple types first (no circular reference possible)
            if (IsSimpleType(type))
            {
                return new SafeObject
                {
                    Type = type.FullName,
                    Value = value
                };
            }

            // Convert GUIDs to strings for storage
            if (type == typeof(Guid))
            {
                return new SafeObject
                {
                    Type = "System.Guid",
                    Value = value.ToString()
                };
            }

            // Handle nullable GUIDs
            if (type == typeof(Guid?))
            {
                var nullableGuid = (Guid?)value;
                return new SafeObject
                {
                    Type = "System.Nullable`1[[System.Guid]]",
                    Value = nullableGuid?.ToString()
                };
            }

            // Check for circular references using object identity
            if (_processingObjects.Value.Contains(value))
            {
                return new SafeObject
                {
                    Type = type.FullName,
                    Value = "[Circular Reference]"
                };
            }

            // Special handling for problematic types
            if (IsProblematicType(type))
            {
                return new SafeObject
                {
                    Type = type.FullName,
                    Value = GetSafeRepresentation(value, type)
                };
            }

            // Add to processing set and increment depth
            _processingObjects.Value.Add(value);
            _recursionDepth.Value++;

            try
            {
                // FIXED: Handle dictionaries BEFORE general IEnumerable check
                if (value is IDictionary dict)
                {
                    var dictItems = new Dictionary<string, SafeObject>();
                    foreach (DictionaryEntry entry in dict)
                    {
                        var key = entry.Key?.ToString() ?? "null";
                        dictItems[key] = FromValueInternal(entry.Value);
                    }
                    return new SafeObject
                    {
                        Type = type.FullName,
                        Value = dictItems
                    };
                }

                // Handle generic dictionaries (Dictionary<TKey, TValue>)
                if (type.IsGenericType &&
                    (type.GetGenericTypeDefinition() == typeof(Dictionary<,>) ||
                     type.GetGenericTypeDefinition() == typeof(IDictionary<,>) ||
                     typeof(IDictionary).IsAssignableFrom(type)))
                {
                    var dictItems = new Dictionary<string, SafeObject>();

                    // Use reflection to get the dictionary entries
                    var enumerator = ((IEnumerable)value).GetEnumerator();
                    while (enumerator.MoveNext())
                    {
                        var kvp = enumerator.Current;
                        var keyProp = kvp.GetType().GetProperty("Key");
                        var valueProp = kvp.GetType().GetProperty("Value");

                        var key = keyProp?.GetValue(kvp)?.ToString() ?? "null";
                        var val = valueProp?.GetValue(kvp);

                        dictItems[key] = FromValueInternal(val);
                    }

                    return new SafeObject
                    {
                        Type = type.FullName,
                        Value = dictItems
                    };
                }

                // Handle collections (but not dictionaries, which are handled above)
                if (value is IEnumerable enumerable && type != typeof(string))
                {
                    var items = new List<SafeObject>();
                    var count = 0;
                    const int maxItems = 50;

                    foreach (var item in enumerable)
                    {
                        if (count >= maxItems)
                        {
                            items.Add(new SafeObject
                            {
                                Type = "System.String",
                                Value = $"[{count - maxItems} more items...]"
                            });
                            break;
                        }
                        items.Add(FromValueInternal(item));
                        count++;
                    }
                    return new SafeObject
                    {
                        Type = type.FullName,
                        Value = items
                    };
                }

                // Handle complex objects - convert to dictionary of SafeObjects
                return new SafeObject
                {
                    Type = type.FullName,
                    Value = ConvertComplexObject(value)
                };
            }
            finally
            {
                // Always clean up
                _processingObjects.Value.Remove(value);
                _recursionDepth.Value--;
            }
        }

        /// <summary>
        /// Reconstructs the original typed object from SafeObject
        /// </summary>
        public T ToValue<T>()
        {
            return (T)ToValue(typeof(T));
        }

        /// <summary>
        /// Reconstructs the original object with the specified target type
        /// </summary>
        public object ToValue(Type targetType = null)
        {
            if (Type == "null" || Value == null)
                return null;

            var actualType = targetType ?? ResolveType(Type);
            if (actualType == null)
                throw new InvalidOperationException($"Cannot resolve type: {Type}");

            // Handle simple types
            if (IsSimpleType(actualType))
            {
                if (Value is BsonDateTime bsonDate)
                    return Convert.ChangeType(bsonDate.ToUniversalTime(), actualType);
                return Convert.ChangeType(Value, actualType);
            }

            // Reconstruct GUIDs from strings
            if (actualType == typeof(Guid) && Value is string guidString)
            {
                return Guid.Parse(guidString);
            }

            // Handle nullable GUIDs
            if (actualType == typeof(Guid?) && Value != null)
            {
                if (Value is string nullableGuidString)
                    return Guid.Parse(nullableGuidString);
                return null;
            }

            // Handle MongoDB's automatic _t/_v wrapping
            if (Value is BsonDocument bsonDoc)
            {
                // Check if MongoDB wrapped our value with _t and _v
                if (bsonDoc.Contains("_t") && bsonDoc.Contains("_v"))
                {
                    // This is MongoDB's polymorphic wrapper
                    var wrappedValue = BsonTypeMapper.MapToDotNetValue(bsonDoc["_v"]);
                    return ProcessWrappedValue(wrappedValue, actualType);
                }

                // Regular BsonDocument - convert to SafeObject dictionary
                return ProcessBsonDocument(bsonDoc, actualType);
            }

            // Handle dictionary that might have MongoDB's _t/_v structure
            if (Value is Dictionary<string, object> dict)
            {
                if (dict.ContainsKey("_t") && dict.ContainsKey("_v"))
                {
                    // MongoDB wrapped this value
                    return ProcessWrappedValue(dict["_v"], actualType);
                }

                // FIXED: Check if this is a dictionary representation from SafeObject
                if (IsDictionaryType(actualType))
                {
                    return ReconstructDictionary(ConvertToDictionarySafeObjects(dict), actualType);
                }

                // Regular dictionary - process as SafeObject dictionary
                return ProcessDictionary(dict, actualType);
            }

            // FIXED: Handle Dictionary<string, SafeObject> for dictionaries specifically
            if (Value is Dictionary<string, SafeObject> safeDict)
            {
                if (IsDictionaryType(actualType))
                {
                    return ReconstructDictionary(safeDict, actualType);
                }
                return ReconstructComplexObject(safeDict, actualType);
            }

            // Handle List<SafeObject> for collections
            if (Value is List<SafeObject> safeObjects)
            {
                return ReconstructCollection(safeObjects, actualType);
            }

            // Handle BsonArray for collections
            if (Value is BsonArray bsonArray)
            {
                var items = new List<SafeObject>();
                foreach (var item in bsonArray)
                {
                    items.Add(ExtractSafeObjectFromBson(item));
                }
                return ReconstructCollection(items, actualType);
            }

            // Handle List<object> that might contain wrapped values
            if (Value is List<object> objectList)
            {
                var items = new List<SafeObject>();
                foreach (var item in objectList)
                {
                    items.Add(ExtractSafeObjectFromValue(item));
                }
                return ReconstructCollection(items, actualType);
            }

            throw new InvalidOperationException($"Cannot reconstruct value of type {Type} from value type {Value?.GetType()?.Name ?? "null"}");
        }

        // FIXED: Add dictionary-specific reconstruction method
        private object ReconstructDictionary(Dictionary<string, SafeObject> safeDict, Type targetType)
        {
            if (!IsDictionaryType(targetType))
                throw new ArgumentException($"Target type {targetType.Name} is not a dictionary type");

            // Handle non-generic IDictionary
            if (targetType == typeof(IDictionary) || targetType == typeof(Hashtable))
            {
                var hashtable = new Hashtable();
                foreach (var kvp in safeDict)
                {
                    hashtable[kvp.Key] = kvp.Value.ToValue();
                }
                return hashtable;
            }

            // Handle generic dictionary types
            if (targetType.IsGenericType)
            {
                var genericTypeDefinition = targetType.GetGenericTypeDefinition();

                if (genericTypeDefinition == typeof(Dictionary<,>) ||
                    genericTypeDefinition == typeof(IDictionary<,>) ||
                    typeof(IDictionary).IsAssignableFrom(targetType))
                {
                    var keyType = targetType.GetGenericArguments()[0];
                    var valueType = targetType.GetGenericArguments()[1];
                    var dictType = typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
                    var instance = Activator.CreateInstance(dictType);
                    var addMethod = dictType.GetMethod("Add");

                    foreach (var kvp in safeDict)
                    {
                        try
                        {
                            var key = Convert.ChangeType(kvp.Key, keyType);
                            var value = kvp.Value.ToValue(valueType);
                            addMethod.Invoke(instance, new[] { key, value });
                        }
                        catch (Exception)
                        {
                            // Skip problematic key-value pairs
                        }
                    }

                    return instance;
                }
            }

            // Fallback - shouldn't reach here
            return new Dictionary<string, object>(safeDict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToValue()));
        }

        // FIXED: Add helper method to check if type is a dictionary
        private static bool IsDictionaryType(Type type)
        {
            if (type == typeof(IDictionary) || type == typeof(Hashtable))
                return true;

            if (type.IsGenericType)
            {
                var genericTypeDefinition = type.GetGenericTypeDefinition();
                return genericTypeDefinition == typeof(Dictionary<,>) ||
                       genericTypeDefinition == typeof(IDictionary<,>) ||
                       typeof(IDictionary).IsAssignableFrom(type);
            }

            return typeof(IDictionary).IsAssignableFrom(type);
        }

        // FIXED: Add helper to convert Dictionary<string, object> to Dictionary<string, SafeObject>
        private Dictionary<string, SafeObject> ConvertToDictionarySafeObjects(Dictionary<string, object> dict)
        {
            var result = new Dictionary<string, SafeObject>();
            foreach (var kvp in dict)
            {
                result[kvp.Key] = ExtractSafeObjectFromValue(kvp.Value);
            }
            return result;
        }

        private object ProcessWrappedValue(object wrappedValue, Type targetType)
        {
            // MongoDB wrapped our actual value - now process it
            if (wrappedValue is BsonDocument doc)
            {
                return ProcessBsonDocument(doc, targetType);
            }

            if (wrappedValue is BsonArray array)
            {
                var items = new List<SafeObject>();
                foreach (var item in array)
                {
                    items.Add(ExtractSafeObjectFromBson(item));
                }
                return ReconstructCollection(items, targetType);
            }

            if (wrappedValue is Dictionary<string, object> dict)
            {
                // FIXED: Check if target is dictionary type
                if (IsDictionaryType(targetType))
                {
                    return ReconstructDictionary(ConvertToDictionarySafeObjects(dict), targetType);
                }
                return ProcessDictionary(dict, targetType);
            }

            if (wrappedValue is List<object> list)
            {
                var items = new List<SafeObject>();
                foreach (var item in list)
                {
                    items.Add(ExtractSafeObjectFromValue(item));
                }
                return ReconstructCollection(items, targetType);
            }

            // Simple value that was wrapped
            if (targetType != null && IsSimpleType(targetType))
            {
                if (wrappedValue is BsonDateTime bsonDate)
                    return Convert.ChangeType(bsonDate.ToUniversalTime(), targetType);
                return Convert.ChangeType(wrappedValue, targetType);
            }

            return wrappedValue;
        }

        private object ProcessBsonDocument(BsonDocument doc, Type targetType)
        {
            var safeDict = new Dictionary<string, SafeObject>();

            foreach (var element in doc)
            {
                safeDict[element.Name] = ExtractSafeObjectFromBson(element.Value);
            }

            // FIXED: Route to appropriate reconstruction method
            if (IsDictionaryType(targetType))
            {
                return ReconstructDictionary(safeDict, targetType);
            }

            return ReconstructComplexObject(safeDict, targetType);
        }

        private object ProcessDictionary(Dictionary<string, object> dict, Type targetType)
        {
            var safeDict = new Dictionary<string, SafeObject>();

            foreach (var kvp in dict)
            {
                safeDict[kvp.Key] = ExtractSafeObjectFromValue(kvp.Value);
            }

            // FIXED: Route to appropriate reconstruction method
            if (IsDictionaryType(targetType))
            {
                return ReconstructDictionary(safeDict, targetType);
            }

            return ReconstructComplexObject(safeDict, targetType);
        }

        private SafeObject ExtractSafeObjectFromBson(BsonValue bsonValue)
        {
            if (bsonValue is BsonDocument doc)
            {
                // Check if this is a SafeObject structure
                if (doc.Contains("_type_") && doc.Contains("_value_"))
                {
                    return new SafeObject
                    {
                        Type = doc["_type_"].AsString,
                        Value = BsonTypeMapper.MapToDotNetValue(doc["_value_"])
                    };
                }

                // Check if MongoDB wrapped it with _t/_v
                if (doc.Contains("_t") && doc.Contains("_v"))
                {
                    var discriminator = doc["_t"].AsString;
                    var innerValue = doc["_v"];

                    // If the discriminator is "SafeObject", extract the actual SafeObject
                    if (discriminator == "SafeObject" && innerValue is BsonDocument innerDoc)
                    {
                        if (innerDoc.Contains("_type_") && innerDoc.Contains("_value_"))
                        {
                            return new SafeObject
                            {
                                Type = innerDoc["_type_"].AsString,
                                Value = BsonTypeMapper.MapToDotNetValue(innerDoc["_value_"])
                            };
                        }
                    }

                    // Otherwise, create a SafeObject from the wrapped value
                    var dotNetValue = BsonTypeMapper.MapToDotNetValue(innerValue);
                    return new SafeObject
                    {
                        Type = discriminator,
                        Value = dotNetValue
                    };
                }
            }

            // Not a SafeObject structure - create one
            var mappedValue = BsonTypeMapper.MapToDotNetValue(bsonValue);
            return SafeObject.FromValue(mappedValue);
        }

        private SafeObject ExtractSafeObjectFromValue(object value)
        {
            if (value is BsonDocument bsonDoc)
            {
                return ExtractSafeObjectFromBson(bsonDoc);
            }

            if (value is Dictionary<string, object> dict)
            {
                // Check if this is a SafeObject structure
                if (dict.ContainsKey("_type_") && dict.ContainsKey("_value_"))
                {
                    return new SafeObject
                    {
                        Type = dict["_type_"].ToString(),
                        Value = dict["_value_"]
                    };
                }

                // Check if MongoDB wrapped it
                if (dict.ContainsKey("_t") && dict.ContainsKey("_v"))
                {
                    var discriminator = dict["_t"].ToString();

                    // If it's a SafeObject discriminator, extract the inner SafeObject
                    if (discriminator == "SafeObject" && dict["_v"] is Dictionary<string, object> innerDict)
                    {
                        if (innerDict.ContainsKey("_type_") && innerDict.ContainsKey("_value_"))
                        {
                            return new SafeObject
                            {
                                Type = innerDict["_type_"].ToString(),
                                Value = innerDict["_value_"]
                            };
                        }
                    }

                    // Otherwise create SafeObject from the wrapped value
                    return new SafeObject
                    {
                        Type = discriminator,
                        Value = dict["_v"]
                    };
                }
            }

            return SafeObject.FromValue(value);
        }

        private static Dictionary<string, SafeObject> ConvertComplexObject(object obj)
        {
            var result = new Dictionary<string, SafeObject>();
            var properties = obj.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0);

            foreach (var property in properties)
            {
                try
                {
                    // Skip problematic properties that are known to cause issues
                    if (IsProblematicProperty(property))
                        continue;

                    var value = property.GetValue(obj);
                    var key = GetPropertyKey(property);
                    result[key] = FromValueInternal(value);
                }
                catch (Exception)
                {
                    // Skip problematic properties
                }
            }

            return result;
        }

        private object ReconstructCollection(List<SafeObject> safeObjects, Type targetType)
        {
            var elementType = targetType.IsArray ? targetType.GetElementType() :
                             targetType.IsGenericType ? targetType.GetGenericArguments()[0] : typeof(object);

            var items = safeObjects.Select(so => so.ToValue(elementType)).ToList();

            if (targetType.IsArray)
            {
                var array = Array.CreateInstance(elementType, items.Count);
                for (int i = 0; i < items.Count; i++)
                    array.SetValue(items[i], i);
                return array;
            }

            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>))
            {
                var listType = typeof(List<>).MakeGenericType(elementType);
                var list = Activator.CreateInstance(listType);
                var addMethod = listType.GetMethod("Add");
                foreach (var item in items)
                    addMethod.Invoke(list, new[] { item });
                return list;
            }

            // Handle IEnumerable<T>, ICollection<T>, IList<T>
            if (targetType.IsGenericType)
            {
                var genericTypeDefinition = targetType.GetGenericTypeDefinition();

                if (genericTypeDefinition == typeof(IEnumerable<>) ||
                    genericTypeDefinition == typeof(ICollection<>) ||
                    genericTypeDefinition == typeof(IList<>))
                {
                    var listType = typeof(List<>).MakeGenericType(elementType);
                    var list = Activator.CreateInstance(listType);
                    var addMethod = listType.GetMethod("Add");
                    foreach (var item in items)
                        addMethod.Invoke(list, new[] { item });
                    return list;
                }
            }

            // Fallback: try to create the target type directly if it has a constructor that accepts IEnumerable
            try
            {
                return Activator.CreateInstance(targetType, items);
            }
            catch
            {
                return items;
            }
        }

        private object ReconstructComplexObject(Dictionary<string, SafeObject> safeDict, Type targetType)
        {
            var instance = Activator.CreateInstance(targetType);
            var properties = targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite);

            foreach (var property in properties)
            {
                var key = GetPropertyKey(property);
                if (safeDict.TryGetValue(key, out var safeValue))
                {
                    try
                    {
                        var value = safeValue.ToValue(property.PropertyType);
                        property.SetValue(instance, value);
                    }
                    catch (Exception)
                    {
                        // Skip problematic properties
                    }
                }
            }

            return instance;
        }

        private static bool IsSimpleType(Type type)
        {
            return type.IsPrimitive ||
                   type.IsEnum ||
                   type == typeof(string) ||
                   type == typeof(decimal) ||
                   type == typeof(DateTime) ||
                   type == typeof(DateTimeOffset) ||
                   type == typeof(TimeSpan);
        }

        private static bool IsProblematicType(Type type)
        {
            return type == typeof(Type) ||
                   type.IsSubclassOf(typeof(Type)) ||
                   type == typeof(System.Reflection.Assembly) ||
                   type.IsSubclassOf(typeof(System.Reflection.MemberInfo)) ||
                   type == typeof(System.Threading.Thread) ||
                   type.Name.Contains("RuntimeType") ||
                   type.Namespace?.StartsWith("System.Reflection") == true;
        }

        private static bool IsProblematicProperty(PropertyInfo property)
        {
            var problematicProperties = new HashSet<string>
            {
                "DeclaringType", "ReflectedType", "Module", "Assembly",
                "BaseType", "UnderlyingSystemType", "TypeHandle", "TypeInitializer"
            };

            return problematicProperties.Contains(property.Name) ||
                   IsProblematicType(property.PropertyType);
        }

        private static object GetSafeRepresentation(object value, Type type)
        {
            try
            {
                if (value is Type typeValue)
                {
                    return new Dictionary<string, object>
                    {
                        ["Name"] = typeValue.Name ?? "Unknown",
                        ["FullName"] = typeValue.FullName ?? "Unknown",
                        ["Namespace"] = typeValue.Namespace ?? "Unknown",
                        ["IsGeneric"] = typeValue.IsGenericType
                    };
                }

                if (value is Exception ex)
                {
                    return new Dictionary<string, object>
                    {
                        ["Message"] = ex.Message ?? "Unknown error",
                        ["Type"] = ex.GetType().Name,
                        ["Source"] = ex.Source ?? "Unknown",
                        ["HResult"] = ex.HResult
                    };
                }

                return value.ToString() ?? "[Object]";
            }
            catch
            {
                return $"[{type.Name}]";
            }
        }

        private static string GetPropertyKey(PropertyInfo property)
        {
            // Check for JsonPropertyName attribute
            var jsonAttr = property.GetCustomAttribute<JsonPropertyNameAttribute>();
            if (jsonAttr != null && !string.IsNullOrEmpty(jsonAttr.Name))
                return jsonAttr.Name;

            // Check for BsonElement attribute
            var bsonAttr = property.GetCustomAttribute<BsonElementAttribute>();
            if (bsonAttr != null && !string.IsNullOrEmpty(bsonAttr.ElementName))
                return bsonAttr.ElementName;

            // Default to camelCase
            return char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1);
        }

        private static Type ResolveType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;

            // Handle special cases
            if (typeName == "System.Guid")
                return typeof(Guid);
            if (typeName == "System.Nullable`1[[System.Guid]]")
                return typeof(Guid?);

            // Try to get the type directly
            var type = System.Type.GetType(typeName);
            if (type != null)
                return type;

            // Try from all loaded assemblies
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName);
                if (type != null)
                    return type;
            }

            return null;
        }
    }
}