using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Utilities;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Infrastructure.Services.FlowEngine.Core.Models
{
    // MongoDB document models
    // Update the FlowDocument class to use SafeObject dictionaries
    public class FlowDocument
    {
        [BsonId]
        public Guid Id { get; set; }

        [BsonElement("flowId")]
        public Guid FlowId { get; set; }

        [BsonElement("flowType")]
        public string FlowType { get; set; }

        [BsonElement("userId")]
        public string UserId { get; set; }

        [BsonElement("correlationId")]
        public string CorrelationId { get; set; }

        [BsonElement("createdAt")]
        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime CreatedAt { get; set; }

        [BsonElement("startedAt")]
        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        [BsonIgnoreIfNull]
        public DateTime? StartedAt { get; set; }

        [BsonElement("completedAt")]
        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        [BsonIgnoreIfNull]
        public DateTime? CompletedAt { get; set; }

        [BsonElement("status")]
        [BsonRepresentation(BsonType.String)]
        public FlowStatus Status { get; set; }

        [BsonElement("currentStepName")]
        [BsonIgnoreIfNull]
        public string CurrentStepName { get; set; }

        [BsonElement("currentStepIndex")]
        public int CurrentStepIndex { get; set; }

        [BsonElement("steps")]
        public List<StepState> Steps { get; set; } = new List<StepState>();

        // CHANGED: Use Dictionary<string, SafeObject> instead of Dictionary<string, object>
        [BsonElement("data")]
        [BsonIgnoreIfNull]
        public Dictionary<string, SafeObject> Data { get; set; }

        [BsonElement("events")]
        [BsonIgnoreIfNull]
        public List<FlowEvent> Events { get; set; }

        [BsonElement("lastError")]
        [BsonIgnoreIfNull]
        public Exception LastError { get; set; }

        // Pause/Resume state
        [BsonElement("pausedAt")]
        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        [BsonIgnoreIfNull]
        public DateTime? PausedAt { get; set; }

        [BsonElement("pauseReason")]
        [BsonRepresentation(BsonType.String)]
        [BsonIgnoreIfNull]
        public PauseReason? PauseReason { get; set; }

        [BsonElement("pauseMessage")]
        [BsonIgnoreIfNull]
        public string PauseMessage { get; set; }

        // CHANGED: Use Dictionary<string, SafeObject> instead of Dictionary<string, object>
        [BsonElement("pauseData")]
        [BsonIgnoreIfNull]
        public Dictionary<string, SafeObject> PauseData { get; set; }

        // Full serialized flow for complex scenarios
        [BsonElement("serializedFlow")]
        [BsonIgnoreIfNull]
        public string SerializedFlow { get; set; }

        [BsonElement("lastUpdatedAt")]
        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime UpdatedAt { get; set; }
    }
}
