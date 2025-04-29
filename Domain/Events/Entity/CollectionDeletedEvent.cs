using Domain.Models;
using MediatR;

namespace Domain.Events
{
    // Event for MediatR
    public class CollectionDeletedEvent<T> : BaseEvent, INotification where T : BaseEntity
    {
        public List<T> Collection { get; }
        public CollectionDeletedEvent(List<T> collection, IDictionary<string, object?> context)
            : base(context)
        {
            Collection = collection;
        }
    }
}