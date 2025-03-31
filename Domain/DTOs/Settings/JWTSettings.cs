namespace Domain.DTOs
{
    public class JwtSettings
    {
        public string Key { get; set; } = null!;
        public string Issuer { get; set; } = null!;
        public string Audience { get; set; } = null!;
        public int ExpirationMinutes { get; set; } = 60; // Default to 1 hour
        public int RefreshTokenExpirationDays { get; set; } = 7; // Refresh token validity
    }
}