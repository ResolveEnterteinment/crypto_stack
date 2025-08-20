using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.BackgroundServices
{
    /// <summary>
    /// Background task queue interface for reliable fire-and-forget operations
    /// </summary>
    public interface IBackgroundTaskQueue
    {
        Task QueueBackgroundWorkItemAsync(Func<IServiceProvider, CancellationToken, Task> workItem, CancellationToken cancellationToken = default);
        Task<Func<IServiceProvider, CancellationToken, Task>?> DequeueAsync(CancellationToken cancellationToken);
        int Count { get; }
    }
}
