namespace Domain.DTOs.Settings
{
    /// <summary>
    /// General application settings that are used across multiple services
    /// </summary>
    public class AppSettings
    {
        /// <summary>
        /// Base URL for the application
        /// </summary>
        public string BaseUrl { get; set; } = "https://localhost:5173";
        
        /// <summary>
        /// Application name
        /// </summary>
        public string ApplicationName { get; set; } = "Lumi Stack";
        
        /// <summary>
        /// Application version
        /// </summary>
        public string Version { get; set; } = "1.0.0";
        
        /// <summary>
        /// Support email address
        /// </summary>
        public string SupportEmail { get; set; } = string.Empty;
        
        /// <summary>
        /// Admin contact email
        /// </summary>
        public string AdminEmail { get; set; } = string.Empty;
        
        /// <summary>
        /// Default timezone for the application
        /// </summary>
        public string TimeZone { get; set; } = "UTC";
        
        /// <summary>
        /// Environment name (Development, Staging, Production)
        /// </summary>
        public string Environment { get; set; } = "Development";
    }
}