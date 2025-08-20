using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.BackgroundServices
{
    public sealed class FlowAutoResumeService : IFlowAutoResumeService
    {
        public Task<int> CheckAndResumeFlowsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }
    }
}
