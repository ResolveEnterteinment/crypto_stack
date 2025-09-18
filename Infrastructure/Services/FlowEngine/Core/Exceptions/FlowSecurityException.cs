using System;

namespace Infrastructure.Services.FlowEngine.Core.Exceptions
{
    /// <summary>
    /// Exception thrown when flow validation fails
    /// </summary>
    public class FlowSecurityException : Exception
    {
        public FlowSecurityException(string message) : base(message) { }
        public FlowSecurityException(string message, Exception innerException) : base(message, innerException) { }
    }
}