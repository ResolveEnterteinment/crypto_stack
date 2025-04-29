using Domain.Models;
using MediatR;

namespace Domain.Events.Entity
{
    // Event for MediatR
    public class EntityCreatedEvent<T> : BaseEvent, INotification where T : BaseEntity
    {
        public T Entity { get; }
        public EntityCreatedEvent(Guid id, T entity, IDictionary<string, object?> context) :
            base(context)
        {
            DomainEntityId = id;
            Entity = entity;
        }
    }
}