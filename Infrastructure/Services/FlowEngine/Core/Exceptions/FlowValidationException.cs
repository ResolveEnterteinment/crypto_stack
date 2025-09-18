using System;

namespace Infrastructure.Services.FlowEngine.Core.Exceptions
{
    /// <summary>
    /// Exception thrown when flow validation fails
    /// </summary>
    public class FlowValidationException : Exception
    {
        public FlowValidationException(string message) : base(message) { }
        public FlowValidationException(string message, Exception innerException) : base(message, innerException) { }
    }
}