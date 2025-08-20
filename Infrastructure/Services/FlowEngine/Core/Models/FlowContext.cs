namespace Infrastructure.Services.FlowEngine.Core.Models
{
    public class FlowContext
    {
        public FlowDefinition Flow { get; set; }
        public FlowStep CurrentStep { get; set; }
        public Dictionary<string, object> StepData { get; set; } = new();
        public CancellationToken CancellationToken { get; set; }
        public IServiceProvider Services { get; set; }

        public T GetData<T>(string key) => Flow.Data.ContainsKey(key) ? (T)Flow.Data[key] : default(T);
        public void SetData(string key, object value) => Flow.Data[key] = value;
    }
}
