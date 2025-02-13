namespace Application.Contracts.Responses.Auth
{
    public class LoginResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public bool EmailConfirmed { get; set; }
        public bool IsTutorialCompleted { get; set; }
    }
}
