// Domain/Models/KYC/KycData.cs
using Domain.Constants.KYC;

namespace Domain.Models.KYC
{
    public class KycData : BaseEntity
    {
        public Guid UserId { get; set; }
        public string Status { get; set; } = KycStatus.NotStarted;
        public string VerificationLevel { get; set; } = KycLevel.None;
        public string ReferenceId { get; set; }  // ID from KYC provider
        public string ProviderName { get; set; } // Which KYC provider was used
        public DateTime? SubmittedAt { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public DateTime? LastCheckedAt { get; set; }
        public string RejectionReason { get; set; }
        public Dictionary<string, object> VerificationData { get; set; } = new Dictionary<string, object>();
        public Dictionary<string, object> AdditionalInfo { get; set; } = new Dictionary<string, object>();
        public List<KycHistoryEntry> History { get; set; } = new List<KycHistoryEntry>();
        public bool IsPoliticallyExposed { get; set; } = false;
        public bool IsHighRisk { get; set; } = false;
        public string RiskScore { get; set; }
        public bool IsRestrictedRegion { get; set; } = false;
    }

    public class KycHistoryEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Action { get; set; }
        public string Status { get; set; }
        public string PerformedBy { get; set; }
        public Dictionary<string, object> Details { get; set; } = new Dictionary<string, object>();
    }
}