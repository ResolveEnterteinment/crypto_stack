using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    /// <summary>
    /// Serializable branch state
    /// </summary>
    public class BranchState
    {
        public bool IsDefault { get; set; }
        public List<StepState> Steps { get; set; } = new();
    }
}
