using Application.Interfaces.Base;
using Domain.DTOs;
using Domain.Events;
using Domain.Models.Event;

namespace Application.Interfaces
{
    public interface IEventService : IBaseService<EventData>
    {
        public Task Publish(BaseEvent eventToPublish);
        public Task<ResultWrapper<IEnumerable<EventData>>> GetRecentEventsAsync(string eventType, int limit = 20);
    }
}
