using Infrastructure.Services.FlowEngine.Configuration.Options;
using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;
using Infrastructure.Utilities;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Options;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using System.Text.Json;

namespace Infrastructure.Services.FlowEngine.Services.Persistence
{
    public class MongoFlowPersistence : IFlowPersistence
    {
        private readonly IMongoDatabase _database;
        private readonly IMongoCollection<FlowDocument> _flowsCollection;
        private readonly IMongoCollection<ResumeConditionDocument> _resumeConditionsCollection;
        private readonly ILogger<MongoFlowPersistence> _logger;
        private readonly IServiceProvider _serviceProvider;

        public MongoFlowPersistence(
            FlowEngineConfiguration config,
            ILogger<MongoFlowPersistence> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;

            if (string.IsNullOrEmpty(config.ConnectionString))
                throw new ArgumentException("MongoDB connection string is required", nameof(config.ConnectionString));

            var client = new MongoClient(config.ConnectionString);
            _database = client.GetDatabase(config.DatabaseName ?? "FlowEngine");
            _flowsCollection = _database.GetCollection<FlowDocument>("flows");
            _resumeConditionsCollection = _database.GetCollection<ResumeConditionDocument>("resumeConditions");

            // Configure MongoDB serialization for polymorphic types
            ConfigureMongoSerialization();

            // Create indexes for better performance
            CreateIndexesAsync().ConfigureAwait(false);
        }

        public async Task<FlowStatus> GetFlowStatusAsync(Guid flowId)
        {
            var filter = Builders<FlowDocument>.Filter.Eq(x => x.FlowId, flowId);
            var projection = Builders<FlowDocument>.Projection.Include(x => x.Status);

            var result = await _flowsCollection.Find(filter)
                .Project<FlowDocument>(projection)
                .FirstOrDefaultAsync();

            return result?.Status ?? FlowStatus.Failed;
        }

