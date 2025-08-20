using Domain.Exceptions;
using Domain.Exceptions.KYC;
using MongoDB.Driver;

namespace Domain.Constants
{
    /// <summary>
    /// Comprehensive categorization of failure reasons
    /// </summary>
    public enum FailureReason
    {
        // General errors
        Unknown,
        NullReturnValue,
        ValidationError,
        NotFound,
        Unauthorized,
        Forbidden,

        // Domain-specific errors
        InvalidOperation,
        InsufficientBalance,
        OrderExecutionFailed,
        ExchangeApiError,
        PaymentProcessingError,
        KycFetchError,
        KycVerificationError,
        SecurityError,
        AssetFetchError,
        BalanceFetchError,
        ResourceNotFound,
        ConcurrencyConflict,
        IdempotencyConflict,

        // Technical errors
        DatabaseError,
        NetworkError,
        ThirdPartyServiceUnavailable,
        ConfigurationError,
        TimeoutError,
        CacheOperationFailed
    }

    /// <summary>
    /// Extension methods for FailureReason enum
    /// </summary>
    public static class FailureReasonExtensions
    {
        /// <summary>
        /// Maps an exception type to a FailureReason
        /// </summary>
        public static FailureReason FromException(Exception ex)
        {
            return ex switch
            {
                InsufficientBalanceException => FailureReason.InsufficientBalance,
                OrderExecutionException => FailureReason.OrderExecutionFailed,
                ExchangeApiException => FailureReason.ExchangeApiError,
                PaymentApiException => FailureReason.PaymentProcessingError,
                AssetFetchException => FailureReason.AssetFetchError,
                BalanceFetchException => FailureReason.BalanceFetchError,
                KycVerificationException => FailureReason.KycVerificationError,
                ResourceNotFoundException => FailureReason.ResourceNotFound,
                ValidationException => FailureReason.ValidationError,
                ConcurrencyException => FailureReason.ConcurrencyConflict,
                IdempotencyException => FailureReason.IdempotencyConflict,
                DatabaseException => FailureReason.DatabaseError,
                ServiceUnavailableException => FailureReason.ThirdPartyServiceUnavailable,

                // Framework/library exceptions
                MongoException => FailureReason.DatabaseError,
                TimeoutException => FailureReason.TimeoutError,
                HttpRequestException => FailureReason.NetworkError,
                KeyNotFoundException => FailureReason.NotFound,
                UnauthorizedAccessException => FailureReason.Unauthorized,

                _ => FailureReason.Unknown
            };
        }

        /// <summary>
        /// Maps a FailureReason to an exception type
        /// </summary>
        public static Type ToException(FailureReason reason)
        {
            return reason switch
            {
                FailureReason.InvalidOperation => typeof(InvalidOperationException),
                FailureReason.InsufficientBalance => typeof(InsufficientBalanceException),
                FailureReason.OrderExecutionFailed => typeof(OrderExecutionException),
                FailureReason.ExchangeApiError => typeof(ExchangeApiException),
                FailureReason.PaymentProcessingError => typeof(PaymentApiException),
                FailureReason.AssetFetchError => typeof(AssetFetchException),
                FailureReason.BalanceFetchError => typeof(BalanceFetchException),
                FailureReason.KycVerificationError => typeof(KycVerificationException),
                FailureReason.ResourceNotFound => typeof(ResourceNotFoundException),
                FailureReason.ValidationError => typeof(ValidationException),
                FailureReason.ConcurrencyConflict => typeof(ConcurrencyException),
                FailureReason.IdempotencyConflict => typeof(IdempotencyException),
                FailureReason.DatabaseError => typeof(DatabaseException),
                FailureReason.ThirdPartyServiceUnavailable => typeof(ServiceUnavailableException),

                // Framework/library exceptions
                FailureReason.TimeoutError => typeof(TimeoutException),
                FailureReason.NetworkError => typeof(HttpRequestException),
                FailureReason.NotFound => typeof(KeyNotFoundException),
                FailureReason.Unauthorized => typeof(UnauthorizedAccessException),

                // For FailureReasons that don't have specific domain exceptions, use generic exceptions
                FailureReason.KycFetchError => typeof(DomainException),
                FailureReason.SecurityError => typeof(DomainException),
                FailureReason.Forbidden => typeof(UnauthorizedAccessException),
                FailureReason.ConfigurationError => typeof(InvalidOperationException),
                FailureReason.CacheOperationFailed => typeof(InvalidOperationException),
                FailureReason.NullReturnValue => typeof(InvalidOperationException),

                _ => typeof(Exception)
            };
        }

        /// <summary>
        /// Gets HTTP status code corresponding to the failure reason
        /// </summary>
        public static int ToStatusCode(this FailureReason reason)
        {
            return reason switch
            {
                FailureReason.ValidationError => 400, // Bad Request
                FailureReason.NotFound => 404, // Not Found
                FailureReason.ResourceNotFound => 404, // Not Found
                FailureReason.Unauthorized => 401, // Unauthorized
                FailureReason.Forbidden => 403, // Forbidden
                FailureReason.InsufficientBalance => 400, // Bad Request
                FailureReason.OrderExecutionFailed => 400, // Bad Request
                FailureReason.ExchangeApiError => 502, // Bad Gateway
                FailureReason.PaymentProcessingError => 400, // Bad Request
                FailureReason.KycFetchError => 400, // Bad Request
                FailureReason.KycVerificationError => 400, // Bad Request
                FailureReason.AssetFetchError => 400, // Bad Request
                FailureReason.BalanceFetchError => 400, // Bad Request
                FailureReason.DatabaseError => 500, // Internal Server Error
                FailureReason.NetworkError => 503, // Service Unavailable
                FailureReason.ThirdPartyServiceUnavailable => 503, // Service Unavailable
                FailureReason.TimeoutError => 504, // Gateway Timeout
                FailureReason.ConcurrencyConflict => 409, // Conflict
                FailureReason.IdempotencyConflict => 400,
                FailureReason.ConfigurationError => 500, // Internal Server Error
                _ => 500, // Internal Server Error
            };
        }
    }
}