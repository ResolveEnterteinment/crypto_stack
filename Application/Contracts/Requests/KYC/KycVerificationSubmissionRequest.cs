using Domain.DTOs.KYC;
using System.ComponentModel.DataAnnotations;

namespace Application.Contracts.Requests.KYC
{
    public class KycVerificationSubmissionRequest
    {
        [Required]
        public Guid SessionId { get; set; }

        [Required]
        public string VerificationLevel { get; set; } = string.Empty;

        [Required]
        public required Dictionary<string, object> Data { get; set; }
        [Required]
        public required bool ConsentGiven { get; set; } = false;
        [Required]
        public required bool TermsAccepted { get; set; } = false;
    }
}
