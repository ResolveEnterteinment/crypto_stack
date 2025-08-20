using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.BackgroundServices
{
    /// <summary>
    /// Channel-based background task queue implementation
    /// </summary>
    public sealed class BackgroundTaskQueue : IBackgroundTaskQueue
    {
        private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _queue;
        private readonly ILogger<BackgroundTaskQueue> _logger;

        public BackgroundTaskQueue(ILogger<BackgroundTaskQueue> logger, int capacity = 1000)
        {
            _logger = logger;

            var options = new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            };

            _queue = Channel.CreateBounded<Func<IServiceProvider, CancellationToken, Task>>(options);
        }

        public async Task QueueBackgroundWorkItemAsync(Func<IServiceProvider, CancellationToken, Task> workItem, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(workItem);

            try
            {
                await _queue.Writer.WriteAsync(workItem, cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("Background work item queued. Queue count: {Count}", Count);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Failed to queue background work item: operation was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to queue background work item");
                throw;
            }
        }

        public async Task<Func<IServiceProvider, CancellationToken, Task>?> DequeueAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await _queue.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        public int Count => _queue.Reader.CanCount ? _queue.Reader.Count : 0;
    }
}
