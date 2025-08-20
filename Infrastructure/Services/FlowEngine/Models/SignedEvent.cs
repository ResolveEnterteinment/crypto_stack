using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Models
{
    /// <summary>
    /// Signed event for secure event publishing
    /// </summary>
    public sealed record SignedEvent
    {
        public string EventId { get; init; } = Guid.NewGuid().ToString();
        public string EventType { get; init; } = string.Empty;
        public object EventData { get; init; } = new();
        public string PublishedBy { get; init; } = string.Empty;
        public string CorrelationId { get; init; } = string.Empty;
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;
        public string Signature { get; init; } = string.Empty;
        public string SigningKeyId { get; init; } = string.Empty;
    }
}
