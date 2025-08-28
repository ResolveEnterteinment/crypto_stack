using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Core.Enums
{
    public enum StepStatus
    {
        Pending,
        Skipped,
        InProgress,
        Cancelled,
        Paused,
        Failed,
        Completed,
    }
}
