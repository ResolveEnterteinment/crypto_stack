using Infrastructure.Services.FlowEngine.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Events
{
    public sealed class EnhancedFlowEventService : IFlowEventService
    {
        public Task PublishAsync(SignedEvent signedEvent, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task SubscribeAsync(string eventType, Func<SignedEvent, CancellationToken, Task> handler)
        {
            return Task.CompletedTask;
        }
    }
}
