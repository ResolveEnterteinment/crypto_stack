using Domain.Constants.KYC;
using System.ComponentModel.DataAnnotations;

namespace Application.Contracts.Requests.KYC
{
    public class UpdateKycStatusRequest
    {
        [Required]
        [RegularExpression("^(NOT_STARTED|PENDING|NEEDS_REVIEW|APPROVED|REJECTED)$",
            ErrorMessage = "Status must be one of: NOT_STARTED, PENDING, NEEDS_REVIEW, APPROVED, REJECTED")]
        public string Status { get; set; } = string.Empty;
        [Required]
        public string VerificationLevel { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Reason { get; set; }

        [MaxLength(500)]
        public string? Comments { get; set; }
    }
}
