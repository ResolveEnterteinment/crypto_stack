using Infrastructure.Utilities;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    public class FlowContext
    {
        public FlowDefinition Flow { get; set; }
        public FlowStep CurrentStep { get; set; }
        public Dictionary<string, SafeObject> StepData { get; set; } = new();  // CHANGED: Use SafeObject
        public CancellationToken CancellationToken { get; set; }
        public IServiceProvider Services { get; set; }

        /// <summary>
        /// Gets typed data from the flow's data dictionary
        /// </summary>
        /// <typeparam name="T">The target type to reconstruct</typeparam>
        /// <param name="key">The data key</param>
        /// <returns>The reconstructed typed value or default(T) if not found</returns>
        public T GetData<T>(string key)
        {
            if (!Flow.Data.ContainsKey(key))
                return default(T);

            var safeObject = Flow.Data[key];

            // Use SafeObject's built-in type reconstruction
            return safeObject.ToValue<T>();
        }

        /// <summary>
        /// Sets data in the flow's data dictionary as a SafeObject
        /// </summary>
        /// <param name="key">The data key</param>
        /// <param name="value">The value to store</param>
        public void SetData(string key, object value)
        {
            Flow.Data[key] = SafeObject.FromValue(value);
        }

        /// <summary>
        /// Gets typed data from the current step's local data dictionary
        /// </summary>
        /// <typeparam name="T">The target type to reconstruct</typeparam>
        /// <param name="key">The data key</param>
        /// <returns>The reconstructed typed value or default(T) if not found</returns>
        public T GetStepData<T>(string key)
        {
            if (!StepData.ContainsKey(key))
                return default(T);

            var safeObject = StepData[key];
            return safeObject.ToValue<T>();
        }

        /// <summary>
        /// Sets data in the current step's local data dictionary as a SafeObject
        /// </summary>
        /// <param name="key">The data key</param>
        /// <param name="value">The value to store</param>
        public void SetStepData(string key, object value)
        {
            StepData[key] = SafeObject.FromValue(value);
        }

        /// <summary>
        /// Checks if the flow contains data for the specified key
        /// </summary>
        /// <param name="key">The data key to check</param>
        /// <returns>True if the key exists, false otherwise</returns>
        public bool HasData(string key)
        {
            return Flow.Data.ContainsKey(key);
        }

        /// <summary>
        /// Checks if the current step contains data for the specified key
        /// </summary>
        /// <param name="key">The data key to check</param>
        /// <returns>True if the key exists, false otherwise</returns>
        public bool HasStepData(string key)
        {
            return StepData.ContainsKey(key);
        }

        /// <summary>
        /// Gets data with a fallback value if the key doesn't exist
        /// </summary>
        /// <typeparam name="T">The target type to reconstruct</typeparam>
        /// <param name="key">The data key</param>
        /// <param name="fallback">The fallback value to return if key doesn't exist</param>
        /// <returns>The reconstructed typed value or the fallback value</returns>
        public T GetDataOrDefault<T>(string key, T fallback = default(T))
        {
            if (!Flow.Data.ContainsKey(key))
                return fallback;

            try
            {
                var safeObject = Flow.Data[key];
                return safeObject.ToValue<T>();
            }
            catch
            {
                return fallback;
            }
        }

        /// <summary>
        /// Gets step data with a fallback value if the key doesn't exist
        /// </summary>
        /// <typeparam name="T">The target type to reconstruct</typeparam>
        /// <param name="key">The data key</param>
        /// <param name="fallback">The fallback value to return if key doesn't exist</param>
        /// <returns>The reconstructed typed value or the fallback value</returns>
        public T GetStepDataOrDefault<T>(string key, T fallback = default(T))
        {
            if (!StepData.ContainsKey(key))
                return fallback;

            try
            {
                var safeObject = StepData[key];
                return safeObject.ToValue<T>();
            }
            catch
            {
                return fallback;
            }
        }
    }
}