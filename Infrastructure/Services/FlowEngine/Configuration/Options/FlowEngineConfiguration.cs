using Infrastructure.Services.FlowEngine.Core.Enums;

namespace Infrastructure.Services.FlowEngine.Configuration.Options
{
    public class FlowEngineConfiguration
    {
        public PersistenceType PersistenceType { get; set; } = PersistenceType.InMemory;
        public string ConnectionString { get; set; }
        public string DatabaseName { get; set; } = "FlowEngine";
        public TimeSpan RecoveryInterval { get; set; } = TimeSpan.FromMinutes(5);
        public bool EnableStartupRecovery { get; set; } = true;
        public List<Type> GlobalMiddleware { get; set; } = new();
        public FlowSecurityOptions Security { get; set; } = new();
        public FlowPerformanceOptions Performance { get; set; } = new();
    }
}
