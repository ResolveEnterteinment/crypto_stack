using Infrastructure.Services.FlowEngine.Engine;
using Infrastructure.Utilities;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    public class FlowExecutionContext
    {
        public Flow Flow { get; set; }
        public Dictionary<string, object> RuntimeStore { get; set; } = new Dictionary<string, object>();
        public FlowState State => Flow.State;
        public FlowDefinition Definition => Flow.Definition;
        public CancellationToken CancellationToken => Flow.CancellationToken;
        public FlowStep CurrentStep { get; set; }
        public IServiceProvider Services { get; set; }

        /// <summary>
        /// Gets typed data from the flow's data dictionary
        /// </summary>
        /// <typeparam name="T">The target type to reconstruct</typeparam>
        /// <param name="key">The data key</param>
        /// <returns>The reconstructed typed value or default(T) if not found</returns>
        public T? GetData<T>(string key)
        {
            return State.GetData<T>(key);
        }

        /// <summary>
        /// Sets data in the flow's data dictionary as a SafeObject
        /// </summary>
        /// <param name="key">The data key</param>
        /// <param name="value">The value to store</param>
        public void SetData(string key, object value)
        {
            State.SetData(key, value);
        }

        /// <summary>
        /// Gets a runtime value from the RuntimeStore dictionary
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <returns></returns>
        public T? GetRuntime<T> (string key)
        {
            if (RuntimeStore.TryGetValue(key, out object? value) && value is T tValue)
            {
                return tValue;
            }
            return default;
        }

        /// <summary>
        /// Sets a runtime value associated with the specified key.
        /// </summary>
        /// <remarks>If the specified key already exists in the runtime store, its value will be
        /// overwritten.</remarks>
        /// <param name="key">The key used to identify the runtime value. Cannot be <see langword="null"/> or empty.</param>
        /// <param name="value">The value to associate with the specified key. Can be <see langword="null"/>.</param>
        public void SetRuntime(string key, object value)
        {
            RuntimeStore[key] = value;
        }

        /// <summary>
        /// Checks if the flow contains data for the specified key
        /// </summary>
        /// <param name="key">The data key to check</param>
        /// <returns>True if the key exists, false otherwise</returns>
        public bool HasData(string key)
        {
            return State.Data.ContainsKey(key) && State.Data[key].ToValue() != null;
        }
    }
}