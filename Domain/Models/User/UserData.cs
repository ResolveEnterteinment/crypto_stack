namespace Domain.Models.User
{
    public class UserData : BaseEntity
    {
        public required string FullName { get; set; }
        public required string Email { get; set; }
        public string PaymentProviderCustomerId { get; set; }
    }
}
