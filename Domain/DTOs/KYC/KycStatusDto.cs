using Domain.Models.KYC;
using System.Xml.Linq;

namespace Domain.DTOs.KYC
{
    public class KycStatusDto
    {
        public Guid? Id { get; set; } = null;
        public string Status { get; set; } = string.Empty;
        public string VerificationLevel { get; set; } = string.Empty;
        public DateTime? SubmittedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? VerifiedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }

        public static KycStatusDto FromKycData (KycData kycData)
        {
            return new KycStatusDto
            {
                Id = kycData.Id,
                Status = kycData.Status,
                VerificationLevel = kycData.VerificationLevel,
                SubmittedAt = kycData.CreatedAt,
                UpdatedAt = kycData.UpdatedAt,
                VerifiedAt = kycData.VerifiedAt,
                ExpiresAt = kycData.ExpiresAt,
            };
        }
    }
}
