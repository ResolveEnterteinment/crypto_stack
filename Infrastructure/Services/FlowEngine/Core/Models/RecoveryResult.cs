using CryptoExchange.Net.Objects;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    /// <summary>
    /// Result of flow recovery operation
    /// </summary>
    public class RecoveryResult
    {
        public int TotalFlowsChecked { get; set; }
        public int FlowsRecovered { get; set; }
        public int FlowsFailed { get; set; }
        public List<Guid> RecoveredFlowIds { get; set; } = new();
        public Dictionary<Guid, Exception> FailedFlowsDict { get; set; } = new();
        public TimeSpan Duration { get; set; }
        public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    }
}