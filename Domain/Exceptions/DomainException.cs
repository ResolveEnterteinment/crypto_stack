using System.Runtime.Serialization;

namespace Domain.Exceptions
{
    /// <summary>
    /// Base exception for all domain-specific errors
    /// </summary>
    [Serializable]
    public class DomainException : Exception
    {
        /// <summary>
        /// Gets the error code associated with this exception.
        /// </summary>
        public string ErrorCode { get; }

        /// <summary>
        /// Gets additional error context data.
        /// </summary>
        public IDictionary<string, object> Context { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DomainException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="errorCode">The error code.</param>
        public DomainException(string message, string errorCode)
            : base(message)
        {
            ErrorCode = errorCode;
            Context = new Dictionary<string, object>();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DomainException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="errorCode">The error code.</param>
        /// <param name="innerException">The inner exception.</param>
        public DomainException(string message, string errorCode, Exception innerException)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
            Context = new Dictionary<string, object>();
        }

        /// <summary>
        /// Adds context information to this exception.
        /// </summary>
        /// <param name="key">The context key.</param>
        /// <param name="value">The context value.</param>
        /// <returns>This exception instance for method chaining.</returns>
        public DomainException AddContext(string key, object value)
        {
            if (Context == null)
            {
                Context = new Dictionary<string, object>();
            }

            Context[key] = value;
            return this;
        }

        /// <summary>
        /// Used for serialization purposes.
        /// </summary>
        protected DomainException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            ErrorCode = info.GetString(nameof(ErrorCode));
            Context = (Dictionary<string, object>)info.GetValue(nameof(Context), typeof(Dictionary<string, object>));
        }

