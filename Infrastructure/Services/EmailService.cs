using Application.Interfaces;
using Application.Interfaces.Logging;
using Domain.Constants;
using Domain.DTOs;
using Domain.DTOs.Settings;
using Domain.Models.Authentication;
using Domain.Models.Email;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Web;

namespace Infrastructure.Services
{
    /// <summary>
    /// Implementation of the email service using SMTP
    /// </summary>
    public class EmailService : IEmailService
    {
        private readonly EmailSettings _emailSettings;
        private readonly ILoggingService _logger;

        public EmailService(
            IOptions<EmailSettings> emailSettings,
            ILoggingService logger)
        {
            _emailSettings = emailSettings.Value;
            _logger = logger;
        }

        /// <inheritdoc/>
        public async Task<ResultWrapper> SendEmailConfirmationMailAsync(ApplicationUser user, string token)
        {
            using var scope = _logger.BeginScope("EmailService::SendEmailConfirmationMail", new Dictionary<string, object?>
            {
                ["UserId"] = user.Id.ToString(),
                ["UserEmail"] = user.Email
            });

            try
            {
                // Encode the token for URL safety
                var encodedToken = HttpUtility.UrlEncode(token);
                
                // Create a combined token that includes user ID
                var combinedToken = $"{user.Id}:{token}";
                var encodedCombinedToken = HttpUtility.UrlEncode(combinedToken);
                
                // Create the confirmation URL pointing to frontend instead of API
                var confirmationLink = $"{_emailSettings.AppBaseUrl}/confirm-email?token={encodedCombinedToken}";
                
                // Create email content
                var subject = "Confirm Your Email Address";
                var body = GenerateEmailConfirmationHtml(user.Fullname, confirmationLink);
                
                // Create and send the email
                var message = new EmailMessage
                {
                    To = new List<string> { user.Email },
                    Subject = subject,
                    Body = body,
                    IsHtml = true
                };
                
                return await SendEmailAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to send email confirmation: {ex.Message}");
                return ResultWrapper.FromException(ex);
            }
        }

        /// <inheritdoc/>
        public async Task<ResultWrapper> SendWelcomeMailToNewUserAsync(string email, object userData)
        {
            using var scope = _logger.BeginScope("EmailService::SendWelcomeMailToNewUser", new Dictionary<string, object?>
            {
                ["UserEmail"] = email
            });

            try
            {
                // Extract name if available in userData
                string name = "Valued Customer";
                if (userData is Dictionary<string, string> data && data.TryGetValue("name", out var userName))
                {
                    name = userName;
                }
                
                // Create email content
                var subject = "Welcome to Crypto Investment Platform";
                var body = GenerateWelcomeEmailHtml(name);
                
                // Create and send the email
                var message = new EmailMessage
                {
                    To = new List<string> { email },
                    Subject = subject,
                    Body = body,
                    IsHtml = true
                };
                
                return await SendEmailAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to send welcome email: {ex.Message}");
                return ResultWrapper.FromException(ex);
            }
        }

        /// <inheritdoc/>
        public async Task<ResultWrapper> SendNewUserMailToAdminAsync(string userEmail, string userId)
        {
            using var scope = _logger.BeginScope("EmailService::SendNewUserMailToAdmin", new Dictionary<string, object?>
            {
                ["NewUserEmail"] = userEmail,
                ["UserId"] = userId
            });

            try
            {
                if (string.IsNullOrEmpty(_emailSettings.AdminEmail))
                {
                    _logger.LogWarning("Admin email not configured. Skipping admin notification.");
                    return ResultWrapper.Success("Admin email not configured. Notification skipped.");
                }
                
                // Create email content
                var subject = "New User Registration";
                var body = $@"
                <html>
                <body style='font-family: Arial, sans-serif; color: #333;'>
                    <h2>New User Registered</h2>
                    <p>A new user has registered on the platform:</p>
                    <ul>
                        <li><strong>Email:</strong> {userEmail}</li>
                        <li><strong>User ID:</strong> {userId}</li>
                        <li><strong>Registration Time:</strong> {DateTime.UtcNow}</li>
                    </ul>
                    <p>This is an automated notification.</p>
                </body>
                </html>";
                
                // Create and send the email
                var message = new EmailMessage
                {
                    To = new List<string> { _emailSettings.AdminEmail },
                    Subject = subject,
                    Body = body,
                    IsHtml = true
                };
                
                return await SendEmailAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to send admin notification: {ex.Message}");
                return ResultWrapper.FromException(ex);
            }
        }

