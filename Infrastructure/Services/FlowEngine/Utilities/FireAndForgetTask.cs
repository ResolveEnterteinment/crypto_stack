using Infrastructure.Services.FlowEngine.Models;
using Infrastructure.Services.FlowEngine.Validation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Utilities
{
    /// <summary>
    /// Fire-and-forget task model
    /// </summary>
    public sealed record FireAndForgetTask<TFlow, TInit>
        where TFlow : FlowDefinition, new()
        where TInit : class, IValidatable
    {
        public TInit InitialData { get; init; } = default!;
        public string? UserId { get; init; }
        public string CorrelationId { get; init; } = string.Empty;
        public DateTime QueuedAt { get; init; }
    }
}
