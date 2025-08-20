using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Exceptions
{
    public sealed class FlowNotFoundException : Exception
    {
        public FlowNotFoundException(string message) : base(message) { }
        public FlowNotFoundException(string message, Exception innerException) : base(message, innerException) { }
    }
}
