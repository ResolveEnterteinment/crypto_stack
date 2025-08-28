using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;
using System.Reflection;
using System.Collections;

namespace Infrastructure.Utilities
{
    /// <summary>
    /// Represents a type-safe serializable object with embedded type information
    /// </summary>
    public class SafeObject
    {
        /// <summary>
        /// Type information - stored as string for MongoDB compatibility
        /// </summary>
        [BsonElement("_t")]
        [JsonPropertyName("_t")]
        public string Type { get; set; }

        /// <summary>
        /// The actual value - can be primitive, collection, or nested SafeObject structure
        /// </summary>
        [BsonElement("_v")]
        [JsonPropertyName("_v")]
        public object Value { get; set; }

        /// <summary>
        /// Creates a SafeObject for a simple value
        /// </summary>
        public static SafeObject FromValue(object value)
        {
            if (value == null)
                return new SafeObject { Type = "null", Value = null };

            var type = value.GetType();

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

            var actualType = targetType ?? ResolveType(Type);
            if (actualType == null)
                throw new InvalidOperationException($"Cannot resolve type: {Type}");

            // Handle simple types
            if (IsSimpleType(actualType))
            {
                return Convert.ChangeType(Value, actualType);
            }

            // Handle collections
            if (Value is List<SafeObject> safeObjects && typeof(IEnumerable).IsAssignableFrom(actualType))
            {
                return ReconstructCollection(safeObjects, actualType);
            }

            // Handle complex objects
            if (Value is Dictionary<string, SafeObject> safeDict)
            {
                return ReconstructComplexObject(safeDict, actualType);
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
                   type == typeof(TimeSpan) ||
                   type == typeof(Guid) ||
                   (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>) &&
                    IsSimpleType(Nullable.GetUnderlyingType(type)));
        }

        private static Type ResolveType(string typeInfo)
        {
            if (string.IsNullOrEmpty(typeInfo)) return null;

            try
            {
                // Try to get type from current app domain first
                var type = System.Type.GetType(typeInfo);
                if (type != null) return type;

                // Search through all loaded assemblies
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    type = assembly.GetType(typeInfo);
                    if (type != null) return type;
                }
            }
            catch
            {
                // Type resolution failed
            }

            return null;
        }
    }
}