using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Core.Exceptions
{
    public class FlowNotFoundException : Exception
    {
        public FlowNotFoundException(string message) : base(message) { }
    }
}
