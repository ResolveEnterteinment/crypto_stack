using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Concurrency
{
    public sealed record ConcurrencyStatus
    {
        public int MaxConcurrentFlows { get; init; }
        public int ActiveFlowCount { get; init; }
        public int AvailableSlots => MaxConcurrentFlows - ActiveFlowCount;
        public Dictionary<string, DateTime> ActiveFlows { get; init; } = new();
    }
}
