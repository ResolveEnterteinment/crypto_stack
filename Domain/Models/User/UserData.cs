using Domain.Attributes;

namespace Domain.Models.User
{
    [BsonCollection("userDatas")]
    public class UserData : BaseEntity
    {
        public string FullName { get; set; }
        public string Email { get; set; }
        public string PaymentProviderCustomerId { get; set; }
        public bool IsKycVerified { get; set; } = false;
        public string KycLevel { get; set; } = Constants.KYC.KycLevel.None;
        public DateTime? KycVerifiedAt { get; set; }
    }
}