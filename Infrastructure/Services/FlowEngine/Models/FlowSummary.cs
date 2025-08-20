using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Models
{
    /// <summary>
    /// Flow summary for queries
    /// </summary>
    public sealed record FlowSummary
    {
        public string FlowId { get; init; } = string.Empty;
        public string FlowType { get; init; } = string.Empty;
        public FlowStatus Status { get; init; }
        public string CurrentStep { get; init; } = string.Empty;
        public int Progress { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
        public string UserId { get; init; } = string.Empty;
        public PauseReason? PauseReason { get; init; }
        public string? PauseMessage { get; init; }
    }
}