        public async Task<FlowDocument> GetByFlowId(Guid flowId)
        {
            var filter = Builders<FlowDocument>.Filter.Eq(x => x.FlowId, flowId);
            return await _flowsCollection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<FlowTimeline> GetFlowTimelineAsync(Guid flowId)
        {
            var filter = Builders<FlowDocument>.Filter.Eq(x => x.FlowId, flowId);
            var projection = Builders<FlowDocument>.Projection
                .Include(x => x.FlowId)
                .Include(x => x.Events);

            var flowDoc = await _flowsCollection.Find(filter)
                .Project<FlowDocument>(projection)
                .FirstOrDefaultAsync();

            if (flowDoc == null)
                return null;

            return new FlowTimeline
            {
                FlowId = flowId,
                Events = flowDoc.Events?.Select(e => new FlowTimelineEvent
                {
                    Timestamp = e.Timestamp,
                    EventType = e.EventType,
                    StepName = e.Data?.GetValueOrDefault("stepName")?.ToString(),
                    Status = e.Data?.ContainsKey("status") == true ?
                        Enum.Parse<FlowStatus>(e.Data["status"].ToString()) : FlowStatus.Running,
                    Message = e.Description,
                    Data = e.Data.FromSafe()  // Convert SafeObject back to object
                }).ToList() ?? new List<FlowTimelineEvent>()
            };
        }

        public async Task<PagedResult<FlowSummary>> QueryFlowsAsync(FlowQuery query)
        {
            var filterBuilder = Builders<FlowDocument>.Filter;
            var filter = filterBuilder.Empty;

            // Apply filters
            if (query.Status.HasValue)
                filter &= filterBuilder.Eq(x => x.Status, query.Status.Value);

            if (!string.IsNullOrEmpty(query.UserId))
                filter &= filterBuilder.Eq(x => x.UserId, query.UserId);

            if (!string.IsNullOrEmpty(query.CorrelationId))
                filter &= filterBuilder.Eq(x => x.CorrelationId, query.CorrelationId);

            if (!string.IsNullOrEmpty(query.FlowType))
                filter &= filterBuilder.Eq(x => x.FlowType, query.FlowType);

            if (query.CreatedAfter.HasValue)
                filter &= filterBuilder.Gte(x => x.CreatedAt, query.CreatedAfter.Value);

            if (query.CreatedBefore.HasValue)
                filter &= filterBuilder.Lte(x => x.CreatedAt, query.CreatedBefore.Value);

            if (query.PauseReason.HasValue)
                filter &= filterBuilder.Eq(x => x.PauseReason, query.PauseReason.Value);

            // Get total count
            var totalCount = await _flowsCollection.CountDocumentsAsync(filter);

            // Get paged results
            var skip = (query.PageNumber - 1) * query.PageSize;
            var flows = await _flowsCollection.Find(filter)
                .Skip(skip)
                .Limit(query.PageSize)
                .SortByDescending(x => x.CreatedAt)
                .ToListAsync();

            var summaries = flows.Select(f => new FlowSummary
            {
                FlowId = f.FlowId,
                FlowType = f.FlowType,
                Status = f.Status,
                UserId = f.UserId,
                CorrelationId = f.CorrelationId,
                CreatedAt = f.CreatedAt,
                StartedAt = f.StartedAt,
                CompletedAt = f.CompletedAt,
                LastUpdatedAt = f.UpdatedAt,
                CurrentStepName = f.CurrentStepName,
                PauseReason = f.PauseReason,
                ErrorMessage = f.LastError?.Message
            }).ToList();

            return new PagedResult<FlowSummary>
            {
                Items = summaries,
                TotalCount = (int)totalCount,
                PageSize = query.PageSize,
                PageNumber = query.PageNumber
            };
        }

        public async Task<List<FlowDocument>> GetRuntimeFlows()
        {
            return await GetFlowsByStatusesAsync(new[] { FlowStatus.Running, FlowStatus.Paused });
        }

        public async Task<List<FlowDocument>> GetFlowsByStatusesAsync(FlowStatus[] flowStatuses)
        {
            var filter = Builders<FlowDocument>.Filter.In(x => x.Status, flowStatuses);
            var flowDocs = await _flowsCollection.Find(filter).ToListAsync();

            return flowDocs;
        }


        public async Task<int> CleanupCompletedFlowsAsync(TimeSpan olderThan)
        {
            var cutoffDate = DateTime.UtcNow - olderThan;
            var filter = Builders<FlowDocument>.Filter.And(
                Builders<FlowDocument>.Filter.In(x => x.Status, new[] { FlowStatus.Completed, FlowStatus.Failed, FlowStatus.Cancelled }),
                Builders<FlowDocument>.Filter.Lt(x => x.CompletedAt, cutoffDate)
            );

            var result = await _flowsCollection.DeleteManyAsync(filter);
            _logger.LogInformation("Cleaned up {Count} completed flows older than {CutoffDate}", result.DeletedCount, cutoffDate);

            return (int)result.DeletedCount;
        }

        public async Task<bool> CancelFlowAsync(Guid flowId, string reason)
        {
            try
            {
                var filter = Builders<FlowDocument>.Filter.Eq(x => x.FlowId, flowId);

                // Create a MongoDB-safe FlowEvent using SafeObject
                var cancelEvent = new FlowEvent
                {
                    EventId = Guid.NewGuid().ToString(),
                    FlowId = flowId.ToString(),
                    EventType = "FlowCancelled",
                    Description = reason ?? "Flow cancelled",
                    Timestamp = DateTime.UtcNow,
                    Data = new Dictionary<string, object> { { "reason", reason ?? "Unknown" } }.ToSafe(_logger)
                };

                var update = Builders<FlowDocument>.Update
                    .Set(x => x.Status, FlowStatus.Cancelled)
                    .Set(x => x.CompletedAt, DateTime.UtcNow)
                    .Push(x => x.Events, cancelEvent);

                var result = await _flowsCollection.UpdateOneAsync(filter, update);
                return result.ModifiedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel flow {FlowId}", flowId);
                return false;
            }
        }

        public async Task<bool> ResumeFlowAsync(Guid flowId, ResumeReason reason, string resumedBy, string message = null)
        {
            try
            {
                var filter = Builders<FlowDocument>.Filter.And(
                    Builders<FlowDocument>.Filter.Eq(x => x.FlowId, flowId),
                    Builders<FlowDocument>.Filter.Eq(x => x.Status, FlowStatus.Paused)
                );

                // Create a MongoDB-safe FlowEvent using SafeObject
                var resumeEvent = new FlowEvent
                {
                    EventId = Guid.NewGuid().ToString(),
                    FlowId = flowId.ToString(),
                    EventType = "FlowResumed",
                    Description = message ?? $"Flow resumed by {resumedBy} (reason: {reason})",
                    Timestamp = DateTime.UtcNow,
                    Data = new Dictionary<string, object>
                    {
                        { "resumeReason", reason.ToString() },
                        { "resumedBy", resumedBy }
                    }.ToSafe(_logger)
                };

                var update = Builders<FlowDocument>.Update
                    .Set(x => x.Status, FlowStatus.Running)
                    .Unset(x => x.PausedAt)
                    .Unset(x => x.PauseReason)
                    .Unset(x => x.PauseMessage)
                    .Set(x => x.UpdatedAt, DateTime.UtcNow)
                    .Push(x => x.Events, resumeEvent);

                var result = await _flowsCollection.UpdateOneAsync(filter, update);

                if (result.ModifiedCount > 0)
                {
                    // Remove any resume conditions for this flow
                    await _resumeConditionsCollection.DeleteManyAsync(
                        Builders<ResumeConditionDocument>.Filter.Eq(x => x.FlowId, flowId));
                }

                return result.ModifiedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resume flow {FlowId}", flowId);
                return false;
            }
        }

        public async Task<bool> SetResumeConditionAsync(Guid flowId, ResumeCondition condition)
        {
            try
            {
                // Check if document already exists to preserve the _id
                var filter = Builders<ResumeConditionDocument>.Filter.Eq(x => x.FlowId, flowId);

                var existingDocument = await _resumeConditionsCollection.Find(filter)
                    .FirstOrDefaultAsync();

                var document = new ResumeConditionDocument
                {
                    Id = existingDocument != null ? existingDocument.Id : Guid.NewGuid(),
                    FlowId = flowId,
                    CheckInterval = condition.CheckInterval,
                    NextCheck = condition.NextCheck,
                    MaxRetries = condition.MaxRetries,
                    CurrentRetries = condition.CurrentRetries,
                    CreatedAt = DateTime.UtcNow
                };

                await _resumeConditionsCollection.ReplaceOneAsync(
                    filter,
                    document,
                    new ReplaceOptions { IsUpsert = true });

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set resume condition for flow {FlowId}", flowId);
                return false;
            }
        }

        public async Task SaveFlowStateAsync(FlowDefinition flow)
        {
            try
            {
                var filter = Builders<FlowDocument>.Filter.Eq(x => x.FlowId, flow.FlowId);

                // Check if document already exists to preserve the _id
                var existingDocument = await _flowsCollection.Find(filter)
                    .FirstOrDefaultAsync();

                var document = SerializeFlow(flow);
                document.UpdatedAt = DateTime.UtcNow;
                document.Id = existingDocument != null ? existingDocument.Id : Guid.NewGuid();

                await _flowsCollection.ReplaceOneAsync(filter, document, new ReplaceOptions { IsUpsert = true });

                _logger.LogDebug("Saved flow state for {FlowId} with status {Status}", flow.FlowId, flow.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save flow state for {FlowId}", flow.FlowId);
                throw;
            }
        }

        private FlowDocument SerializeFlow(FlowDefinition flow)
        {
            // Create a serializable version of the flow without function delegates
            string serializedFlow = null;
            try
            {
                // Create a simplified flow object for serialization that excludes function delegates
                var serializableFlow = new FlowDocument
                {
                    FlowId = flow.FlowId,
                    UserId = flow.UserId,
                    CorrelationId = flow.CorrelationId,
                    CreatedAt = flow.CreatedAt,
                    StartedAt = flow.StartedAt,
                    CompletedAt = flow.CompletedAt,
                    Status = flow.Status,
                    CurrentStepName = flow.CurrentStepName,
                    CurrentStepIndex = flow.CurrentStepIndex,
                    // SIMPLIFIED: Direct assignment since Data is already Dictionary<string, SafeObject>
                    Data = flow.Data,
                    // Safely serialize Events with their data
                    Events = flow.Events,
                    // Only serialize basic step information, not the executable functions
                    Steps = flow.Steps.Select(s => new StepData
                    {
                        Name = s.Name,
                        StepDependencies = s.StepDependencies,
                        // Convert DataDependencies to string representation
                        DataDependencies = s.DataDependencies?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.FullName),
                        MaxRetries = s.MaxRetries,
                        RetryDelay = s.RetryDelay,
                        Timeout = s.Timeout,
                        Priority = s.Priority,
                        Status = s.Status,
                        // SIMPLIFIED: Use SafeObject directly
                        SourceData = s.SourceData?.ToSafeObject(),
                        Metadata = s.Metadata?.ToSafe(_logger),
                        ResourceGroup = s.ResourceGroup,
                        IsCritical = s.IsCritical,
                        IsIdempotent = s.IsIdempotent,
                        CanRunInParallel = s.CanRunInParallel,
                        EstimatedDuration = s.EstimatedDuration,
                        // Safe serialization of StepResult
                        Result = s.Result != null ? new StepResult
                        {
                            IsSuccess = s.Result.IsSuccess,
                            Message = s.Result.Message,
                            Data = s.Result.Data
                        }: null
                    }).ToList(),
                    PausedAt = flow.PausedAt,
                    PauseReason = flow.PauseReason,
                    PauseMessage = flow.PauseMessage,
                    // SIMPLIFIED: Direct assignment since PauseData is already Dictionary<string, SafeObject>
                    PauseData = flow.PauseData,
                    // Exclude ActiveResumeConfig as it may contain function delegates
                };

                serializedFlow = JsonSerializer.Serialize(serializableFlow, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to serialize flow {FlowId} to JSON, continuing with basic data only", flow.FlowId);
                serializedFlow = null;
            }

            // Enhanced step serialization with safe handling of all complex data
            var mongoSafeSteps = flow.Steps?.Select(s => new StepData
            {
                Name = s.Name,
                Status = s.Status,
                StepDependencies = s.StepDependencies,
                DataDependencies = s.DataDependencies?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.FullName),
                // Safe handling of StepResult with SafeObject
                Result = s.Result != null ? new StepResult
                {
                    IsSuccess = s.Result.IsSuccess,
                    Message = s.Result.Message,
                    Data = s.Result.Data
                } : null,
                MaxRetries = s.MaxRetries,
                RetryDelay = s.RetryDelay,
                Timeout = s.Timeout,
                Priority = s.Priority,
                SourceData = s.SourceData?.ToSafeObject(),
                Metadata = s.Metadata?.ToSafe(_logger),
                ResourceGroup = s.ResourceGroup,
                IsCritical = s.IsCritical,
                IsIdempotent = s.IsIdempotent,
                CanRunInParallel = s.CanRunInParallel,
                EstimatedDuration = s.EstimatedDuration
            }).ToList();

            // Safely serialize Events using SafeObject
            var mongoSafeEvents = flow.Events?.Select(e => new FlowEvent
            {
                EventId = e.EventId,
                FlowId = e.FlowId,
                EventType = e.EventType,
                Description = e.Description,
                Timestamp = e.Timestamp,
                Data = e.Data  // Convert to SafeObject
            }).ToList();

            return new FlowDocument
            {
                FlowId = flow.FlowId,
                FlowType = flow.GetType().FullName,
                UserId = flow.UserId,
                CorrelationId = flow.CorrelationId,
                CreatedAt = flow.CreatedAt,
                StartedAt = flow.StartedAt,
                CompletedAt = flow.CompletedAt,
                Status = flow.Status,
                CurrentStepName = flow.CurrentStepName,
                CurrentStepIndex = flow.CurrentStepIndex,
                Steps = mongoSafeSteps,
                Data = flow.Data,  // SIMPLIFIED: Direct assignment since it's already SafeObject
                Events = mongoSafeEvents ?? new List<FlowEvent>(),
                LastError = flow.LastError,
                PausedAt = flow.PausedAt,
                PauseReason = flow.PauseReason,
                PauseMessage = flow.PauseMessage,
                PauseData = flow.PauseData,  // SIMPLIFIED: Direct assignment since it's already SafeObject
                SerializedFlow = serializedFlow,
                UpdatedAt = DateTime.UtcNow
            };
        }

        private async Task CreateIndexesAsync()
        {
            try
            {
                var indexKeys = Builders<FlowDocument>.IndexKeys;

                var indexes = new[]
                {
                    new CreateIndexModel<FlowDocument>(indexKeys.Ascending(x => x.FlowId)),
                    new CreateIndexModel<FlowDocument>(indexKeys.Ascending(x => x.Status)),
                    new CreateIndexModel<FlowDocument>(indexKeys.Ascending(x => x.UserId)),
                    new CreateIndexModel<FlowDocument>(indexKeys.Ascending(x => x.CorrelationId)),
                    new CreateIndexModel<FlowDocument>(indexKeys.Ascending(x => x.CreatedAt)),
                    new CreateIndexModel<FlowDocument>(indexKeys.Ascending(x => x.FlowType)),
                    new CreateIndexModel<FlowDocument>(
                        indexKeys.Ascending(x => x.Status).Ascending(x => x.CreatedAt))
                };

                await _flowsCollection.Indexes.CreateManyAsync(indexes);

                // Indexes for resume conditions
                var resumeIndexes = new[]
                {
                    new CreateIndexModel<ResumeConditionDocument>(
                        Builders<ResumeConditionDocument>.IndexKeys.Ascending(x => x.FlowId)),
                    new CreateIndexModel<ResumeConditionDocument>(
                        Builders<ResumeConditionDocument>.IndexKeys.Ascending(x => x.NextCheck))
                };

                await _resumeConditionsCollection.Indexes.CreateManyAsync(resumeIndexes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create MongoDB indexes");
            }
        }

        private void ConfigureMongoSerialization()
        {

            // Configure object serializer to use JSON strings for complex objects
            if (BsonSerializer.LookupSerializer(typeof(object)) == null)
            {
                BsonSerializer.RegisterSerializer(
                    typeof(object),
                    new ObjectSerializer(type =>
                        ObjectSerializer.DefaultAllowedTypes(type) ||
                        typeof(Dictionary<string, SafeObject>).IsAssignableFrom(type) ||
                        typeof(List<SafeObject>).IsAssignableFrom(type)
                    )
                );
            }

            // Register Guid serializer first - this is critical for handling Guids in dictionaries
            if (BsonSerializer.LookupSerializer(typeof(Guid)) == null)
            {
                BsonSerializer.RegisterSerializer(typeof(Guid), new GuidSerializer(GuidRepresentation.Standard));
            }

            // Register Guid? serializer for nullable Guids
            if (BsonSerializer.LookupSerializer(typeof(Guid?)) == null)
            {
                BsonSerializer.RegisterSerializer(typeof(Guid?), new NullableSerializer<Guid>(new GuidSerializer(GuidRepresentation.Standard)));
            }

            // Register serializer for Dictionary<string, SafeObject>
            if (BsonSerializer.LookupSerializer(typeof(Dictionary<string, SafeObject>)) == null)
            {
                BsonSerializer.RegisterSerializer(
                    typeof(Dictionary<string, SafeObject>),
                    new DictionaryInterfaceImplementerSerializer<Dictionary<string, SafeObject>>(DictionaryRepresentation.Document)
                );
            }

            // Register serializer for SafeObject itself if not already registered
            if (!BsonClassMap.IsClassMapRegistered(typeof(SafeObject)))
            {
                BsonClassMap.RegisterClassMap<SafeObject>(cm =>
                {
                    cm.AutoMap();
                    cm.SetIgnoreExtraElements(true);

                    // Use a custom ObjectSerializer for the Value property
                    cm.MapProperty(c => c.Value)
                      .SetSerializer(new ObjectSerializer(type =>
                          ObjectSerializer.DefaultAllowedTypes(type) ||
                          typeof(Dictionary<string, SafeObject>).IsAssignableFrom(type) ||
                          typeof(List<SafeObject>).IsAssignableFrom(type)
                      ))
                      .SetDefaultValue(new Dictionary<string, object>())
                      .SetIgnoreIfDefault(false);
                });
            }

            // Ensure that Exception types can be serialized/deserialized
            if (!BsonClassMap.IsClassMapRegistered(typeof(Exception)))
            {
                BsonClassMap.RegisterClassMap<Exception>(cm =>
                {
                    cm.AutoMap();
                    cm.SetIgnoreExtraElements(true);
                });
            }

            // Ensure that FlowContext types can be serialized/deserialized
            if (!BsonClassMap.IsClassMapRegistered(typeof(FlowContext)))
            {
                BsonClassMap.RegisterClassMap<FlowContext>(cm =>
                {
                    cm.AutoMap();
                    cm.SetIgnoreExtraElements(true);
                });
            }

            // Ensure that StepResult types can be serialized/deserialized
            if (!BsonClassMap.IsClassMapRegistered(typeof(StepResult)))
            {
                BsonClassMap.RegisterClassMap<StepResult>(cm =>
                {
                    cm.AutoMap();
                    cm.SetIgnoreExtraElements(true);
                    // Configure the Data dictionary to handle complex objects safely
                    cm.MapProperty(c => c.Data)
                      .SetDefaultValue(new Dictionary<string, SafeObject>())
                      .SetIgnoreIfDefault(false);
                });
            }

            // Register FlowEvent class map if needed
            if (!BsonClassMap.IsClassMapRegistered(typeof(FlowEvent)))
            {
                BsonClassMap.RegisterClassMap<FlowEvent>(cm =>
                {
                    cm.AutoMap();
                    cm.SetIgnoreExtraElements(true);
                    // Configure the Data dictionary to handle complex objects safely
                    cm.MapProperty(c => c.Data)
                      .SetDefaultValue(new Dictionary<string, SafeObject>())
                      .SetIgnoreIfDefault(false);
                });
            }

            // Register enum serialization for FlowStatus
            if (BsonSerializer.LookupSerializer(typeof(FlowStatus)) == null)
            {
                BsonSerializer.RegisterSerializer(typeof(FlowStatus), new EnumSerializer<FlowStatus>(BsonType.String));
            }

            // Register enum serialization for PauseReason
            if (BsonSerializer.LookupSerializer(typeof(PauseReason)) == null)
            {
                BsonSerializer.RegisterSerializer(typeof(PauseReason), new EnumSerializer<PauseReason>(BsonType.String));
            }

            // Register enum serialization for ResumeReason
            if (BsonSerializer.LookupSerializer(typeof(ResumeReason)) == null)
            {
                BsonSerializer.RegisterSerializer(typeof(ResumeReason), new EnumSerializer<ResumeReason>(BsonType.String));
            }
        }
    }

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
        public List<StepData> Steps { get; set; } = new List<StepData>();

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

    public class StepData
    {
        public string Name { get; set; }
        public StepStatus Status { get; set; }
        public List<string> StepDependencies { get; set; }
        public Dictionary<string, string> DataDependencies { get; set; }
        public List<FlowBranch> Branches { get; set; } = [];
        public StepResult? Result { get; set; }
        public int MaxRetries { get; set; }
        public TimeSpan RetryDelay { get; set; }
        public TimeSpan? Timeout { get; set; }
        public bool IsCritical { get; set; }
        public bool IsIdempotent { get; set; }
        public bool CanRunInParallel { get; set; }

        //Sub step related properties
        public int Priority { get; set; }
        public SafeObject SourceData { get; set; }  // CHANGED: Use SafeObject instead of object
        public int Index { get; set; } = -1;
        public Dictionary<string, SafeObject> Metadata { get; set; }  // CHANGED: Use SafeObject instead of object
        public TimeSpan? EstimatedDuration { get; set; }
        public string ResourceGroup { get; set; }

        public StepData() { }

        public StepData(FlowStep step)
        {
            Name = step.Name;
            StepDependencies = step.StepDependencies;
            DataDependencies = step.DataDependencies?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.FullName);
            Branches = step.Branches;
            Result = step.Result;
            MaxRetries = step.MaxRetries;
            RetryDelay = step.RetryDelay;
            Timeout = step.Timeout;
            IsCritical = step.IsCritical;
            IsIdempotent = step.IsIdempotent;
            CanRunInParallel = step.CanRunInParallel;
            Priority = step.Priority;
            SourceData = step.SourceData?.ToSafeObject();  // CHANGED: Use SafeObject
            Metadata = step.Metadata?.ToSafe();  // CHANGED: Use SafeObject
            ResourceGroup = step.ResourceGroup;
            EstimatedDuration = step.EstimatedDuration;
        }
    }

    internal class ResumeConditionDocument
    {
        [BsonId]
        public Guid Id { get; set; }

        [BsonElement("flowId")]
        public Guid FlowId { get; set; }

        [BsonElement("checkInterval")]
        [BsonTimeSpanOptions(BsonType.String)]
        public TimeSpan CheckInterval { get; set; }

        [BsonElement("nextCheck")]
        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime NextCheck { get; set; }

        [BsonElement("maxRetries")]
        public int MaxRetries { get; set; }

        [BsonElement("currentRetries")]
        public int CurrentRetries { get; set; }

        [BsonElement("createdAt")]
        [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
        public DateTime CreatedAt { get; set; }
    }
}