        /// <summary>
        /// Serializes the exception data.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            info.AddValue(nameof(ErrorCode), ErrorCode);
            info.AddValue(nameof(Context), Context);
            base.GetObjectData(info, context);
        }
    }

    /// <summary>
    /// Exception thrown when validation errors occur
    /// </summary>
    [Serializable]
    public class ValidationException : DomainException
    {
        /// <summary>
        /// Gets the validation errors.
        /// </summary>
        public Dictionary<string, string[]> ValidationErrors { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ValidationException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="validationErrors">The validation errors.</param>
        public ValidationException(string message, Dictionary<string, string[]> validationErrors)
            : base(message, "VALIDATION_ERROR")
        {
            ValidationErrors = validationErrors ?? new Dictionary<string, string[]>();
        }

        /// <summary>
        /// Used for serialization purposes.
        /// </summary>
        protected ValidationException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            ValidationErrors = (Dictionary<string, string[]>)info.GetValue(
                nameof(ValidationErrors), typeof(Dictionary<string, string[]>));
        }

        /// <summary>
        /// Serializes the exception data.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            info.AddValue(nameof(ValidationErrors), ValidationErrors);
            base.GetObjectData(info, context);
        }
    }

    /// <summary>
    /// Exception thrown when a requested resource is not found
    /// </summary>
    [Serializable]
    public class ResourceNotFoundException : DomainException
    {
        /// <summary>
        /// Gets the resource type.
        /// </summary>
        public string ResourceType { get; }

        /// <summary>
        /// Gets the resource ID.
        /// </summary>
        public string ResourceId { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceNotFoundException"/> class.
        /// </summary>
        /// <param name="resourceType">The resource type.</param>
        /// <param name="resourceId">The resource ID.</param>
        public ResourceNotFoundException(string resourceType, string resourceId)
            : base($"{resourceType} with ID {resourceId} was not found", "RESOURCE_NOT_FOUND")
        {
            ResourceType = resourceType;
            ResourceId = resourceId;
            AddContext("ResourceType", resourceType);
            AddContext("ResourceId", resourceId);
        }

        /// <summary>
        /// Used for serialization purposes.
        /// </summary>
        protected ResourceNotFoundException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            ResourceType = info.GetString(nameof(ResourceType));
            ResourceId = info.GetString(nameof(ResourceId));
        }

        /// <summary>
        /// Serializes the exception data.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            info.AddValue(nameof(ResourceType), ResourceType);
            info.AddValue(nameof(ResourceId), ResourceId);
            base.GetObjectData(info, context);
        }
    }

    /// <summary>
    /// Exception thrown when there are insufficient funds to perform an operation
    /// </summary>
    [Serializable]
    public class InsufficientBalanceException : DomainException
    {
        /// <summary>
        /// Gets the asset ticker.
        /// </summary>
        public string AssetTicker { get; }

        /// <summary>
        /// Gets the available balance.
        /// </summary>
        public decimal AvailableBalance { get; }

        /// <summary>
        /// Gets the required amount.
        /// </summary>
        public decimal RequiredAmount { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="InsufficientBalanceException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        public InsufficientBalanceException(string message)
            : base(message, "INSUFFICIENT_BALANCE")
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InsufficientBalanceException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="assetTicker">The asset ticker.</param>
        /// <param name="availableBalance">The available balance.</param>
        /// <param name="requiredAmount">The required amount.</param>
        public InsufficientBalanceException(string message, string assetTicker, decimal availableBalance, decimal requiredAmount)
            : base(message, "INSUFFICIENT_BALANCE")
        {
            AssetTicker = assetTicker;
            AvailableBalance = availableBalance;
            RequiredAmount = requiredAmount;

            AddContext("AssetTicker", assetTicker);
            AddContext("AvailableBalance", availableBalance);
            AddContext("RequiredAmount", requiredAmount);
            AddContext("Deficit", requiredAmount - availableBalance);
        }

        /// <summary>
        /// Used for serialization purposes.
        /// </summary>
        protected InsufficientBalanceException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            AssetTicker = info.GetString(nameof(AssetTicker));
            AvailableBalance = info.GetDecimal(nameof(AvailableBalance));
            RequiredAmount = info.GetDecimal(nameof(RequiredAmount));
        }

        /// <summary>
        /// Serializes the exception data.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            info.AddValue(nameof(AssetTicker), AssetTicker);
            info.AddValue(nameof(AvailableBalance), AvailableBalance);
            info.AddValue(nameof(RequiredAmount), RequiredAmount);
            base.GetObjectData(info, context);
        }
    }

    /// <summary>
    /// Exception thrown when exchange order execution fails
    /// </summary>
    [Serializable]
    public class OrderExecutionException : DomainException
    {
        /// <summary>
        /// Gets the order ID.
        /// </summary>
        public string OrderId { get; }

        /// <summary>
        /// Gets the exchange.
        /// </summary>
        public string Exchange { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="OrderExecutionException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="exchange">The exchange.</param>
        /// <param name="orderId">The order ID.</param>
        public OrderExecutionException(string message, string exchange, string orderId = null)
            : base(message, "ORDER_EXECUTION_FAILED")
        {
            OrderId = orderId;
            Exchange = exchange;

            AddContext("Exchange", exchange);
            if (!string.IsNullOrEmpty(orderId))
            {
                AddContext("OrderId", orderId);
            }
        }

        /// <summary>
        /// Used for serialization purposes.
        /// </summary>
        protected OrderExecutionException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            OrderId = info.GetString(nameof(OrderId));
            Exchange = info.GetString(nameof(Exchange));
        }

        /// <summary>
        /// Serializes the exception data.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            info.AddValue(nameof(OrderId), OrderId);
            info.AddValue(nameof(Exchange), Exchange);
            base.GetObjectData(info, context);
        }
    }

    /// <summary>
    /// Exception thrown when an exchange API returns an error
    /// </summary>
    [Serializable]
    public class ExchangeApiException : DomainException
    {
        /// <summary>
        /// Gets the exchange.
        /// </summary>
        public string Exchange { get; }

        /// <summary>
        /// Gets the API endpoint that was called.
        /// </summary>
        public string ApiEndpoint { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ExchangeApiException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="exchange">The exchange.</param>
        public ExchangeApiException(string message, string exchange)
            : base(message, "EXCHANGE_API_ERROR")
        {
            Exchange = exchange;
            AddContext("Exchange", exchange);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ExchangeApiException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="exchange">The exchange.</param>
        /// <param name="innerException">The inner exception.</param>
        public ExchangeApiException(string message, string exchange, Exception innerException)
            : base(message, "EXCHANGE_API_ERROR", innerException)
        {
            Exchange = exchange;
            AddContext("Exchange", exchange);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ExchangeApiException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="exchange">The exchange.</param>
        /// <param name="apiEndpoint">The API endpoint.</param>
        /// <param name="innerException">The inner exception.</param>
        public ExchangeApiException(string message, string exchange, string apiEndpoint, Exception innerException)
            : base(message, "EXCHANGE_API_ERROR", innerException)
        {
            Exchange = exchange;
            ApiEndpoint = apiEndpoint;

            AddContext("Exchange", exchange);
            AddContext("ApiEndpoint", apiEndpoint);
        }

        /// <summary>
        /// Used for serialization purposes.
        /// </summary>
        protected ExchangeApiException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            Exchange = info.GetString(nameof(Exchange));
            ApiEndpoint = info.GetString(nameof(ApiEndpoint));
        }

        /// <summary>
        /// Serializes the exception data.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            info.AddValue(nameof(Exchange), Exchange);
            info.AddValue(nameof(ApiEndpoint), ApiEndpoint);
            base.GetObjectData(info, context);
        }
    }

    [Serializable]
    public class AssetFetchException : DomainException
    {
        public AssetFetchException(string message)
            : base(message, "ASSET_FETCH_ERROR") { }

        public AssetFetchException(string message, Exception inner)
            : base(message, "ASSET_FETCH_ERROR", inner) { }
    }

    [Serializable]
    public class BalanceFetchException : DomainException
    {
        public BalanceFetchException(string message)
            : base(message, "BALANCE_FETCH_ERROR") { }

        public BalanceFetchException(string message, Exception inner)
            : base(message, "BALANCE_FETCH_ERROR", inner) { }
    }

    [Serializable]
    public class SubscriptionFetchException : DomainException
    {
        public SubscriptionFetchException(string message)
            : base(message, "SUBSCRIPTION_FETCH_ERROR") { }

        public SubscriptionFetchException(string message, Exception inner)
            : base(message, "SUBSCRIPTION_FETCH_ERROR", inner) { }
    }

    /// <summary>
    /// Exception thrown when a payment processing error occurs
    /// </summary>
    [Serializable]
    public class PaymentApiException : DomainException
    {
        /// <summary>
        /// Gets the payment provider.
        /// </summary>
        public string PaymentProvider { get; }

        /// <summary>
        /// Gets the payment ID.
        /// </summary>
        public string PaymentId { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PaymentApiException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="paymentProvider">The payment provider.</param>
        /// <param name="paymentId">The payment ID.</param>
        public PaymentApiException(string message, string paymentProvider, string paymentId = null)
            : base(message, "PAYMENT_PROCESSING_ERROR")
        {
            PaymentProvider = paymentProvider;
            PaymentId = paymentId;

            AddContext("PaymentProvider", paymentProvider);
            if (!string.IsNullOrEmpty(paymentId))
            {
                AddContext("PaymentId", paymentId);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PaymentApiException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="paymentProvider">The payment provider.</param>
        /// <param name="paymentId">The payment ID.</param>
        /// <param name="innerException">The inner exception.</param>
        public PaymentApiException(string message, string paymentProvider, string paymentId, Exception innerException)
            : base(message, "PAYMENT_PROCESSING_ERROR", innerException)
        {
            PaymentProvider = paymentProvider;
            PaymentId = paymentId;

            AddContext("PaymentProvider", paymentProvider);
            if (!string.IsNullOrEmpty(paymentId))
            {
                AddContext("PaymentId", paymentId);
            }
        }

        /// <summary>
        /// Used for serialization purposes.
        /// </summary>
        protected PaymentApiException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            PaymentProvider = info.GetString(nameof(PaymentProvider));
            PaymentId = info.GetString(nameof(PaymentId));
        }

        /// <summary>
        /// Serializes the exception data.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            info.AddValue(nameof(PaymentProvider), PaymentProvider);
            info.AddValue(nameof(PaymentId), PaymentId);
            base.GetObjectData(info, context);
        }
    }

    /// <summary>
    /// Exception thrown when a third-party service is unavailable
    /// </summary>
    [Serializable]
    public class ServiceUnavailableException : DomainException
    {
        /// <summary>
        /// Gets the service name.
        /// </summary>
        public string ServiceName { get; }

        /// <summary>
        /// Gets the retry after seconds.
        /// </summary>
        public int? RetryAfterSeconds { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ServiceUnavailableException"/> class.
        /// </summary>
        /// <param name="serviceName">The service name.</param>
        /// <param name="message">The error message.</param>
        public ServiceUnavailableException(string serviceName, string message = null)
            : base(message ?? $"Service {serviceName} is currently unavailable", "SERVICE_UNAVAILABLE")
        {
            ServiceName = serviceName;
            AddContext("ServiceName", serviceName);
        }

        /// <summary>
        /// Sets the retry after seconds.
        /// </summary>
        /// <param name="seconds">The seconds to wait before retrying.</param>
        /// <returns>This exception instance for method chaining.</returns>
        public ServiceUnavailableException WithRetryAfter(int seconds)
        {
            RetryAfterSeconds = seconds;
            AddContext("RetryAfterSeconds", seconds);
            return this;
        }

        /// <summary>
        /// Used for serialization purposes.
        /// </summary>
        protected ServiceUnavailableException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            ServiceName = info.GetString(nameof(ServiceName));
            RetryAfterSeconds = (int?)info.GetValue(nameof(RetryAfterSeconds), typeof(int?));
        }

        /// <summary>
        /// Serializes the exception data.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            info.AddValue(nameof(ServiceName), ServiceName);
            info.AddValue(nameof(RetryAfterSeconds), RetryAfterSeconds);
            base.GetObjectData(info, context);
        }
    }

    /// <summary>
    /// Exception thrown when a database operation fails
    /// </summary>
    [Serializable]
    public class DatabaseException : DomainException
    {
        /// <summary>
        /// Gets the collection name.
        /// </summary>
        public string CollectionName { get; private set; }

        /// <summary>
        /// Gets the operation type.
        /// </summary>
        public string OperationType { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="DatabaseException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        public DatabaseException(string message)
            : base(message, "DATABASE_ERROR")
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DatabaseException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The inner exception.</param>
        public DatabaseException(string message, Exception innerException)
            : base(message, "DATABASE_ERROR", innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="DatabaseException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="collectionName">The collection name.</param>
        /// <param name="operationType">The operation type.</param>
        /// <param name="innerException">The inner exception.</param>
        public DatabaseException(string message, string collectionName, string operationType, Exception innerException)
            : base(message, "DATABASE_ERROR", innerException)
        {
            CollectionName = collectionName;
            OperationType = operationType;

            AddContext("CollectionName", collectionName);
            AddContext("OperationType", operationType);
        }

        /// <summary>
        /// Used for serialization purposes.
        /// </summary>
        protected DatabaseException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            CollectionName = info.GetString(nameof(CollectionName));
            OperationType = info.GetString(nameof(OperationType));
        }

        /// <summary>
        /// Serializes the exception data.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            info.AddValue(nameof(CollectionName), CollectionName);
            info.AddValue(nameof(OperationType), OperationType);
            base.GetObjectData(info, context);
        }
    }

    /// <summary>
    /// Exception thrown for concurrent modification conflicts
    /// </summary>
    [Serializable]
    public class ConcurrencyException : DomainException
    {
        /// <summary>
        /// Gets the resource type.
        /// </summary>
        public string ResourceType { get; }

        /// <summary>
        /// Gets the resource ID.
        /// </summary>
        public string ResourceId { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConcurrencyException"/> class.
        /// </summary>
        /// <param name="resourceType">The resource type.</param>
        /// <param name="resourceId">The resource ID.</param>
        public ConcurrencyException(string resourceType, string resourceId)
            : base($"Concurrency conflict detected for {resourceType} with ID {resourceId}", "CONCURRENCY_CONFLICT")
        {
            ResourceType = resourceType;
            ResourceId = resourceId;

            AddContext("ResourceType", resourceType);
            AddContext("ResourceId", resourceId);
        }

        /// <summary>
        /// Used for serialization purposes.
        /// </summary>
        protected ConcurrencyException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            ResourceType = info.GetString(nameof(ResourceType));
            ResourceId = info.GetString(nameof(ResourceId));
        }

        /// <summary>
        /// Serializes the exception data.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            info.AddValue(nameof(ResourceType), ResourceType);
            info.AddValue(nameof(ResourceId), ResourceId);
            base.GetObjectData(info, context);
        }
    }

    /// <summary>
    /// Exception thrown when a payment event cannot be processed
    /// </summary>
    [Serializable]
    public class PaymentEventException : DomainException
    {
        /// <summary>
        /// Gets the event type.
        /// </summary>
        public string EventType { get; }

        /// <summary>
        /// Gets the provider.
        /// </summary>
        public string Provider { get; }

        /// <summary>
        /// Gets the event ID.
        /// </summary>
        public string EventId { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PaymentEventException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="eventType">The event type.</param>
        /// <param name="provider">The provider.</param>
        /// <param name="eventId">The event ID.</param>
        public PaymentEventException(string message, string eventType, string provider, string eventId = null)
            : base(message, "PAYMENT_EVENT_ERROR")
        {
            EventType = eventType;
            Provider = provider;
            EventId = eventId;

            AddContext("EventType", eventType);
            AddContext("Provider", provider);
            if (!string.IsNullOrEmpty(eventId))
            {
                AddContext("EventId", eventId);
            }
        }

        /// <summary>
        /// Used for serialization purposes.
        /// </summary>
        protected PaymentEventException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            EventType = info.GetString(nameof(EventType));
            Provider = info.GetString(nameof(Provider));
            EventId = info.GetString(nameof(EventId));
        }

        /// <summary>
        /// Serializes the exception data.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            info.AddValue(nameof(EventType), EventType);
            info.AddValue(nameof(Provider), Provider);
            info.AddValue(nameof(EventId), EventId);
            base.GetObjectData(info, context);
        }
    }

    /// <summary>
    /// Exception thrown when a security-related operation fails
    /// </summary>
    [Serializable]
    public class SecurityException : DomainException
    {
        /// <summary>
        /// Gets the operation type.
        /// </summary>
        public string OperationType { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="SecurityException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="operationType">The operation type.</param>
        public SecurityException(string message, string operationType)
            : base(message, "SECURITY_ERROR")
        {
            OperationType = operationType;
            AddContext("OperationType", operationType);
        }

        /// <summary>
        /// Used for serialization purposes.
        /// </summary>
        protected SecurityException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            OperationType = info.GetString(nameof(OperationType));
        }

        /// <summary>
        /// Serializes the exception data.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            info.AddValue(nameof(OperationType), OperationType);
            base.GetObjectData(info, context);
        }
    }
}