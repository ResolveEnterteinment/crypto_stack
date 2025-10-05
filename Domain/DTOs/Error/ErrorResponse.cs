using Domain.Constants;
using System.Text.Json.Serialization;

namespace Domain.DTOs.Error
{
    /// <summary>
    /// Standard error response model for API responses
    /// </summary>
    public class ErrorResponse
    {
        /// <summary>
        /// Human-readable error message
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; }

        /// <summary>
        /// Machine-readable error code
        /// </summary>
        [JsonPropertyName("code")]
        public string Code { get; set; }

        /// <summary>
        /// Validation errors (if applicable)
        /// </summary>
        [JsonPropertyName("validationErrors")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string[]>? ValidationErrors { get; set; }

        /// <summary>
        /// Correlation ID for tracing the error in logs
        /// </summary>
        [JsonPropertyName("traceId")]
        public string TraceId { get; set; }
    }

    /// <summary>
    /// Extended ResultWrapper with error details
    /// </summary>
    public class EnhancedResultWrapper<T>
    {
        public bool IsSuccess { get; private set; }
        public T Data { get; private set; }
        public string DataMessage { get; private set; }
        public string ErrorMessage { get; private set; }
        public string ErrorCode { get; private set; }
        public FailureReason Reason { get; private set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string[]>? ValidationErrors { get; private set; }

        private EnhancedResultWrapper() { }

        public static EnhancedResultWrapper<T> Success(T data, string message = null)
        {
            return new EnhancedResultWrapper<T>
            {
                IsSuccess = true,
                Data = data,
                DataMessage = message
            };
        }

        public static EnhancedResultWrapper<T> Failure(
            FailureReason reason,
            string errorMessage,
            string errorCode = null,
            Dictionary<string, string[]>? validationErrors = null)
        {
            return new EnhancedResultWrapper<T>
            {
                IsSuccess = false,
                ErrorMessage = errorMessage,
                ErrorCode = errorCode ?? reason.ToString(),
                Reason = reason,
                ValidationErrors = validationErrors // Can be null
            };
        }

        public static EnhancedResultWrapper<T> FromException(Exception exception)
        {
            if (exception is Domain.Exceptions.DomainException domainEx)
            {
                var validationErrors = exception is Domain.Exceptions.ValidationException validationEx
                    ? validationEx.ValidationErrors
                    : null;

                return Failure(
                    FailureReasonExtensions.FromException(exception),
                    domainEx.Message,
                    domainEx.ErrorCode,
                    validationErrors);
            }

            return Failure(
                FailureReasonExtensions.FromException(exception),
                exception.Message);
        }
    }
}