using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Utilities;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

/// <summary>
/// Serializable flow state for persistence
/// </summary>
public class FlowState
{
    [BsonId]
    public Guid Id { get; set; } // Database primary key

    [BsonElement("flowId")]
    public Guid FlowId { get; set; }

    [BsonElement("flowType")]
    public string FlowType { get; set; } = "";
    [BsonElement("triggeredBy")]
    public Guid? TriggeredBy { get; set; } = null;

    [BsonElement("userId")]
    public string UserId { get; set; } = "system";

    [BsonElement("correlationId")]
    public string CorrelationId { get; set; } = "";

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
    public string CurrentStepName { get; set; }

    [BsonElement("currentStepIndex")]
    public int CurrentStepIndex { get; set; } = 0;

    [BsonElement("version")]
    public long Version { get; set; } = 1; // For optimistic concurrency

    // State data
    [BsonElement("data")]
    public Dictionary<string, SafeObject> Data { get; set; } = new();

    [BsonElement("steps")]
    public List<StepState> Steps { get; set; } = new();

    [BsonElement("events")]
    public List<FlowEvent> Events { get; set; } = new();

    [BsonElement("lastError")]
    public Exception? LastError { get; set; }

    // Pause/Resume state
    [BsonElement("pausedAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    [BsonIgnoreIfNull]
    public DateTime? PausedAt { get; set; }

    [BsonElement("pauseReason")]
    [BsonIgnoreIfNull]
    public PauseReason? PauseReason { get; set; }

    [BsonElement("pauseMessage")]
    [BsonIgnoreIfNull]
    public string? PauseMessage { get; set; }

    [BsonElement("pauseData")]
    public Dictionary<string, SafeObject> PauseData { get; set; } = new();

    // Metadata
    [BsonElement("lastUpdatedAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("lastUpdatedBy")]
    public string LastUpdatedBy { get; set; } = "system";

    /// <summary>
    /// Get typed data from flow state
    /// </summary>
    public T GetData<T>(string key)
    {
        return Data.TryGetValue(key, out var safeObj) ? safeObj.ToValue<T>() : default(T);
    }

    /// <summary>
    /// Set data in flow state
    /// </summary>
    public void SetData(string key, object value)
    {
        Data[key] = SafeObject.FromValue(value);
        Version++;
    }

    public void SetData(Dictionary<string, object> data)
    {
        foreach (var kvp in data)
        {
            Data[kvp.Key] = SafeObject.FromValue(kvp.Value);
        }
        Version++;
    }
}