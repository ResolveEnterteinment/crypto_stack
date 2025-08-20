using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Exceptions
{
    public sealed class FlowExecutionException : Exception
    {
        public FlowExecutionException(string message) : base(message) { }
        public FlowExecutionException(string message, Exception innerException) : base(message, innerException) { }
    }
}
