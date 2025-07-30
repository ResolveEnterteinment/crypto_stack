using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.DTOs.KYC
{
    public class AmlResult
    {
        public string Status { get; set; } = string.Empty;
        public string RiskLevel { get; set; } = string.Empty;
        public double RiskScore { get; set; }
        public DateTime CheckedAt { get; set; }
        public List<string> RiskIndicators { get; set; } = new();
    }
}
