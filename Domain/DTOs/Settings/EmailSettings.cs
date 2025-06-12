namespace Domain.DTOs.Settings
{
    /// <summary>
    /// Email service configuration settings
    /// </summary>
    public class EmailSettings
    {
        /// <summary>
        /// SMTP server address
        /// </summary>
        public string SmtpServer { get; set; } = string.Empty;
        
        /// <summary>
        /// SMTP server port
        /// </summary>
        public int Port { get; set; } = 587;
        
        /// <summary>
        /// Use SSL for SMTP connection
        /// </summary>
        public bool EnableSsl { get; set; } = true;
        
        /// <summary>
        /// From email address
        /// </summary>
        public string FromEmail { get; set; } = string.Empty;
        
        /// <summary>
        /// From display name
        /// </summary>
        public string FromName { get; set; } = "Crypto Investment Platform";
        
        /// <summary>
        /// SMTP authentication username
        /// </summary>
        public string Username { get; set; } = string.Empty;
        
        /// <summary>
        /// SMTP authentication password
        /// </summary>
        public string Password { get; set; } = string.Empty;
        
        /// <summary>
        /// Base URL for the application (used in email links)
        /// </summary>
        public string AppBaseUrl { get; set; } = "https://localhost:5001";
        
        /// <summary>
        /// Admin email address for notifications
        /// </summary>
        public string AdminEmail { get; set; } = string.Empty;
        
        /// <summary>
        /// Enable email service (can be disabled for testing)
        /// </summary>
        public bool Enabled { get; set; } = true;
    }
}