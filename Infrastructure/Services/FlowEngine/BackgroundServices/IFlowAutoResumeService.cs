using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.BackgroundServices
{
    public interface IFlowAutoResumeService
    {
        Task<int> CheckAndResumeFlowsAsync(CancellationToken cancellationToken);
    }
}
