using Domain.DTOs;
using Domain.Events;
using Domain.Models.Event;

namespace Application.Interfaces.Base
{
    public interface IEventService
    {
        Task PublishAsync(BaseEvent eventToPublish);
        Task<ResultWrapper<bool>> MarkAsProcessedAsync(Guid eventId);
        Task<ResultWrapper<bool>> MarkAsFailedAsync(Guid eventId, string errorMessage);
        Task<ResultWrapper<PaginatedResult<EventData>>> GetRecentEventsAsync(string eventType, int limit = 20);
        Task<ResultWrapper<PaginatedResult<EventData>>> GetUnprocessedEventsAsync(Type eventType, int limit = 100);
    }
}
