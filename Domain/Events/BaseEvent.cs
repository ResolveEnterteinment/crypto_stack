namespace Domain.Events
{
    public class BaseEvent
    {
        public Guid EventId { get; set; }
        public Guid DomainEntityId { get; set; }
    }
}
