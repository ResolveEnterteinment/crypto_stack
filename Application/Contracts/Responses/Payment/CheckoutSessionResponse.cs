namespace Application.Contracts.Responses.Payment
{
    public class CheckoutSessionResponse
    {
        public string CheckoutUrl { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
    }
}
