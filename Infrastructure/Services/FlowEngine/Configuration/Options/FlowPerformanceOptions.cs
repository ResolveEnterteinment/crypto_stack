using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Configuration.Options
{
    public class FlowPerformanceOptions
    {
        public int MaxConcurrentFlows { get; set; } = 100;
        public TimeSpan AutoSaveInterval { get; set; } = TimeSpan.FromMinutes(1);
        public bool EnableMetrics { get; set; } = true;
        public bool EnableCaching { get; set; } = true;
    }
}
