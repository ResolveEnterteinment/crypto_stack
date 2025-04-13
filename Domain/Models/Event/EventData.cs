namespace Domain.Models.Event
{
    // Persistent event model for MongoDB
    public class EventData : BaseEntity
    {
        public required string Name { get; set; }
        public object Payload { get; set; }
        public bool Processed { get; set; } = false;
        public DateTime? ProcessedAt { get; set; }
    }
}