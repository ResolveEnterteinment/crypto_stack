using System;

namespace Infrastructure.Services.FlowEngine.Core.Exceptions
{
    /// <summary>
    /// Exception thrown when flow validation fails
    /// </summary>
    public class FlowValidationException : FlowExecutionException
    {
        public FlowValidationException(string message) : base(message) { }
        public FlowValidationException(string message, Exception innerException) : base(message, innerException) { }
    }
}