        /// <inheritdoc/>
        public async Task<ResultWrapper> SendPasswordResetMailAsync(ApplicationUser user, string token)
        {
            using var scope = _logger.BeginScope("EmailService::SendPasswordResetMail", new Dictionary<string, object?>
            {
                ["UserId"] = user.Id.ToString(),
                ["UserEmail"] = user.Email
            });

            try
            {
                // Encode the token for URL safety
                var encodedToken = HttpUtility.UrlEncode(token);
                var encodedEmail = HttpUtility.UrlEncode(user.Email);
                
                // Create the reset URL
                var resetLink = $"{_emailSettings.AppBaseUrl}/reset-password?email={encodedEmail}&token={encodedToken}";
                
                // Create email content
                var subject = "Reset Your Password";
                var body = $@"
                <html>
                <body style='font-family: Arial, sans-serif; color: #333;'>
                    <h2>Password Reset Request</h2>
                    <p>Hello {user.Fullname},</p>
                    <p>We received a request to reset your password. Please click the button below to set a new password:</p>
                    <p style='text-align: center;'>
                        <a href='{resetLink}' style='display: inline-block; padding: 10px 20px; background-color: #4CAF50; color: white; text-decoration: none; border-radius: 5px;'>Reset Password</a>
                    </p>
                    <p>If you didn't request a password reset, you can safely ignore this email.</p>
                    <p>This link will expire in 24 hours.</p>
                    <p>Thank you,<br>Crypto Investment Platform Team</p>
                </body>
                </html>";
                
                // Create and send the email
                var message = new EmailMessage
                {
                    To = new List<string> { user.Email },
                    Subject = subject,
                    Body = body,
                    IsHtml = true
                };
                
                return await SendEmailAsync(message);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to send password reset email: {ex.Message}");
                return ResultWrapper.FromException(ex);
            }
        }

        /// <inheritdoc/>
        public async Task<ResultWrapper> SendEmailAsync(EmailMessage emailMessage)
        {
            using var scope = _logger.BeginScope("EmailService::SendEmail", new Dictionary<string, object?>
            {
                ["Recipients"] = string.Join(", ", emailMessage.To)
            });

            // Check if email service is enabled
            if (!_emailSettings.Enabled)
            {
                _logger.LogInformation("Email service is disabled. Email not sent.");
                return ResultWrapper.Success("Email service is disabled. Email not sent.");
            }

            try
            {
                using var client = CreateSmtpClient();
                using var message = CreateMailMessage(emailMessage);
                
                // Log SMTP connection details for debugging (excluding password)
                _logger.LogInformation("Attempting to send email via SMTP: {Server}:{Port}, SSL: {EnableSsl}, User: {Username}",
                    _emailSettings.SmtpServer,
                    _emailSettings.Port,
                    _emailSettings.EnableSsl,
                    _emailSettings.Username);
                
                await client.SendMailAsync(message);
                
                _logger.LogInformation("Email sent successfully to {Recipients}", string.Join(", ", emailMessage.To));
                return ResultWrapper.Success("Email sent successfully");
            }
            catch (SmtpException smtpEx)
            {
                // Detailed SMTP error logging
                string detailedError = $"SMTP Error: {smtpEx.StatusCode}, {smtpEx.Message}";
                if (smtpEx.InnerException != null)
                {
                    detailedError += $" Inner exception: {smtpEx.InnerException.Message}";
                }
                
                _logger.LogError("SMTP error sending email: {ErrorMessage}", detailedError);
                return ResultWrapper.Failure(
                    FailureReason.ThirdPartyServiceUnavailable,
                    $"Failed to send email: {smtpEx.Message}",
                    errorCode: "SMTP_ERROR",
                    debugInformation: detailedError
                );
            }
            catch (Exception ex)
            {
                await _logger.LogTraceAsync($"Failed to send email: {ex.Message}", level: Domain.Constants.Logging.LogLevel.Critical, requiresResolution: true);
                return ResultWrapper.Failure(
                    FailureReason.ThirdPartyServiceUnavailable,
                    $"Failed to send email: {ex.Message}"
                );
            }
        }

        #region Helper Methods

        // Updated CreateSmtpClient method specifically for Hostinger
        private SmtpClient CreateSmtpClient()
        {
            var client = new SmtpClient
            {
                Host = _emailSettings.SmtpServer,
                Port = _emailSettings.Port,
                EnableSsl = _emailSettings.EnableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Timeout = 60000 // 60 seconds for Hostinger
            };

            // Hostinger-specific configuration
            if (_emailSettings.SmtpServer.Contains("hostinger"))
            {
                // Force port 587 for Hostinger if 465 is configured
                if (_emailSettings.Port == 465)
                {
                    _logger.LogWarning("Port 465 detected for Hostinger. Switching to port 587 for better compatibility.");
                    client.Port = 587;
                    client.EnableSsl = true;
                }
            }

            if (!string.IsNullOrEmpty(_emailSettings.Username) && !string.IsNullOrEmpty(_emailSettings.Password))
            {
                client.Credentials = new NetworkCredential(_emailSettings.Username, _emailSettings.Password);
            }

            return client;
        }

