using Infrastructure.Utilities;

public static class SafeObjectExtensions
{
    /// <summary>
    /// Convert dictionary to SafeObject dictionary (non-pooled)
    /// </summary>
    public static Dictionary<string, SafeObject> ToSafe(this Dictionary<string, object> source)
    {
        if (source == null) return new Dictionary<string, SafeObject>();

        return source.ToDictionary(
            kvp => kvp.Key,
            kvp => SafeObject.FromValue(kvp.Value) // Direct allocation
        );
    }

    /// <summary>
    /// Convert SafeObject dictionary back to regular dictionary
    /// </summary>
    public static Dictionary<string, object> FromSafe(this Dictionary<string, SafeObject> source)
    {
        if (source == null) return new Dictionary<string, object>();

        return source.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value?.ToValue() ?? (object)null
        );
    }
}