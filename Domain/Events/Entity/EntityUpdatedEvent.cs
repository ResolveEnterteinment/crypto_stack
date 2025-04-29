using Domain.Models;
using MediatR;

namespace Domain.Events
{
    // Event for MediatR
    public class EntityUpdatedEvent<T> : BaseEvent, INotification where T : BaseEntity
    {
        public T Entity { get; }
        public EntityUpdatedEvent(Guid id, T entity, IDictionary<string, object?> context) : base(context)
        {
            DomainEntityId = id;
            Entity = entity;
        }
    }
}