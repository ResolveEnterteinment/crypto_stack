using Infrastructure.Services.FlowEngine.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Events
{
    public interface IFlowEventService
    {
        Task PublishAsync(SignedEvent signedEvent, CancellationToken cancellationToken);
        Task SubscribeAsync(string eventType, Func<SignedEvent, CancellationToken, Task> handler);
    }
}
