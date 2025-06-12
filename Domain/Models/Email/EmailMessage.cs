namespace Domain.Models.Email
{
    /// <summary>
    /// Represents an email message to be sent
    /// </summary>
    public class EmailMessage
    {
        /// <summary>
        /// Email recipient(s)
        /// </summary>
        public List<string> To { get; set; } = new List<string>();
        
        /// <summary>
        /// Email subject
        /// </summary>
        public string Subject { get; set; } = string.Empty;
        
        /// <summary>
        /// Email body content
        /// </summary>
        public string Body { get; set; } = string.Empty;
        
        /// <summary>
        /// Whether the body contains HTML
        /// </summary>
        public bool IsHtml { get; set; } = true;
        
        /// <summary>
        /// Email CC recipients
        /// </summary>
        public List<string> Cc { get; set; } = new List<string>();
        
        /// <summary>
        /// Email BCC recipients
        /// </summary>
        public List<string> Bcc { get; set; } = new List<string>();
        
        /// <summary>
        /// Optional custom from email address (overrides default)
        /// </summary>
        public string? FromEmail { get; set; }
        
        /// <summary>
        /// Optional custom from name (overrides default)
        /// </summary>
        public string? FromName { get; set; }
    }
}