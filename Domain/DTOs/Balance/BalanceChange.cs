using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.DTOs.Balance
{
    /// <summary>
    /// Balance change record for audit
    /// </summary>
    public class BalanceChange
    {
        public Guid TransactionId { get; set; }
        public DateTime Timestamp { get; set; }
        public decimal AvailableBefore { get; set; }
        public decimal AvailableAfter { get; set; }
        public decimal LockedBefore { get; set; }
        public decimal LockedAfter { get; set; }
        public decimal Change { get; set; }
        public string Action { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
