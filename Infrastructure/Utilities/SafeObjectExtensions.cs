using Microsoft.Extensions.Logging;

namespace Infrastructure.Utilities
{
    public static class SafeObjectExtensions
    {
        /// <summary>
        /// Converts any object to a SafeObject-based dictionary
        /// </summary>
        public static Dictionary<string, SafeObject> ToSafe(this Dictionary<string, object> dict, ILogger logger = null)
        {
            if (dict == null) return new Dictionary<string, SafeObject>();

            var result = new Dictionary<string, SafeObject>();

            foreach (var kvp in dict)
            {
                try
                {
                    result[kvp.Key] = SafeObject.FromValue(kvp.Value);
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to convert value for key {Key}", kvp.Key);
                    result[kvp.Key] = SafeObject.FromValue($"__conversion_error__: {ex.Message}");
                }
            }

            return result;
        }

        /// <summary>
        /// Reconstructs a Dictionary<string, object> from SafeObject dictionary
        /// </summary>
        public static Dictionary<string, object> FromSafe(this Dictionary<string, SafeObject> safeDict, ILogger logger = null)
        {
            if (safeDict == null) return new Dictionary<string, object>();

            var result = new Dictionary<string, object>();

            foreach (var kvp in safeDict)
            {
                try
                {
                    result[kvp.Key] = kvp.Value.ToValue();
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to reconstruct value for key {Key}", kvp.Key);
                    result[kvp.Key] = null;
                }
            }

            return result;
        }

        /// <summary>
        /// Converts any object to SafeObject
        /// </summary>
        public static SafeObject ToSafeObject(this object obj)
        {
            return SafeObject.FromValue(obj);
        }

        /// <summary>
        /// Converts any object to a SafeObject-based dictionary (legacy compatibility)
        /// </summary>
        public static Dictionary<string, SafeObject> ToMongoSafe(this Dictionary<string, object> dict, ILogger logger = null)
        {
            return dict.ToSafe(logger);
        }

        /// <summary>
        /// Reconstructs from SafeObject dictionary (legacy compatibility)
        /// </summary>
        public static Dictionary<string, object> FromMongoSafe<T>(this Dictionary<string, SafeObject> safeDict, ILogger logger = null)
        {
            return safeDict.FromSafe(logger);
        }
    }
}