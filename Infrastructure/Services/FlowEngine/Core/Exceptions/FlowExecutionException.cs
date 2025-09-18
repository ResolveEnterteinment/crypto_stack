namespace Infrastructure.Services.FlowEngine.Core.Exceptions
{
    public class FlowExecutionException : Exception
    {
        public FlowExecutionException() : base("Flow execution failed") { }
        public FlowExecutionException(string message) : base(message) { }
        public FlowExecutionException(string message, Exception innerException) : base(message, innerException) { }
    }
}