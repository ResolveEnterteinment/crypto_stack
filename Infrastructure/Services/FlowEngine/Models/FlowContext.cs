using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Models
{
    /// <summary>
    /// Flow context
    /// </summary>
    public sealed record FlowContext
    {
        public FlowDefinition Flow { get; init; } = null!;
        public IServiceProvider Services { get; init; } = null!;
        public CancellationToken CancellationToken { get; init; }
    }
}
