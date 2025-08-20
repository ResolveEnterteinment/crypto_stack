namespace Application.Contracts.Responses.Auth
{
    /// <summary>
    /// Response model for user login operations
    /// </summary>
    public class LoginResponse
    {
        /// <summary>
        /// JWT access token for API authorization
        /// </summary>
        public string AccessToken { get; set; } = string.Empty;

        /// <summary>
        /// Refresh token for obtaining a new access token when the current one expires
        /// </summary>
        public string RefreshToken { get; set; } = string.Empty;

        /// <summary>
        /// The unique identifier of the authenticated user
        /// </summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// The display name or full name of the authenticated user
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Indicates whether the user's email address has been confirmed
        /// </summary>
        public bool EmailConfirmed { get; set; }

        /// <summary>
        /// The expiration date/time of the access token
        /// </summary>
        public DateTime? TokenExpiration { get; set; }

        /// <summary>
        /// Optional list of roles assigned to the user
        /// </summary>
        public string[] Roles { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Indicates whether this is the user's first login
        /// </summary>
        public bool IsFirstLogin { get; set; }
    }
}