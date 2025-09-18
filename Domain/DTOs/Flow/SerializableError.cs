namespace Domain.DTOs.Flow
{
    /// <summary>
    /// Serializable error information that doesn't include problematic types
    /// </summary>
    public class SerializableError
    {
        public string Message { get; set; } = "";
        public string? ExceptionType { get; set; }
        public string? StackTrace { get; set; }
        public string? Source { get; set; }
        public DateTime OccurredAt { get; set; }
        public SerializableError? InnerError { get; set; }

        public static SerializableError FromException(Exception ex)
        {
            return new SerializableError
            {
                Message = ex.Message,
                ExceptionType = ex.GetType().FullName,
                StackTrace = ex.StackTrace,
                Source = ex.Source,
                OccurredAt = DateTime.UtcNow,
                InnerError = ex.InnerException != null ? FromException(ex.InnerException) : null
            };
        }
    }
}