        private MailMessage CreateMailMessage(EmailMessage emailMessage)
        {
            var message = new MailMessage
            {
                From = new MailAddress(
                    emailMessage.FromEmail ?? _emailSettings.FromEmail,
                    emailMessage.FromName ?? _emailSettings.FromName),
                Subject = emailMessage.Subject,
                Body = emailMessage.Body,
                IsBodyHtml = emailMessage.IsHtml,
                BodyEncoding = Encoding.UTF8,
                Priority = MailPriority.Normal
            };

            // Add recipients
            foreach (var recipient in emailMessage.To)
            {
                message.To.Add(new MailAddress(recipient));
            }

            // Add CC recipients
            foreach (var ccRecipient in emailMessage.Cc)
            {
                message.CC.Add(new MailAddress(ccRecipient));
            }

            // Add BCC recipients
            foreach (var bccRecipient in emailMessage.Bcc)
            {
                message.Bcc.Add(new MailAddress(bccRecipient));
            }

            return message;
        }

        private string GenerateEmailConfirmationHtml(string name, string confirmationLink)
        {
            return $@"
            <html>
            <body style='font-family: Arial, sans-serif; color: #333;'>
                <div style='max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e1e1e1; border-radius: 5px;'>
                    <div style='text-align: center; margin-bottom: 20px;'>
                        <h2 style='color: #4CAF50;'>Confirm Your Email Address</h2>
                    </div>
                    <div style='padding: 20px;'>
                        <p>Hello {name},</p>
                        <p>Thank you for registering with our Crypto Investment Platform. To complete your registration, please confirm your email address by clicking the button below:</p>
                        <p style='text-align: center; margin: 30px 0;'>
                            <a href='{confirmationLink}' style='display: inline-block; padding: 12px 24px; background-color: #4CAF50; color: white; text-decoration: none; border-radius: 5px; font-weight: bold;'>Confirm Email</a>
                        </p>
                        <p>If the button doesn't work, you can also copy and paste the following link into your browser:</p>
                        <p style='background-color: #f9f9f9; padding: 10px; border-radius: 3px; word-break: break-all;'>
                            <a href='{confirmationLink}'>{confirmationLink}</a>
                        </p>
                        <p>This link will expire in 24 hours.</p>
                        <p>If you didn't create an account with us, you can safely ignore this email.</p>
                        <p>Thank you,<br>Crypto Investment Platform Team</p>
                    </div>
                    <div style='margin-top: 20px; border-top: 1px solid #e1e1e1; padding-top: 20px; font-size: 12px; color: #777; text-align: center;'>
                        <p>This is an automated message, please do not reply to this email.</p>
                    </div>
                </div>
            </body>
            </html>";
        }

        private string GenerateWelcomeEmailHtml(string name)
        {
            return $@"
            <html>
            <body style='font-family: Arial, sans-serif; color: #333;'>
                <div style='max-width: 600px; margin: 0 auto; padding: 20px; border: 1px solid #e1e1e1; border-radius: 5px;'>
                    <div style='text-align: center; margin-bottom: 20px;'>
                        <h2 style='color: #4CAF50;'>Welcome to Crypto Investment Platform</h2>
                    </div>
                    <div style='padding: 20px;'>
                        <p>Hello {name},</p>
                        <p>Thank you for joining our Crypto Investment Platform. We're excited to have you on board!</p>
                        <p>With your new account, you can:</p>
                        <ul>
                            <li>Create custom investment portfolios</li>
                            <li>Set up recurring investments</li>
                            <li>Track your investment performance</li>
                            <li>Withdraw your funds anytime</li>
                        </ul>
                        <p style='text-align: center; margin: 30px 0;'>
                            <a href='{_emailSettings.AppBaseUrl}/dashboard' style='display: inline-block; padding: 12px 24px; background-color: #4CAF50; color: white; text-decoration: none; border-radius: 5px; font-weight: bold;'>Go to Dashboard</a>
                        </p>
                        <p>If you have any questions or need assistance, please don't hesitate to contact our support team.</p>
                        <p>Thank you,<br>Crypto Investment Platform Team</p>
                    </div>
                    <div style='margin-top: 20px; border-top: 1px solid #e1e1e1; padding-top: 20px; font-size: 12px; color: #777; text-align: center;'>
                        <p>This is an automated message, please do not reply to this email.</p>
                    </div>
                </div>
            </body>
            </html>";
        }

        #endregion
    }
}