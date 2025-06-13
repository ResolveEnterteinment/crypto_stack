namespace Application.Contracts.Responses.Auth
{
    /// <summary>
    /// Response model for user data operations
    /// </summary>
    public class UserDataResponse : BaseResponse
    {
        /// <summary>
        /// The unique identifier of the user
        /// </summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// The user's email address
        /// </summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// The display name or full name of the user
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Indicates whether the user's email address has been confirmed
        /// </summary>
        public bool EmailConfirmed { get; set; }

        /// <summary>
        /// Roles assigned to the user
        /// </summary>
        public string[] Roles { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Indicates whether this is the user's first login
        /// </summary>
        public bool IsFirstLogin { get; set; }
    }
}