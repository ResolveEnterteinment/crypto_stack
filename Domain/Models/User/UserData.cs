using Domain.Attributes;

namespace Domain.Models.User
{
    [BsonCollection("userDatas")]
    public class UserData : BaseEntity
    {
        public string FullName { get; set; }
        public string Email { get; set; }
        public string PaymentProviderCustomerId { get; set; }
    }
}
