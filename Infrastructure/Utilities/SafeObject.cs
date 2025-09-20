using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Infrastructure.Utilities
{
    /// <summary>
    /// High-performance polymorphic object storage with direct allocation
    /// - No object pooling (eliminates cross-context issues)
    /// - Binary serialization for primitives
    /// - JSON serialization for complex objects
    /// - Simplified type resolution
    /// </summary>
    [BsonDiscriminator("SafeObject")]
    public sealed class SafeObject
    {
        // Simplified caching - no weak references needed with direct allocation
        private static readonly ConcurrentDictionary<Type, TypeInfo> _typeCache = new();

        /// <summary>
        /// Type discriminator (1 byte instead of full type name)
        /// </summary>
        [BsonElement("t")]
        [JsonPropertyName("t")]
        public TypeDiscriminator TypeCode { get; set; }

        /// <summary>
        /// The serialized value - can be primitive, byte[], or JSON string
        /// </summary>
        [BsonElement("v")]
        [JsonPropertyName("v")]
        public object Value { get; set; }

        /// <summary>
        /// Type hint for complex types (full type name for reliability)
        /// </summary>
        [BsonElement("h")]
        [JsonPropertyName("h")]
        [BsonIgnoreIfNull]
        public string TypeHint { get; set; }

        /// <summary>
        /// Compatibility property for TypeName - maps to TypeHint
        /// </summary>
        [BsonIgnore]
        [JsonIgnore]
        public string TypeName
        {
            get => TypeHint;
            set => TypeHint = value;
        }

        /// <summary>
        /// Gets the actual type of the stored value
        /// </summary>
        [BsonIgnore]
        [JsonIgnore]
        public string Type
        {
            get
            {
                try
                {
                    if (TypeCode == TypeDiscriminator.Null)
                        return "null";

                    // FIXED: Handle JSON-serialized complex types first before primitive check
                    if (TypeCode == TypeDiscriminator.Json || TypeCode >= TypeDiscriminator.Complex)
                    {
                        // For JSON-serialized complex types, resolve from type hint
                        if (!string.IsNullOrEmpty(TypeHint))
                        {
                            try
                            {
                                var resolvedType = ResolveTypeName(TypeHint);
                                return resolvedType?.FullName ?? TypeHint;
                            }
                            catch
                            {
                                return TypeHint;
                            }
                        }
                        return "object"; // Fallback for complex types without type hint
                    }
                    else if (TypeCode < TypeDiscriminator.Json)
                    {
                        // For built-in primitive types, return the type name directly
                        var typeInfo = GetBuiltInTypeInfo(TypeCode);
                        return typeInfo.Type.FullName ?? typeInfo.Type.Name;
                    }

                    return "object"; // Final fallback
                }
                catch
                {
                    // Return a safe fallback if anything goes wrong
                    return $"SafeObject[{TypeCode}]";
                }
            }
        }

        /// <summary>
        /// Public constructor - no pooling
        /// </summary>
        public SafeObject() { }

        /// <summary>
        /// Creates a SafeObject from any value using the most efficient serialization method
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SafeObject FromValue(object value)
        {
            var safeObj = new SafeObject(); // Direct allocation - no pooling

            if (value == null)
            {
                safeObj.TypeCode = TypeDiscriminator.Null;
                safeObj.Value = null;
                safeObj.TypeHint = null;
                return safeObj;
            }

            try
            {
                var type = value.GetType();
                var typeInfo = GetOrCreateTypeInfo(type);

                safeObj.TypeCode = typeInfo.Discriminator;
                safeObj.Value = typeInfo.Serializer.Serialize(value);
                safeObj.TypeHint = typeInfo.RequiresTypeHint ? type.AssemblyQualifiedName : null;

                return safeObj;
            }
            catch (Exception ex)
            {
                // Log the error and create a safe representation
                System.Diagnostics.Debug.WriteLine($"SafeObject.FromValue failed for type {value?.GetType()?.FullName}: {ex.Message}");

                // Create a fallback representation
                safeObj.TypeCode = TypeDiscriminator.Json;
                safeObj.TypeHint = value?.GetType()?.AssemblyQualifiedName ?? "Unknown";

                try
                {
                    // Try a simple JSON serialization as fallback
                    safeObj.Value = JsonSerializer.Serialize(new
                    {
                        _error = "Serialization failed",
                        _originalType = value?.GetType()?.FullName,
                        _errorMessage = ex.Message,
                        _stringRepresentation = value?.ToString()
                    });
                }
                catch
                {
                    // Last resort - store error message
                    safeObj.Value = $"{{\"_error\":\"Complete serialization failure\",\"_type\":\"{value?.GetType()?.FullName}\"}}";
                }

                return safeObj;
            }
        }

        /// <summary>
        /// Reconstructs the original typed object
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T ToValue<T>()
        {
            return (T)ToValue(typeof(T));
        }

        /// <summary>
        /// Reconstructs the original object with optional target type
        /// </summary>
        public object ToValue(Type targetType = null)
        {
            if (TypeCode == TypeDiscriminator.Null)
                return null;

            TypeInfo typeInfo;
            try
            {
                if (targetType != null)
                {
                    typeInfo = GetOrCreateTypeInfo(targetType);
                }
                // FIXED: Handle JSON-serialized complex types first
                else if (TypeCode == TypeDiscriminator.Json || TypeCode >= TypeDiscriminator.Complex)
                {
                    // Complex type with type hint
                    if (TypeHint != null)
                    {
                        var resolvedType = ResolveTypeName(TypeHint);
                        if (resolvedType == null)
                        {
                            // If type resolution fails, return the value as-is
                            return Value;
                        }
                        typeInfo = GetOrCreateTypeInfo(resolvedType);
                    }
                    else
                    {
                        // No type information available, return value as-is
                        return Value;
                    }
                }
                else if (TypeCode < TypeDiscriminator.Json)
                {
                    // Fast path for built-in primitive types
                    typeInfo = GetBuiltInTypeInfo(TypeCode);
                }
                else
                {
                    // Fallback: return value as-is
                    return Value;
                }

                return typeInfo.Serializer.Deserialize(Value, typeInfo.Type);
            }
            catch (Exception ex)
            {
                // Log the error but don't throw - return a safe fallback
                System.Diagnostics.Debug.WriteLine($"SafeObject.ToValue failed: {ex.Message}");
                return Value; // Return raw value as fallback
            }
        }

        // Simplified type resolution and caching
        private static TypeInfo GetOrCreateTypeInfo(Type type)
        {
            return _typeCache.GetOrAdd(type, t => new TypeInfo(t));
        }

        private static TypeInfo GetBuiltInTypeInfo(TypeDiscriminator discriminator)
        {
            var type = discriminator switch
            {
                TypeDiscriminator.Null => typeof(object),
                TypeDiscriminator.String => typeof(string),
                TypeDiscriminator.Int32 => typeof(int),
                TypeDiscriminator.Int64 => typeof(long),
                TypeDiscriminator.Decimal => typeof(decimal),
                TypeDiscriminator.Double => typeof(double),
                TypeDiscriminator.Float => typeof(float),
                TypeDiscriminator.Boolean => typeof(bool),
                TypeDiscriminator.DateTime => typeof(DateTime),
                TypeDiscriminator.Guid => typeof(Guid),
                TypeDiscriminator.ByteArray => typeof(byte[]),
                TypeDiscriminator.TimeSpan => typeof(TimeSpan),
                TypeDiscriminator.Json => typeof(object),
                _ => throw new ArgumentException($"Not a built-in type: {discriminator}")
            };

            return GetOrCreateTypeInfo(type);
        }

        private static Type ResolveTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;

            try
            {
                // Try direct resolution first (works for AssemblyQualifiedName)
                var type = System.Type.GetType(typeName);
                if (type != null)
                    return type;

                // Try loaded assemblies with exact name match
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        // Try with exact type name first
                        type = assembly.GetType(typeName);
                        if (type != null)
                            return type;

                        // For shortened type names, try appending assembly name
                        if (!typeName.Contains(',') && !typeName.Contains('.'))
                        {
                            foreach (var exportedType in assembly.GetExportedTypes())
                            {
                                if (exportedType.Name.Equals(typeName, StringComparison.Ordinal) ||
                                    exportedType.FullName?.Equals(typeName, StringComparison.Ordinal) == true)
                                {
                                    return exportedType;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Continue searching other assemblies
                    }
                }

                // Last attempt: try to find by FullName across all types
                if (typeName.Contains('.'))
                {
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            var matchingType = assembly.GetTypes()
                                .FirstOrDefault(t => t.FullName?.Equals(typeName, StringComparison.Ordinal) == true);
                            if (matchingType != null)
                                return matchingType;
                        }
                        catch
                        {
                            // Skip assemblies that can't be reflected
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ResolveTypeName failed for '{typeName}': {ex.Message}");
            }

            return null; // Type not found
        }
    }

    /// <summary>
    /// Type discriminators for efficient serialization
    /// </summary>
    public enum TypeDiscriminator : byte
    {
        Null = 0,
        String = 1,
        Int32 = 2,
        Int64 = 3,
        Decimal = 4,
        Double = 5,
        Float = 6,
        Boolean = 7,
        DateTime = 8,
        Guid = 9,
        ByteArray = 10,
        TimeSpan = 11,

        // Collections
        PrimitiveArray = 20,
        ObjectArray = 21,
        List = 22,
        Dictionary = 23,

        // Complex
        Json = 50,
        Complex = 100
    }

    /// <summary>
    /// Cached type information with pre-compiled serializers
    /// </summary>
    internal sealed class TypeInfo
    {
        public Type Type { get; }
        public TypeDiscriminator Discriminator { get; }
        public bool RequiresTypeHint { get; }
        public IValueSerializer Serializer { get; }

        public TypeInfo(Type type)
        {
            Type = type;
            (Discriminator, Serializer, RequiresTypeHint) = CreateSerializer(type);
        }

        private static (TypeDiscriminator, IValueSerializer, bool) CreateSerializer(Type type)
        {
            // Handle nullable types
            var underlyingType = Nullable.GetUnderlyingType(type);
            if (underlyingType != null)
            {
                var (discriminator, serializer, _) = CreateSerializer(underlyingType);
                return (discriminator, new NullableSerializer(serializer), false);
            }

            // Fast path for primitives and common types
            if (type == typeof(string))
                return (TypeDiscriminator.String, StringSerializer.Instance, false);
            if (type == typeof(int))
                return (TypeDiscriminator.Int32, Int32Serializer.Instance, false);
            if (type == typeof(long))
                return (TypeDiscriminator.Int64, Int64Serializer.Instance, false);
            if (type == typeof(decimal))
                return (TypeDiscriminator.Decimal, DecimalSerializer.Instance, false);
            if (type == typeof(double))
                return (TypeDiscriminator.Double, DoubleSerializer.Instance, false);
            if (type == typeof(float))
                return (TypeDiscriminator.Float, FloatSerializer.Instance, false);
            if (type == typeof(bool))
                return (TypeDiscriminator.Boolean, BooleanSerializer.Instance, false);
            if (type == typeof(DateTime))
                return (TypeDiscriminator.DateTime, DateTimeSerializer.Instance, false);
            if (type == typeof(Guid))
                return (TypeDiscriminator.Guid, GuidSerializer.Instance, false);
            if (type == typeof(byte[]))
                return (TypeDiscriminator.ByteArray, ByteArraySerializer.Instance, false);
            if (type == typeof(TimeSpan))
                return (TypeDiscriminator.TimeSpan, TimeSpanSerializer.Instance, false);

            // Arrays
            if (type.IsArray)
            {
                var elementType = type.GetElementType();
                if (elementType.IsPrimitive || elementType == typeof(decimal))
                {
                    return (TypeDiscriminator.PrimitiveArray, new PrimitiveArraySerializer(type), true);
                }
                return (TypeDiscriminator.ObjectArray, new JsonObjectSerializer(type), true);
            }

            // Collections
            if (typeof(System.Collections.IList).IsAssignableFrom(type))
            {
                return (TypeDiscriminator.List, new JsonObjectSerializer(type), true);
            }

            if (typeof(System.Collections.IDictionary).IsAssignableFrom(type))
            {
                return (TypeDiscriminator.Dictionary, new JsonObjectSerializer(type), true);
            }

            // Default to JSON for complex types
            return (TypeDiscriminator.Json, new JsonObjectSerializer(type), true);
        }
    }

    /// <summary>
    /// High-performance value serializers with zero allocation for primitives
    /// </summary>
    internal interface IValueSerializer
    {
        object Serialize(object value);
        object Deserialize(object serialized, Type targetType);
    }

    internal sealed class StringSerializer : IValueSerializer
    {
        public static readonly StringSerializer Instance = new();
        public object Serialize(object value) => value;
        public object Deserialize(object serialized, Type targetType) => serialized;
    }

    internal sealed class Int32Serializer : IValueSerializer
    {
        public static readonly Int32Serializer Instance = new();
        public object Serialize(object value) => value;
        public object Deserialize(object serialized, Type targetType) =>
            serialized is int i ? i : Convert.ToInt32(serialized);
    }

    internal sealed class Int64Serializer : IValueSerializer
    {
        public static readonly Int64Serializer Instance = new();
        public object Serialize(object value) => value;
        public object Deserialize(object serialized, Type targetType) =>
            serialized is long l ? l : Convert.ToInt64(serialized);
    }

    internal sealed class DecimalSerializer : IValueSerializer
    {
        public static readonly DecimalSerializer Instance = new();
        public object Serialize(object value) => value.ToString(); // Store as string for precision
        public object Deserialize(object serialized, Type targetType) =>
            decimal.Parse(serialized.ToString());
    }

    internal sealed class DoubleSerializer : IValueSerializer
    {
        public static readonly DoubleSerializer Instance = new();
        public object Serialize(object value) => value;
        public object Deserialize(object serialized, Type targetType) =>
            serialized is double d ? d : Convert.ToDouble(serialized);
    }

    internal sealed class FloatSerializer : IValueSerializer
    {
        public static readonly FloatSerializer Instance = new();
        public object Serialize(object value) => value;
        public object Deserialize(object serialized, Type targetType) =>
            serialized is float f ? f : Convert.ToSingle(serialized);
    }

    internal sealed class BooleanSerializer : IValueSerializer
    {
        public static readonly BooleanSerializer Instance = new();
        public object Serialize(object value) => value;
        public object Deserialize(object serialized, Type targetType) =>
            serialized is bool b ? b : Convert.ToBoolean(serialized);
    }

    internal sealed class DateTimeSerializer : IValueSerializer
    {
        public static readonly DateTimeSerializer Instance = new();
        public object Serialize(object value) => ((DateTime)value).ToBinary();
        public object Deserialize(object serialized, Type targetType) =>
            DateTime.FromBinary(Convert.ToInt64(serialized));
    }

    internal sealed class GuidSerializer : IValueSerializer
    {
        public static readonly GuidSerializer Instance = new();
        public object Serialize(object value) => ((Guid)value).ToString("N"); // Compact format
        public object Deserialize(object serialized, Type targetType) =>
            Guid.ParseExact(serialized.ToString(), "N");
    }

    internal sealed class TimeSpanSerializer : IValueSerializer
    {
        public static readonly TimeSpanSerializer Instance = new();
        public object Serialize(object value) => ((TimeSpan)value).Ticks.ToString();
        public object Deserialize(object serialized, Type targetType) =>
            new TimeSpan(long.Parse(serialized.ToString()));
    }

    internal sealed class ByteArraySerializer : IValueSerializer
    {
        public static readonly ByteArraySerializer Instance = new();
        public object Serialize(object value) => value; // Store directly
        public object Deserialize(object serialized, Type targetType) => serialized;
    }

    internal sealed class NullableSerializer : IValueSerializer
    {
        private readonly IValueSerializer _innerSerializer;

        public NullableSerializer(IValueSerializer innerSerializer)
        {
            _innerSerializer = innerSerializer;
        }

        public object Serialize(object value) =>
            value == null ? null : _innerSerializer.Serialize(value);

        public object Deserialize(object serialized, Type targetType) =>
            serialized == null ? null : _innerSerializer.Deserialize(serialized, Nullable.GetUnderlyingType(targetType));
    }

    /// <summary>
    /// JSON serializer for complex objects using System.Text.Json for best performance
    /// </summary>
    internal sealed class JsonObjectSerializer : IValueSerializer
    {
        private static readonly JsonSerializerOptions _options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            IgnoreReadOnlyProperties = false,
            IncludeFields = true,
            WriteIndented = false
        };

        // Safe options for Exception serialization - exclude problematic properties
        private static readonly JsonSerializerOptions _exceptionOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            IgnoreReadOnlyProperties = false,
            IncludeFields = true,
            WriteIndented = false,
            TypeInfoResolver = new ExceptionTypeInfoResolver()
        };

        private readonly Type _type;

        public JsonObjectSerializer(Type type)
        {
            _type = type;
        }

        public object Serialize(object value)
        {
            try
            {
                if (value is Exception)
                {
                    return JsonSerializer.Serialize(value, _type, _exceptionOptions);
                }

                return JsonSerializer.Serialize(value, _type, _options);
            }
            catch (NotSupportedException ex) when (ex.Message.Contains("System.Reflection.MethodBase"))
            {
                if (value is Exception exception)
                {
                    var safeException = CreateSafeExceptionRepresentation(exception);
                    return JsonSerializer.Serialize(safeException, _options);
                }
                throw;
            }
        }

        public object Deserialize(object serialized, Type targetType)
        {
            if (serialized is string json)
            {
                try
                {
                    if (typeof(Exception).IsAssignableFrom(targetType))
                    {
                        return JsonSerializer.Deserialize(json, targetType, _exceptionOptions);
                    }

                    return JsonSerializer.Deserialize(json, targetType, _options);
                }
                catch (JsonException)
                {
                    if (typeof(Exception).IsAssignableFrom(targetType))
                    {
                        return new Exception($"Deserialized exception data: {json}");
                    }
                    throw;
                }
            }

            // Handle BsonString from MongoDB
            if (serialized is BsonString bsonString)
            {
                try
                {
                    if (typeof(Exception).IsAssignableFrom(targetType))
                    {
                        return JsonSerializer.Deserialize(bsonString.Value, targetType, _exceptionOptions);
                    }

                    return JsonSerializer.Deserialize(bsonString.Value, targetType, _options);
                }
                catch (JsonException)
                {
                    if (typeof(Exception).IsAssignableFrom(targetType))
                    {
                        return new Exception($"Deserialized exception data: {bsonString.Value}");
                    }
                    throw;
                }
            }

            throw new InvalidOperationException($"Cannot deserialize {serialized?.GetType()} to {targetType}");
        }

        private static object CreateSafeExceptionRepresentation(Exception exception)
        {
            return new
            {
                Type = exception.GetType().FullName,
                Message = exception.Message,
                StackTrace = exception.StackTrace,
                Source = exception.Source,
                HelpLink = exception.HelpLink,
                HResult = exception.HResult,
                InnerException = exception.InnerException != null
                    ? CreateSafeExceptionRepresentation(exception.InnerException)
                    : null
            };
        }
    }

    /// <summary>
    /// Custom type info resolver that excludes problematic Exception properties
    /// </summary>
    internal sealed class ExceptionTypeInfoResolver : IJsonTypeInfoResolver
    {
        public JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            if (!typeof(Exception).IsAssignableFrom(type))
                return null;

            var typeInfo = JsonTypeInfo.CreateJsonTypeInfo(type, options);

            var propertiesToRemove = new[] { "TargetSite", "Data" };
            foreach (var propertyName in propertiesToRemove)
            {
                var property = typeInfo.Properties.FirstOrDefault(p =>
                    string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));
                if (property != null)
                {
                    typeInfo.Properties.Remove(property);
                }
            }

            return typeInfo;
        }
    }

    /// <summary>
    /// Optimized serializer for primitive arrays
    /// </summary>
    internal sealed class PrimitiveArraySerializer : IValueSerializer
    {
        private readonly Type _elementType;

        public PrimitiveArraySerializer(Type arrayType)
        {
            _elementType = arrayType.GetElementType();
        }

        public object Serialize(object value) => value;
        public object Deserialize(object serialized, Type targetType) => serialized;
    }
}