namespace Domain.Events
{
    public class BaseEvent
    {
        public Guid EventId { get; set; }
        public IDictionary<string, object?> Context { get; }
        public Guid DomainEntityId { get; set; }

        protected BaseEvent(IDictionary<string, object?> context)
        {
            Context = context;
        }
    }
}
