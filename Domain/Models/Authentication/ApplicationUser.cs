using AspNetCore.Identity.Mongo.Model;

namespace Domain.Models.Authentication
{
    public class ApplicationUser : MongoUser<Guid>
    {
        /// <summary>
        /// User's full name for display purposes
        /// </summary>
        public string Fullname { get; set; } = string.Empty;

        /// <summary>
        /// Refresh token for JWT authentication
        /// </summary>
        public string? RefreshToken { get; set; }

        /// <summary>
        /// Expiration time for the current refresh token
        /// </summary>
        public DateTime RefreshTokenExpiryTime { get; set; }

        /// <summary>
        /// The date when the user profile was last updated
        /// </summary>
        public DateTime? LastProfileUpdateDate { get; set; }

        /// <summary>
        /// Flag indicating whether the user has completed onboarding
        /// </summary>
        public bool HasCompletedOnboarding { get; set; } = false;

        /// <summary>
        /// Date of the user's last successful login
        /// </summary>
        public DateTime? LastLoginDate { get; set; }

        /// <summary>
        /// IP address from the last successful login
        /// </summary>
        public string? LastLoginIP { get; set; }

        /// <summary>
        /// Two-factor authentication recovery codes
        /// </summary>
        public string[]? RecoveryCodes { get; set; }

        /// <summary>
        /// Flag indicating if the user has set up 2FA
        /// </summary>
        /// The property 'TwoFactorEnabled' of type 'Domain.Models.Authentication.ApplicationUser' cannot use element name 'TwoFactorEnabled' because it is already being used by property 'TwoFactorEnabled' of type 'Microsoft.AspNetCore.Identity.IdentityUser`

        //public bool TwoFactorEnabled { get; set; } = false;
    }
}