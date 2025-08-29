using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;
using System.Reflection;
using System.Collections;
using MongoDB.Bson;

namespace Infrastructure.Utilities
{
    /// <summary>
    /// Backward compatible SafeObject that can read old _t/_v format
    /// but writes new type/val format
    /// </summary>
    public class SafeObject
    {
        /// <summary>
        /// Type information - using "type" to avoid MongoDB's reserved "_t"
        /// </summary>
        [BsonElement("_type_")]
        [JsonPropertyName("_type_")]
        public string Type { get; set; }

        /// <summary>
        /// The actual value - using "val" instead of "_v" for consistency
        /// </summary>
        [BsonElement("_value_")]
        [JsonPropertyName("_value_")]
        public object Value { get; set; }

        /// <summary>
        /// Creates a SafeObject for a simple value (always uses new format)
        /// </summary>
        public static SafeObject FromValue(object value)
        {
            if (value == null)
                return new SafeObject { Type = "null", Value = null };

            var type = value.GetType();

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

            // Handle simple types directly
            if (IsSimpleType(type))
            {
                return new SafeObject
                {
                    Type = type.FullName,
                    Value = value
                };
            }

            // Handle collections
            if (value is IEnumerable enumerable && type != typeof(string))
            {
                var items = new List<SafeObject>();
                foreach (var item in enumerable)
                {
                    items.Add(FromValue(item));
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

            // Reconstruct GUIDs from strings
            if (Type == "System.Guid" && Value is string guidString)
            {
                return Guid.Parse(guidString);
            }

            // Handle nullable GUIDs
            if (Type == "System.Nullable`1[[System.Guid]]")
            {
                if (Value == null)
                    return null;
                if (Value is string nullableGuidString)
                    return Guid.Parse(nullableGuidString);
            }

            var actualType = targetType ?? ResolveType(Type);
            if (actualType == null)
                throw new InvalidOperationException($"Cannot resolve type: {Type}");

            // Handle simple types
            if (IsSimpleType(actualType))
            {
                // Handle BsonDateTime for DateTime values
                if (Value is BsonDateTime bsonDate)
                {
                    return Convert.ChangeType(bsonDate.ToUniversalTime(), actualType);
                }
                return Convert.ChangeType(Value, actualType);
            }

            // Handle collections
            if (Value is List<SafeObject> safeObjects && typeof(IEnumerable).IsAssignableFrom(actualType))
            {
                return ReconstructCollection(safeObjects, actualType);
            }

            // Handle collections from MongoDB (might be BsonArray)
            if (Value is BsonArray bsonArray && typeof(IEnumerable).IsAssignableFrom(actualType))
            {
                var items = new List<SafeObject>();
                foreach (var item in bsonArray)
                {
                    if (item is BsonDocument doc)
                    {
                        var so = new SafeObject();
                        if (doc.Contains("type"))
                            so.Type = doc["type"].AsString;
                        else if (doc.Contains("_t"))
                            so.Type = doc["_t"].AsString;

                        if (doc.Contains("val"))
                            so.Value = BsonTypeMapper.MapToDotNetValue(doc["val"]);
                        else if (doc.Contains("_v"))
                            so.Value = BsonTypeMapper.MapToDotNetValue(doc["_v"]);

                        items.Add(so);
                    }
                    else
                    {
                        // Simple value
                        items.Add(SafeObject.FromValue(BsonTypeMapper.MapToDotNetValue(item)));
                    }
                }
                return ReconstructCollection(items, actualType);
            }

            // Handle complex objects
            if (Value is Dictionary<string, SafeObject> safeDict)
            {
                return ReconstructComplexObject(safeDict, actualType);
            }

            // Handle BsonDocument
            if (Value is BsonDocument bsonDoc)
            {
                var dict = new Dictionary<string, SafeObject>();
                foreach (var element in bsonDoc)
                {
                    var val = BsonTypeMapper.MapToDotNetValue(element.Value);
                    if (val is BsonDocument subdoc && (subdoc.Contains("type") || subdoc.Contains("_t")))
                    {
                        var so = new SafeObject();
                        if (subdoc.Contains("type"))
                            so.Type = subdoc["type"].AsString;
                        else if (subdoc.Contains("_t"))
                            so.Type = subdoc["_t"].AsString;

                        if (subdoc.Contains("val"))
                            so.Value = BsonTypeMapper.MapToDotNetValue(subdoc["val"]);
                        else if (subdoc.Contains("_v"))
                            so.Value = BsonTypeMapper.MapToDotNetValue(subdoc["_v"]);

                        dict[element.Name] = so;
                    }
                    else
                    {
                        dict[element.Name] = SafeObject.FromValue(val);
                    }
                }
                return ReconstructComplexObject(dict, actualType);
            }

            throw new InvalidOperationException($"Cannot reconstruct value of type {Type}");
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
                    var value = property.GetValue(obj);
                    var key = GetPropertyKey(property);
                    result[key] = FromValue(value);
                }
                catch (Exception)
                {
                    // Skip problematic properties
                }
            }

            return result;
        }

        private static string GetPropertyKey(PropertyInfo property)
        {
            // Check for JsonPropertyName attribute
            var jsonAttr = property.GetCustomAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>();
            if (jsonAttr != null && !string.IsNullOrEmpty(jsonAttr.Name))
                return jsonAttr.Name;

            // Check for BsonElement attribute
            var bsonAttr = property.GetCustomAttribute<MongoDB.Bson.Serialization.Attributes.BsonElementAttribute>();
            if (bsonAttr != null && !string.IsNullOrEmpty(bsonAttr.ElementName))
                return bsonAttr.ElementName;

            // Default to camelCase
            return char.ToLowerInvariant(property.Name[0]) + property.Name.Substring(1);
        }

        private object ReconstructCollection(List<SafeObject> safeObjects, Type targetType)
        {
            var elementType = targetType.IsArray ? targetType.GetElementType() :
                             targetType.IsGenericType ? targetType.GetGenericArguments()[0] : typeof(object);

            var items = safeObjects.Select(so =>
            {
                return so.ToValue(elementType);
            }).ToList();

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

            return items;
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