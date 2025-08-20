using System.Diagnostics;

namespace Infrastructure.Services.FlowEngine.Core
{
    /// <summary>
    /// Activity source for distributed tracing
    /// </summary>
    public static class FlowEngineActivity
    {
        private static readonly ActivitySource ActivitySource = new("FlowEngine");

        public static Activity? StartActivity(string name)
        {
            return ActivitySource.StartActivity(name);
        }
    }
}
