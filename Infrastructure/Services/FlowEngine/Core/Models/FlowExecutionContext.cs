using Infrastructure.Services.FlowEngine.Engine;
using Infrastructure.Utilities;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    public class FlowExecutionContext
    {
        public Flow Flow { get; set; }
        public FlowState State { get; set; }
        public FlowDefinition Definition { get; set; }
        public FlowStep CurrentStep { get; set; }
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
            if (!State.Data.ContainsKey(key))
                return default(T);

            var safeObject = State.Data[key];

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
            State.Data[key] = SafeObject.FromValue(value);
        }

        /// <summary>
        /// Checks if the flow contains data for the specified key
        /// </summary>
        /// <param name="key">The data key to check</param>
        /// <returns>True if the key exists, false otherwise</returns>
        public bool HasData(string key)
        {
            return State.Data.ContainsKey(key);
        }
    }
}