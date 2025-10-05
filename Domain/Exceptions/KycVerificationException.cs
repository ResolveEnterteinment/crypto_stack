namespace Domain.Exceptions.KYC
{
    public class KycVerificationException : DomainException
    {
        private const string DefaultMessage = "KYC verification failed.";

        /// <summary>
        /// Initializes a new instance of the <see cref="KycVerificationException"/> class with a default error message.
        /// </summary>
        public KycVerificationException()
            : base(DefaultMessage, "KYC_VERIFICATION_ERROR")
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="KycVerificationException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">The error message that explains the reason for this exception.</param>
        public KycVerificationException(string? message)
            : base(string.IsNullOrWhiteSpace(message) ? DefaultMessage : message, "KYC_VERIFICATION_ERROR")
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="KycVerificationException"/> class with a specified error message 
        /// and a reference to the inner exception that is the cause of this exception.
        /// </summary>
        /// <param name="message">The error message that explains the reason for this exception.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public KycVerificationException(string? message, Exception? innerException)
            : base(string.IsNullOrWhiteSpace(message) ? DefaultMessage : message, "KYC_VERIFICATION_ERROR", innerException)
        {
        }
    }
}
