using Infrastructure.Services.FlowEngine.Configuration.Options;
using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Exceptions;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;
using Infrastructure.Utilities;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson.Serialization.Conventions;
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
                CurrentStepIndex = f.Steps.IndexOf(f.Steps.First(s => s.Name == f.CurrentStepName)) + 1,
                TotalSteps = f.Steps.Count,
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
                    EventId = Guid.NewGuid(),
                    FlowId = flowId,
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
                    EventId = Guid.NewGuid(),
                    FlowId = flowId,
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
            // Enhanced step serialization with safe handling of all complex data
            var mongoSafeSteps = flow.Steps?.Select(s => new StepData(s)).ToList();

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
                Events = flow.Events,
                LastError = flow.LastError,
                PausedAt = flow.PausedAt,
                PauseReason = flow.PauseReason,
                PauseMessage = flow.PauseMessage,
                PauseData = flow.PauseData,  // SIMPLIFIED: Direct assignment since it's already SafeObject
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
            // Register SafeObject class map
            if (!BsonClassMap.IsClassMapRegistered(typeof(SafeObject)))
            {
                BsonClassMap.RegisterClassMap<SafeObject>(cm =>
                {
                    cm.AutoMap();
                    cm.SetIgnoreExtraElements(true);
                });
            }

            // Register ObjectSerializer with ALL the types SafeObject might use
            // This should be sufficient to handle List<SafeObject>
            try
            {
                BsonSerializer.RegisterSerializer(
                    typeof(object),
                    new ObjectSerializer(
                        type =>
                            type == typeof(SafeObject) ||
                            type == typeof(List<SafeObject>) ||  // This allows List<SafeObject>
                            type == typeof(Dictionary<string, SafeObject>) ||
                            ObjectSerializer.DefaultAllowedTypes(type)
                    )
                );
            }
            catch (Exception)
            {
                // Already registered
            }

            // Register Dictionary<string, SafeObject> serializer
            try
            {
                BsonSerializer.RegisterSerializer(
                    typeof(Dictionary<string, SafeObject>),
                    new DictionaryInterfaceImplementerSerializer<Dictionary<string, SafeObject>>(
                        DictionaryRepresentation.Document)
                );
            }
            catch (BsonSerializationException)
            {
                // Already registered
            }

            // Register Exception class map
            if (!BsonClassMap.IsClassMapRegistered(typeof(Exception)))
            {
                BsonClassMap.RegisterClassMap<Exception>(cm =>
                {
                    cm.AutoMap();
                    cm.SetIgnoreExtraElements(true);
                    cm.UnmapProperty(e => e.TargetSite);
                    cm.UnmapProperty(e => e.Data);
                });
            }

            RegisterExceptionType<InvalidOperationException>();
            RegisterExceptionType<ArgumentNullException>();
            RegisterExceptionType<ArgumentException>();
            RegisterExceptionType<NullReferenceException>();
            RegisterExceptionType<FlowExecutionException>();
            RegisterExceptionType<FlowNotFoundException>();

            // Register FlowContext class map
            if (!BsonClassMap.IsClassMapRegistered(typeof(FlowContext)))
            {
                BsonClassMap.RegisterClassMap<FlowContext>(cm =>
                {
                    cm.AutoMap();
                    cm.SetIgnoreExtraElements(true);
                });
            }

            // Register StepResult class map
            if (!BsonClassMap.IsClassMapRegistered(typeof(StepResult)))
            {
                BsonClassMap.RegisterClassMap<StepResult>(cm =>
                {
                    cm.AutoMap();
                    cm.SetIgnoreExtraElements(true);
                    cm.MapProperty(c => c.Data)
                      .SetDefaultValue(new Dictionary<string, SafeObject>())
                      .SetIgnoreIfDefault(false);
                });
            }

            // Register FlowEvent class map
            if (!BsonClassMap.IsClassMapRegistered(typeof(FlowEvent)))
            {
                BsonClassMap.RegisterClassMap<FlowEvent>(cm =>
                {
                    cm.AutoMap();
                    cm.SetIgnoreExtraElements(true);
                    cm.MapProperty(c => c.Data)
                      .SetDefaultValue(new Dictionary<string, SafeObject>())
                      .SetIgnoreIfDefault(false);
                });
            }

            // Register enum serializers
            try
            {
                BsonSerializer.RegisterSerializer(typeof(FlowStatus), new EnumSerializer<FlowStatus>(BsonType.String));
                BsonSerializer.RegisterSerializer(typeof(PauseReason), new EnumSerializer<PauseReason>(BsonType.String));
                BsonSerializer.RegisterSerializer(typeof(ResumeReason), new EnumSerializer<ResumeReason>(BsonType.String));
            }
            catch (BsonSerializationException)
            {
                // Already registered
            }

            _logger.LogInformation("MongoDB serialization configured with SafeObject support");
        }

        private void RegisterExceptionType<TException>() where TException : Exception
        {
            if (!BsonClassMap.IsClassMapRegistered(typeof(TException)))
            {
                BsonClassMap.RegisterClassMap<TException>(cm =>
                {
                    cm.AutoMap();
                    cm.SetIgnoreExtraElements(true);

                    if (typeof(TException).GetProperty(nameof(Exception.TargetSite))?.DeclaringType == typeof(TException))
                    {
                        cm.UnmapProperty(e => e.TargetSite);
                    }

                    if (typeof(TException).GetProperty(nameof(Exception.Data))?.DeclaringType == typeof(TException))
                    {
                        cm.UnmapProperty(e => e.Data);
                    }
                });
            }
        }
    }
}