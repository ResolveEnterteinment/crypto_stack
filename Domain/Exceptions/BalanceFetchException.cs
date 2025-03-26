namespace Domain.Exceptions
{
    public class BalanceFetchException : Exception
    {
        private const string DefaultMessage = "";

        /// <summary>
        /// Initializes a new instance of the <see cref="BalanceFetchException"/> class with a default error message.
        /// </summary>
        public BalanceFetchException()
            : base(DefaultMessage)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BalanceFetchException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">The error message that explains the reason for this exception.</param>
        public BalanceFetchException(string? message)
            : base(string.IsNullOrWhiteSpace(message) ? DefaultMessage : message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BalanceFetchException"/> class with a specified error message 
        /// and a reference to the inner exception that is the cause of this exception.
        /// </summary>
        /// <param name="message">The error message that explains the reason for this exception.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public BalanceFetchException(string? message, Exception? innerException)
            : base(string.IsNullOrWhiteSpace(message) ? DefaultMessage : message, innerException)
        {
        }
    }
}
