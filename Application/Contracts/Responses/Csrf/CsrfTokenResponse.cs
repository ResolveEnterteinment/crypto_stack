namespace Application.Contracts.Responses.Csrf
{
    /// <summary>
    /// Response model for user login operations
    /// </summary>
    public class CsrfTokenResponse
    {
        /// <summary>
        /// Csrf token for API authorization
        /// </summary>
        public string Token { get; set; } = string.Empty;
    }
}