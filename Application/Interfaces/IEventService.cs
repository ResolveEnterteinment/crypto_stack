using Domain.Events;
using Domain.Models.Event;

namespace Application.Interfaces
{
    public interface IEventService : IRepository<EventData>
    {
        public Task Publish(BaseEvent eventToPublish);
    }
}
