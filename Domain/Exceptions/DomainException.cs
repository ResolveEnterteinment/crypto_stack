namespace Domain.Exceptions
{
    /// <summary>
    /// Base exception for all domain-specific errors
    /// </summary>
    public class DomainException : Exception
    {
        public string ErrorCode { get; }

        public DomainException(string message, string errorCode) : base(message)
        {
            ErrorCode = errorCode;
        }

        public DomainException(string message, string errorCode, Exception innerException)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
        }
    }

    /// <summary>
    /// Exception thrown when validation errors occur
    /// </summary>
    public class ValidationException : DomainException
    {
        public Dictionary<string, string[]> ValidationErrors { get; }

        public ValidationException(string message, Dictionary<string, string[]> validationErrors)
            : base(message, "VALIDATION_ERROR")
        {
            ValidationErrors = validationErrors;
        }
    }

    /// <summary>
    /// Exception thrown when a requested resource is not found
    /// </summary>
    public class ResourceNotFoundException : DomainException
    {
        public string ResourceType { get; }
        public string ResourceId { get; }

        public ResourceNotFoundException(string resourceType, string resourceId)
            : base($"{resourceType} with ID {resourceId} was not found", "RESOURCE_NOT_FOUND")
        {
            ResourceType = resourceType;
            ResourceId = resourceId;
        }
    }

    /// <summary>
    /// Exception thrown when there are insufficient funds to perform an operation
    /// </summary>
    public class InsufficientBalanceException : DomainException
    {
        public InsufficientBalanceException(string message)
            : base(message, "INSUFFICIENT_BALANCE")
        {
        }
    }

    /// <summary>
    /// Exception thrown when exchange order execution fails
    /// </summary>
    public class OrderExecutionException : DomainException
    {
        public string OrderId { get; }
        public string Exchange { get; }

        public OrderExecutionException(string message, string exchange, string orderId = null)
            : base(message, "ORDER_EXECUTION_FAILED")
        {
            OrderId = orderId;
            Exchange = exchange;
        }
    }

    /// <summary>
    /// Exception thrown when an exchange API returns an error
    /// </summary>
    public class ExchangeApiException : DomainException
    {
        public string Exchange { get; }

        public ExchangeApiException(string message, string exchange)
            : base(message, "EXCHANGE_API_ERROR")
        {
            Exchange = exchange;
        }

        public ExchangeApiException(string message, string exchange, Exception innerException)
            : base(message, "EXCHANGE_API_ERROR", innerException)
        {
            Exchange = exchange;
        }
    }

    /// <summary>
    /// Exception thrown when a payment processing error occurs
    /// </summary>
    public class PaymentProcessingException : DomainException
    {
        public string PaymentProvider { get; }
        public string PaymentId { get; }

        public PaymentProcessingException(string message, string paymentProvider, string paymentId = null)
            : base(message, "PAYMENT_PROCESSING_ERROR")
        {
            PaymentProvider = paymentProvider;
            PaymentId = paymentId;
        }
    }

    /// <summary>
    /// Exception thrown when a third-party service is unavailable
    /// </summary>
    public class ServiceUnavailableException : DomainException
    {
        public string ServiceName { get; }

        public ServiceUnavailableException(string serviceName, string message = null)
            : base(message ?? $"Service {serviceName} is currently unavailable", "SERVICE_UNAVAILABLE")
        {
            ServiceName = serviceName;
        }
    }

    /// <summary>
    /// Exception thrown when a database operation fails
    /// </summary>
    public class DatabaseException : DomainException
    {
        public DatabaseException(string message)
            : base(message, "DATABASE_ERROR")
        {
        }

        public DatabaseException(string message, Exception innerException)
            : base(message, "DATABASE_ERROR", innerException)
        {
        }
    }

    /// <summary>
    /// Exception thrown for concurrent modification conflicts
    /// </summary>
    public class ConcurrencyException : DomainException
    {
        public string ResourceType { get; }
        public string ResourceId { get; }

        public ConcurrencyException(string resourceType, string resourceId)
            : base($"Concurrency conflict detected for {resourceType} with ID {resourceId}", "CONCURRENCY_CONFLICT")
        {
            ResourceType = resourceType;
            ResourceId = resourceId;
        }
    }
}