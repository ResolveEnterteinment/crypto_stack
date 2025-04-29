using Domain.Models;
using MediatR;

namespace Domain.Events
{
    // Event for MediatR
    public class EntityDeletedEvent<T> : BaseEvent, INotification where T : BaseEntity
    {
        public T Entity { get; }
        public EntityDeletedEvent(Guid id, T entity, IDictionary<string, object?> context) : base(context)
        {
            DomainEntityId = id;
            Entity = entity;
        }
    }
}