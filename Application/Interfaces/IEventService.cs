using Domain.Events;
using Domain.Interfaces;
using Domain.Modals.Event;

namespace Application.Interfaces
{
    public interface IEventService : IRepository<EventData>
    {
        public Task Publish(BaseEvent eventType);
    }
}
