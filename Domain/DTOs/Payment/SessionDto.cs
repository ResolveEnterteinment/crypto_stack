namespace Domain.DTOs.Payment
{
    public class SessionDto
    {
        public string Id { get; set; }
        public string ClientSecret { get; set; }
        public string Url { get; set; }
        public string Status { get; set; }
    }
}
