using System.Runtime.Serialization;

namespace Domain.Exceptions
{
    /// <summary>
    /// Exception thrown when an external service call fails
    /// </summary>
    [Serializable]
    public class ExternalServiceException : DomainException
    {
        /// <summary>
        /// Gets the service name.
        /// </summary>
        public string ServiceName { get; }

        /// <summary>
        /// Gets the operation that was being performed.
        /// </summary>
        public string Operation { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ExternalServiceException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="serviceName">The name of the external service.</param>
        public ExternalServiceException(string message, string serviceName)
            : base(message, "EXTERNAL_SERVICE_ERROR")
        {
            ServiceName = serviceName;
            _ = AddContext("ServiceName", serviceName);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ExternalServiceException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="serviceName">The name of the external service.</param>
        /// <param name="innerException">The inner exception.</param>
        public ExternalServiceException(string message, string serviceName, Exception innerException)
            : base(message, "EXTERNAL_SERVICE_ERROR", innerException)
        {
            ServiceName = serviceName;
            _ = AddContext("ServiceName", serviceName);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ExternalServiceException"/> class.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="serviceName">The name of the external service.</param>
        /// <param name="operation">The operation that was being performed.</param>
        /// <param name="innerException">The inner exception.</param>
        public ExternalServiceException(string message, string serviceName, string operation, Exception innerException)
            : base(message, "EXTERNAL_SERVICE_ERROR", innerException)
        {
            ServiceName = serviceName;
            Operation = operation;

            _ = AddContext("ServiceName", serviceName);
            _ = AddContext("Operation", operation);
        }

        /// <summary>
        /// Sets the operation that was being performed.
        /// </summary>
        /// <param name="operation">The operation name.</param>
        /// <returns>This exception instance for method chaining.</returns>
        public ExternalServiceException WithOperation(string operation)
        {
            Operation = operation;
            _ = AddContext("Operation", operation);
            return this;
        }

        /// <summary>
        /// Used for serialization purposes.
        /// </summary>
        protected ExternalServiceException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            ServiceName = info.GetString(nameof(ServiceName)) ?? string.Empty;
            Operation = info.GetString(nameof(Operation)) ?? string.Empty;
        }

        /// <summary>
        /// Serializes the exception data.
        /// </summary>
        /// <param name="info">The serialization info.</param>
        /// <param name="context">The streaming context.</param>
        [Obsolete]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            info.AddValue(nameof(ServiceName), ServiceName);
            info.AddValue(nameof(Operation), Operation);
            base.GetObjectData(info, context);
        }
    }
}