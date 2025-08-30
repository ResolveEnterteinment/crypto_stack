using Infrastructure.Services.FlowEngine.Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    public class StepData
    {
        public string Name { get; set; }
        public StepStatus Status { get; set; }
        public List<string> StepDependencies { get; set; }
        public Dictionary<string, string> DataDependencies { get; set; }
        public List<FlowBranch> Branches { get; set; } = [];
        public StepResult? Result { get; set; }
        public int MaxRetries { get; set; }
        public TimeSpan RetryDelay { get; set; }
        public TimeSpan? Timeout { get; set; }
        public bool IsCritical { get; set; }
        public bool IsIdempotent { get; set; }
        public bool CanRunInParallel { get; set; }
        public string? JumpTo { get; set; }
        public int? MaxJumps { get; set; } = null;
        public int CurrentJumps { get; set; } = 0;

        public StepData() { }

        public StepData(FlowStep step)
        {
            Name = step.Name;
            StepDependencies = step.StepDependencies;
            DataDependencies = step.DataDependencies?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.FullName);
            Branches = step.Branches;
            Result = step.Result;
            MaxRetries = step.MaxRetries;
            RetryDelay = step.RetryDelay;
            Timeout = step.Timeout;
            IsCritical = step.IsCritical;
            IsIdempotent = step.IsIdempotent;
            CanRunInParallel = step.CanRunInParallel;
            Status = step.Status;
            JumpTo = step.JumpTo;
            MaxJumps = step.MaxJumps;
            CurrentJumps = step.CurrentJumps;
        }
    }
}
