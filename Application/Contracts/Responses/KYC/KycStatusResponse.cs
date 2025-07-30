using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Contracts.Responses.KYC
{
    public class KycStatusResponse
    {
        public string Status { get; set; } = string.Empty;
        public string VerificationLevel { get; set; } = string.Empty;
        public DateTime? SubmittedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public List<string> NextSteps { get; set; } = new();
    }
}
