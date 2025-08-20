using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Core.Exceptions
{
    public class RateLimitException : Exception
    {
        public TimeSpan RetryAfter { get; }

        public RateLimitException(TimeSpan retryAfter) : base($"Rate limit exceeded, retry after {retryAfter}")
        {
            RetryAfter = retryAfter;
        }
    }
}
