namespace Domain.Exceptions.Withdrawal
{
    public class WithdrawalLimitException : Exception
    {
        private const string DefaultMessage = "KYC verification failed.";

        /// <summary>
        /// Initializes a new instance of the <see cref="WithdrawalLimitException"/> class with a default error message.
        /// </summary>
        public WithdrawalLimitException()
            : base(DefaultMessage)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WithdrawalLimitException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">The error message that explains the reason for this exception.</param>
        public WithdrawalLimitException(string? message)
            : base(string.IsNullOrWhiteSpace(message) ? DefaultMessage : message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WithdrawalLimitException"/> class with a specified error message 
        /// and a reference to the inner exception that is the cause of this exception.
        /// </summary>
        /// <param name="message">The error message that explains the reason for this exception.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public WithdrawalLimitException(string? message, Exception? innerException)
            : base(string.IsNullOrWhiteSpace(message) ? DefaultMessage : message, innerException)
        {
        }
    }
}
