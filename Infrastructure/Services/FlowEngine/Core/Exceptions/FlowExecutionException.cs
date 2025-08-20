using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Core.Exceptions
{
    public class FlowExecutionException : Exception
    {
        public FlowExecutionException(string message) : base(message) { }
        public FlowExecutionException(string message, Exception innerException) : base(message, innerException) { }
    }
}
