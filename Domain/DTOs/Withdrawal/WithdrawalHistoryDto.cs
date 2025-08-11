using Domain.Constants.Withdrawal;
using Domain.Models.Withdrawal;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.DTOs.Withdrawal
{
    public class WithdrawalHistoryDto
    {
        public decimal Amount { get; set; }
        public decimal Value { get; set; }
        public string Currency { get; set; }
        public string WithdrawalMethod { get; set; } // Bank, crypto, etc.
        public string Status { get; set; } = WithdrawalStatus.Pending;
        public string? TransactionHash { get; set; } // For crypto withdrawals
        public Dictionary<string, string> AdditionalDetails { get; set; } = [];
        public DateTime? CreatedAt { get; set; }

        public WithdrawalHistoryDto(WithdrawalData data)
        {
            Amount = data.Amount;
            Value = data.Value;
            Currency = data.Currency;
            WithdrawalMethod = data.WithdrawalMethod;
            Status = data.Status;
            TransactionHash = data.TransactionHash;
            AdditionalDetails = data.AdditionalDetails;
            CreatedAt = data.CreatedAt;
        }
    }
}
