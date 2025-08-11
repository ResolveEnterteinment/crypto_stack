// Domain/Models/Withdrawal/WithdrawalData.cs
using Domain.Constants.Withdrawal;

namespace Domain.Models.Withdrawal
{
    public class WithdrawalData : BaseEntity
    {
        public Guid UserId { get; set; }
        public required string RequestedBy { get; set; } // User's identity (email, etc.)
        public decimal Amount { get; set; }
        public decimal Value { get; set; }
        public required string Currency { get; set; }
        public required string WithdrawalMethod { get; set; } // Bank, crypto, etc.
        public string Status { get; set; } = WithdrawalStatus.Pending;
        public string? ReasonCode { get; set; }
        public string? TransactionHash { get; set; } // For crypto withdrawals
        public DateTime? ProcessedAt { get; set; }
        public string? ProcessedBy { get; set; } // Admin who processed it
        public string? Comments { get; set; }
        public Dictionary<string, string> AdditionalDetails { get; set; } = [];
        public required string KycLevelAtTime { get; set; } // KYC level when withdrawal was requested
        public Dictionary<string, WithdrawalAuditTrail> AuditTrail { get; set; } = [];
    }
}