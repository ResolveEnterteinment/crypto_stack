using Infrastructure.Services.FlowEngine.Core.Enums;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    /// <summary>
    /// Query parameters for searching and filtering flows
    /// </summary>
    public class FlowQuery
    {
        public FlowStatus? Status { get; set; }
        public string UserId { get; set; }
        public DateTime? CreatedAfter { get; set; }
        public DateTime? CreatedBefore { get; set; }
        public PauseReason? PauseReason { get; set; }
        public int PageSize { get; set; } = 50;
        public int PageNumber { get; set; } = 1;
        public string CorrelationId { get; set; }
        public string FlowType { get; set; }
    }
}
