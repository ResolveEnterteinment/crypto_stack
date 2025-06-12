// Domain/Models/KYC/KycData.cs
using Domain.Constants.KYC;

namespace Domain.Models.KYC
{
    public class KycData : BaseEntity
    {
        public Guid UserId { get; set; }
        public string Status { get; set; } = KycStatus.NotStarted;
        public string VerificationLevel { get; set; } = KycLevel.None;
        public DateTime? SubmittedAt { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public DateTime? LastCheckedAt { get; set; }
        public string? RejectionReason { get; set; }
        public Dictionary<string, object> VerificationData { get; set; } = [];
        public Dictionary<string, object> AdditionalInfo { get; set; } = [];
        public List<KycHistoryEntry> History { get; set; } = [];
        public bool IsPoliticallyExposed { get; set; } = false;
        public bool IsHighRisk { get; set; } = false;
        public string? RiskScore { get; set; }
        public bool IsRestrictedRegion { get; set; } = false;
    }

    public class KycHistoryEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public required string Action { get; set; }
        public Guid SessionId { get; set; }
        public required string Status { get; set; }
        public required string PerformedBy { get; set; }
        public Dictionary<string, object> Details { get; set; } = [];
    }
}