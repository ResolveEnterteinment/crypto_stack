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
    private Guid _id;
    private Guid _flowId;
    private string _flowType = "";
    private TriggeredFlowData? _triggeredBy = null;
    private string? _userId = null;
    private string? _userEmail = null;
    private string _correlationId = "";
    private DateTime _createdAt;
    private DateTime? _startedAt;
    private DateTime? _completedAt;
    private FlowStatus _status;
    private string _currentStepName;
    private int _currentStepIndex = 0;
    private long _version = 1;
    private Dictionary<string, SafeObject> _data = new();
    private List<StepState> _steps = new();
    private List<FlowEvent> _events = new();
    private Exception? _lastError;
    private DateTime? _pausedAt;
    private PauseReason? _pauseReason;
    private string? _pauseMessage;
    private SafeObject _pauseData = new();
    private DateTime? _cancelledAt;
    private string? _cancelReason;
    private DateTime _lastUpdatedAt = DateTime.UtcNow;
    private string _lastUpdatedBy = "system";

    /// <summary>
    /// Indicates whether the flow state has been modified and needs persistence
    /// </summary>
    [BsonIgnore]
    public bool IsDirty { get; private set; } = false;

    [BsonId]
    public Guid Id
    {
        get => _id;
        set
        {
            if (_id != value)
            {
                _id = value;
                MarkDirty();
            }
        }
    }

    [BsonElement("flowId")]
    public Guid FlowId
    {
        get => _flowId;
        set
        {
            if (_flowId != value)
            {
                _flowId = value;
                MarkDirty();
            }
        }
    }

    [BsonElement("flowType")]
    public string FlowType
    {
        get => _flowType;
        set
        {
            if (_flowType != value)
            {
                _flowType = value ?? "";
                MarkDirty();
            }
        }
    }

    [BsonElement("triggeredBy")]
    public TriggeredFlowData? TriggeredBy
    {
        get => _triggeredBy;
        set
        {
            if (_triggeredBy != value)
            {
                _triggeredBy = value;
                MarkDirty();
            }
        }
    }

    [BsonElement("userId")]
    public string? UserId
    {
        get => _userId;
        set
        {
            if (_userId != value)
            {
                _userId = value ?? null;
                MarkDirty();
            }
        }
    }

    [BsonElement("userEmail")]
    public string? UserEmail
    {
        get => _userEmail;
        set
        {
            if (_userEmail != value)
            {
                _userEmail = value ?? null;
                MarkDirty();
            }
        }
    }

    [BsonElement("correlationId")]
    public string CorrelationId
    {
        get => _correlationId;
        set
        {
            if (_correlationId != value)
            {
                _correlationId = value ?? "";
                MarkDirty();
            }
        }
    }

    [BsonElement("createdAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt
    {
        get => _createdAt;
        set
        {
            if (_createdAt != value)
            {
                _createdAt = value;
                MarkDirty();
            }
        }
    }

    [BsonElement("startedAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    [BsonIgnoreIfNull]
    public DateTime? StartedAt
    {
        get => _startedAt;
        set
        {
            if (_startedAt != value)
            {
                _startedAt = value;
                MarkDirty();
            }
        }
    }

    [BsonElement("completedAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    [BsonIgnoreIfNull]
    public DateTime? CompletedAt
    {
        get => _completedAt;
        set
        {
            if (_completedAt != value)
            {
                _completedAt = value;
                MarkDirty();
            }
        }
    }

    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public FlowStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                MarkDirty();
            }
        }
    }

    [BsonElement("currentStepName")]
    public string CurrentStepName
    {
        get => _currentStepName;
        set
        {
            if (_currentStepName != value)
            {
                _currentStepName = value;
                MarkDirty();
            }
        }
    }

    [BsonElement("currentStepIndex")]
    public int CurrentStepIndex
    {
        get => _currentStepIndex;
        set
        {
            if (_currentStepIndex != value)
            {
                _currentStepIndex = value;
                MarkDirty();
            }
        }
    }

    [BsonElement("version")]
    public long Version
    {
        get => _version;
        set
        {
            if (_version != value)
            {
                _version = value;
                MarkDirty();
            }
        }
    }

    // State data
    [BsonElement("data")]
    public Dictionary<string, SafeObject> Data
    {
        get => _data;
        set
        {
            if (_data != value)
            {
                _data = value ?? new Dictionary<string, SafeObject>();
                MarkDirty();
            }
        }
    }

    [BsonElement("steps")]
    public List<StepState> Steps
    {
        get => _steps;
        set
        {
            if (_steps != value)
            {
                _steps = value ?? new List<StepState>();
                MarkDirty();
            }
        }
    }

    [BsonElement("events")]
    public List<FlowEvent> Events
    {
        get => _events;
        set
        {
            if (_events != value)
            {
                _events = value ?? new List<FlowEvent>();
                MarkDirty();
            }
        }
    }

    [BsonElement("lastError")]
    [BsonIgnoreIfNull]
    public Exception? LastError
    {
        get => _lastError;
        set
        {
            if (_lastError != value)
            {
                _lastError = value;
                MarkDirty();
            }
        }
    }

    // Pause/Resume state
    [BsonElement("pausedAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    [BsonIgnoreIfNull]
    public DateTime? PausedAt
    {
        get => _pausedAt;
        set
        {
            if (_pausedAt != value)
            {
                _pausedAt = value;
                MarkDirty();
            }
        }
    }

    [BsonElement("pauseReason")]
    [BsonIgnoreIfNull]
    public PauseReason? PauseReason
    {
        get => _pauseReason;
        set
        {
            if (_pauseReason != value)
            {
                _pauseReason = value;
                MarkDirty();
            }
        }
    }

    [BsonElement("pauseMessage")]
    [BsonIgnoreIfNull]
    public string? PauseMessage
    {
        get => _pauseMessage;
        set
        {
            if (_pauseMessage != value)
            {
                _pauseMessage = value;
                MarkDirty();
            }
        }
    }

    [BsonElement("pauseData")]
    public SafeObject PauseData
    {
        get => _pauseData;
        set
        {
            if (_pauseData != value)
            {
                _pauseData = value;
                MarkDirty();
            }
        }
    }

    // Cancel state
    [BsonElement("cancelledAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    [BsonIgnoreIfNull]
    public DateTime? CancelledAt
    {
        get => _cancelledAt;
        set
        {
            if (_cancelledAt != value)
            {
                _cancelledAt = value;
                MarkDirty();
            }
        }
    }

    [BsonElement("cancelReason")]
    [BsonIgnoreIfNull]
    public string? CancelReason
    {
        get => _cancelReason;
        set
        {
            if (_cancelReason != value)
            {
                _cancelReason = value;
                MarkDirty();
            }
        }
    }

    // Metadata
    [BsonElement("lastUpdatedAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime LastUpdatedAt
    {
        get => _lastUpdatedAt;
        set
        {
            if (_lastUpdatedAt != value)
            {
                _lastUpdatedAt = value;
                MarkDirty();
            }
        }
    }

    [BsonElement("lastUpdatedBy")]
    public string LastUpdatedBy
    {
        get => _lastUpdatedBy;
        set
        {
            if (_lastUpdatedBy != value)
            {
                _lastUpdatedBy = value ?? "system";
                MarkDirty();
            }
        }
    }

    /// <summary>
    /// Marks the state as dirty, indicating it needs persistence
    /// </summary>
    private void MarkDirty()
    {
        IsDirty = true;
        _lastUpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks the state as clean after successful persistence
    /// </summary>
    public void MarkClean()
    {
        IsDirty = false;
    }

    /// <summary>
    /// Get typed data from flow state
    /// </summary>
    public T? GetData<T>(string key)
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