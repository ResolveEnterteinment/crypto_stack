using Domain.DTOs;
using Domain.Models.Authentication;
using Domain.Models.Email;

namespace Application.Interfaces
{
    /// <summary>
    /// Interface for email sending operations
    /// </summary>
    public interface IEmailService
    {
        /// <summary>
        /// Sends an email confirmation link to a newly registered user
        /// </summary>
        /// <param name="user">The user who needs email confirmation</param>
        /// <param name="token">The email confirmation token</param>
        /// <returns>A result indicating success or failure</returns>
        Task<ResultWrapper> SendEmailConfirmationMailAsync(ApplicationUser user, string token);

        /// <summary>
        /// Sends a welcome email to a new user
        /// </summary>
        /// <param name="email">The user's email</param>
        /// <param name="userData">Additional user data for personalization</param>
        /// <returns>A result indicating success or failure</returns>
        Task<ResultWrapper> SendWelcomeMailToNewUserAsync(string email, object userData);

        /// <summary>
        /// Notifies admins about a new user registration
        /// </summary>
        /// <param name="userEmail">The new user's email</param>
        /// <param name="userId">The new user's ID</param>
        /// <returns>A result indicating success or failure</returns>
        Task<ResultWrapper> SendNewUserMailToAdminAsync(string userEmail, string userId);

        /// <summary>
        /// Sends a password reset link to a user
        /// </summary>
        /// <param name="user">The user who requested password reset</param>
        /// <param name="token">The password reset token</param>
        /// <returns>A result indicating success or failure</returns>
        Task<ResultWrapper> SendPasswordResetMailAsync(ApplicationUser user, string token);

        /// <summary>
        /// Sends a custom email with the specified parameters
        /// </summary>
        /// <param name="emailMessage">The email message details</param>
        /// <returns>A result indicating success or failure</returns>
        Task<ResultWrapper> SendEmailAsync(EmailMessage emailMessage);
    }
}