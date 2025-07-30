using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Contracts.Requests.KYC
{
    public class UpdateKycStatusRequest
    {
        [Required]
        [RegularExpression("^(NOT_STARTED|PENDING|NEEDS_REVIEW|APPROVED|REJECTED)$",
            ErrorMessage = "Status must be one of: NOT_STARTED, PENDING, NEEDS_REVIEW, APPROVED, REJECTED")]
        public string Status { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Reason { get; set; }
    }
}
