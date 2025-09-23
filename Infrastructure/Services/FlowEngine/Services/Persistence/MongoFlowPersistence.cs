using Domain.DTOs.Subscription;
using Domain.Exceptions;
using Infrastructure.Services.FlowEngine.Configuration.Options;
using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Exceptions;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;
using Infrastructure.Utilities;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Options;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using StringSerializer = MongoDB.Bson.Serialization.Serializers.StringSerializer; // Resolve ambiguity at top

namespace Infrastructure.Services.FlowEngine.Services.Persistence
{
    public class MongoFlowPersistence : IFlowPersistence
    {
        private readonly IMongoDatabase _database;
        private readonly IMongoCollection<FlowState> _flowsCollection;
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
            _flowsCollection = _database.GetCollection<FlowState>("flows");
            _resumeConditionsCollection = _database.GetCollection<ResumeConditionDocument>("resumeConditions");

            // Configure MongoDB serialization for polymorphic types
            ConfigureMongoSerialization();

            // Create indexes for better performance
            CreateIndexesAsync().ConfigureAwait(false);
        }

        public async Task<FlowState> GetByFlowId(Guid flowId)
        {
            var filter = Builders<FlowState>.Filter.Eq(x => x.FlowId, flowId);
            return await _flowsCollection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<FlowTimeline> GetFlowTimelineAsync(Guid flowId)
        {
            var filter = Builders<FlowState>.Filter.Eq(x => x.FlowId, flowId);
            var projection = Builders<FlowState>.Projection
                .Include(x => x.FlowId)
                .Include(x => x.Events);

            var flowDoc = await _flowsCollection.Find(filter)
                .Project<FlowState>(projection)
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
            var filterBuilder = Builders<FlowState>.Filter;
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
            var flowStates = await _flowsCollection.Find(filter)
                .Skip(skip)
                .Limit(query.PageSize)
                .SortBy(x => x.CreatedAt)
                .ToListAsync();

            var summaries = flowStates.Select(state => new FlowSummary
            {
                FlowId = state.FlowId,
                FlowType = Type.GetType(state.FlowType).Name,
                Status = state.Status,
                UserId = state.UserId,
                CorrelationId = state.CorrelationId,
                CreatedAt = state.CreatedAt,
                StartedAt = state.StartedAt,
                CompletedAt = state.CompletedAt,
                LastUpdatedAt = state.LastUpdatedAt,
                CurrentStepName = state.CurrentStepName,
                CurrentStepIndex = state.CurrentStepIndex,
                TotalSteps = state.Steps.Count,
                PauseReason = state.PauseReason,
                ErrorMessage = state.LastError?.Message
            }).ToList();

            return new PagedResult<FlowSummary>
            {
                Items = summaries,
                TotalCount = (int)totalCount,
                PageSize = query.PageSize,
                PageNumber = query.PageNumber
            };
        }

        public async Task<List<FlowState>> GetFlowsByStatusesAsync(FlowStatus[] flowStatuses)
        {
            var filter = Builders<FlowState>.Filter.In(x => x.Status, flowStatuses);
            var flowDocs = await _flowsCollection.Find(filter).ToListAsync();

            return flowDocs;
        }

        public async Task SaveFlowStateAsync(FlowState state)
        {
            try
            {
                var filter = Builders<FlowState>.Filter.Eq(x => x.FlowId, state.FlowId);

                // Check if document already exists to preserve the _id
                var existingState = await _flowsCollection.Find(filter)
                    .FirstOrDefaultAsync();

                state.LastUpdatedAt = DateTime.UtcNow;
                state.Id = existingState != null ? existingState.Id : Guid.NewGuid();

                await _flowsCollection.ReplaceOneAsync(filter, state, new ReplaceOptions { IsUpsert = true });

                _logger.LogDebug("Saved flow state for {FlowId} with status {Status}", state.FlowId, state.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save flow state for {FlowId}", state.FlowId);
                throw;
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

        public async Task<int> CleanupCompletedFlowsAsync(TimeSpan olderThan)
        {
            var cutoffDate = DateTime.UtcNow - olderThan;
            var filter = Builders<FlowState>.Filter.And(
                Builders<FlowState>.Filter.In(x => x.Status, [FlowStatus.Completed, FlowStatus.Failed, FlowStatus.Cancelled]),
                Builders<FlowState>.Filter.Lt(x => x.CompletedAt, cutoffDate)
            );

            var result = await _flowsCollection.DeleteManyAsync(filter);
            _logger.LogInformation("Cleaned up {Count} completed flows older than {CutoffDate}", result.DeletedCount, cutoffDate);

            return (int)result.DeletedCount;
        }

        private async Task CreateIndexesAsync()
        {
            try
            {
                var indexKeys = Builders<FlowState>.IndexKeys;

                var indexes = new[]
                {
                    new CreateIndexModel<FlowState>(indexKeys.Ascending(x => x.FlowId)),
                    new CreateIndexModel<FlowState>(indexKeys.Ascending(x => x.Status)),
                    new CreateIndexModel<FlowState>(indexKeys.Ascending(x => x.UserId)),
                    new CreateIndexModel<FlowState>(indexKeys.Ascending(x => x.CorrelationId)),
                    new CreateIndexModel<FlowState>(indexKeys.Ascending(x => x.CreatedAt)),
                    new CreateIndexModel<FlowState>(indexKeys.Ascending(x => x.FlowType)),
                    new CreateIndexModel<FlowState>(
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
            // Register optimized SafeObject class map with custom serializer
            if (!BsonClassMap.IsClassMapRegistered(typeof(SafeObject)))
            {
                BsonClassMap.RegisterClassMap<SafeObject>(cm =>
                {
                    cm.AutoMap();
                    cm.SetIgnoreExtraElements(true);
                    // Map to shorter field names for reduced storage
                    cm.MapProperty(x => x.TypeCode).SetElementName("t");
                    cm.MapProperty(x => x.Value).SetElementName("v");
                    cm.MapProperty(x => x.TypeHint).SetElementName("h").SetIgnoreIfNull(true);
                });
            }

            // Register custom SafeObject serializer
            try
            {
                BsonSerializer.RegisterSerializer(typeof(SafeObject), new SafeObjectBsonSerializer());
            }
            catch (BsonSerializationException)
            {
                // Already registered
            }

            // Register TypeDiscriminator enum serializer
            try
            {
                BsonSerializer.RegisterSerializer(typeof(TypeDiscriminator), new EnumSerializer<TypeDiscriminator>(BsonType.Int32));
            }
            catch (BsonSerializationException)
            {
                // Already registered
            }

            // Register high-performance object serializer
            try
            {
                BsonSerializer.RegisterSerializer(
                    typeof(object),
                    new HighPerformanceObjectSerializer()
                );
            }
            catch (Exception)
            {
                // Already registered
            }

            // Register Dictionary<string, SafeObject> serializer with custom SafeObject handling
            try
            {
                BsonSerializer.RegisterSerializer(
                    typeof(Dictionary<string, SafeObject>),
                    new DictionaryInterfaceImplementerSerializer<Dictionary<string, SafeObject>>(
                        DictionaryRepresentation.Document,
                        new StringSerializer(), // Now resolves to MongoDB's StringSerializer
                        new SafeObjectBsonSerializer())
                );
            }
            catch (BsonSerializationException)
            {
                // Already registered
            }

            // FIX: Register custom serializer for Dictionary<string, Type> to handle DataDependencies
            try
            {
                BsonSerializer.RegisterSerializer(
                    typeof(Dictionary<string, Type>),
                    new DictionaryInterfaceImplementerSerializer<Dictionary<string, Type>>(
                        DictionaryRepresentation.Document,
                        new StringSerializer(), // Now resolves to MongoDB's StringSerializer
                        new TypeStringSerializer()) // Use custom type serializer
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
            RegisterExceptionType<HttpRequestException>();
            RegisterExceptionType<PaymentApiException>();

            // Register FlowContext class map
            if (!BsonClassMap.IsClassMapRegistered(typeof(FlowExecutionContext)))
            {
                BsonClassMap.RegisterClassMap<FlowExecutionContext>(cm =>
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
                      .SetIgnoreIfDefault(true);
                });
            }

            // Register TriggeredFlowData class map
            if (!BsonClassMap.IsClassMapRegistered(typeof(TriggeredFlowData)))
            {
                BsonClassMap.RegisterClassMap<TriggeredFlowData>(cm =>
                {
                    cm.AutoMap();
                    cm.SetIgnoreExtraElements(true);
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

            // Register EnhancedAllocationDto class map
            if (!BsonClassMap.IsClassMapRegistered(typeof(EnhancedAllocationDto)))
            {
                BsonClassMap.RegisterClassMap<EnhancedAllocationDto>(cm =>
                {
                    cm.AutoMap();
                    cm.SetIgnoreExtraElements(true);
                });
            }

            // Register FlowStep class map with proper DataDependencies handling
            if (!BsonClassMap.IsClassMapRegistered(typeof(FlowStep)))
            {
                BsonClassMap.RegisterClassMap<FlowStep>(cm =>
                {
                    cm.AutoMap();
                    cm.SetIgnoreExtraElements(true);

                    // Map DataDependencies with custom handling
                    cm.MapProperty(c => c.DataDependencies)
                      .SetDefaultValue(new Dictionary<string, Type>())
                      .SetIgnoreIfDefault(false);
                });
            }

            // Register FlowSubStep class map (inherits from FlowStep)
            if (!BsonClassMap.IsClassMapRegistered(typeof(FlowSubStep)))
            {
                BsonClassMap.RegisterClassMap<FlowSubStep>(cm =>
                {
                    cm.AutoMap();
                    cm.SetIgnoreExtraElements(true);
                });
            }

            // Register FlowBranch class map
            if (!BsonClassMap.IsClassMapRegistered(typeof(FlowBranch)))
            {
                BsonClassMap.RegisterClassMap<FlowBranch>(cm =>
                {
                    cm.AutoMap();
                    cm.SetIgnoreExtraElements(true);
                });
            }

            // Register StepState class map
            if (!BsonClassMap.IsClassMapRegistered(typeof(StepState)))
            {
                BsonClassMap.RegisterClassMap<StepState>(cm =>
                {
                    cm.AutoMap();
                    cm.SetIgnoreExtraElements(true);
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

    /// <summary>
    /// Custom BSON serializer for SafeObject that integrates with MongoDB's native BSON system
    /// </summary>
    public class SafeObjectBsonSerializer : SerializerBase<SafeObject>
    {
        public override SafeObject Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            var currentBsonType = context.Reader.GetCurrentBsonType();

            if (currentBsonType == BsonType.Null)
            {
                context.Reader.ReadNull();
                return null;
            }

            if (currentBsonType != BsonType.Document)
            {
                throw new BsonSerializationException($"Expected Document but got {currentBsonType}");
            }

            context.Reader.ReadStartDocument();

            // FIXED: Direct instantiation - no pooling issues
            var safeObject = new SafeObject();

            while (context.Reader.ReadBsonType() != BsonType.EndOfDocument)
            {
                var elementName = context.Reader.ReadName();

                switch (elementName)
                {
                    case "t":
                        safeObject.TypeCode = (TypeDiscriminator)context.Reader.ReadInt32();
                        break;
                    case "v":
                        safeObject.Value = BsonValueSerializer.Instance.Deserialize(context, args);
                        break;
                    case "h":
                        safeObject.TypeHint = context.Reader.ReadString();
                        break;
                    default:
                        context.Reader.SkipValue();
                        break;
                }
            }

            context.Reader.ReadEndDocument();
            return safeObject;
        }

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, SafeObject value)
        {
            if (value == null)
            {
                context.Writer.WriteNull();
                return;
            }

            context.Writer.WriteStartDocument();

            context.Writer.WriteName("t");
            context.Writer.WriteInt32((int)value.TypeCode);

            context.Writer.WriteName("v");
            BsonValueSerializer.Instance.Serialize(context, args, BsonValue.Create(value.Value));

            if (!string.IsNullOrEmpty(value.TypeHint))
            {
                context.Writer.WriteName("h");
                context.Writer.WriteString(value.TypeHint);
            }

            context.Writer.WriteEndDocument();
        }
    }

    /// <summary>
    /// Custom BSON serializer for Type objects that handles both string and document representations
    /// </summary>
    public class TypeStringSerializer : SerializerBase<Type>
    {
        public override Type Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            var currentBsonType = context.Reader.GetCurrentBsonType();

            switch (currentBsonType)
            {
                case BsonType.String:
                    // Handle string representation (normal case)
                    var typeString = context.Reader.ReadString();
                    if (string.IsNullOrEmpty(typeString))
                        return null;

                    try
                    {
                        return Type.GetType(typeString);
                    }
                    catch
                    {
                        return null;
                    }

                case BsonType.Document:
                    // Handle document representation (complex serialization case)
                    var document = BsonDocumentSerializer.Instance.Deserialize(context, args);

                    // Try to extract type information from various possible fields
                    if (document.Contains("_t"))
                    {
                        // MongoDB polymorphic type discriminator
                        var typeName = document["_t"].AsString;
                        try
                        {
                            return Type.GetType(typeName);
                        }
                        catch
                        {
                            return null;
                        }
                    }
                    else if (document.Contains("AssemblyQualifiedName"))
                    {
                        var typeName = document["AssemblyQualifiedName"].AsString;
                        try
                        {
                            return Type.GetType(typeName);
                        }
                        catch
                        {
                            return null;
                        }
                    }
                    else if (document.Contains("FullName"))
                    {
                        var typeName = document["FullName"].AsString;
                        try
                        {
                            return Type.GetType(typeName);
                        }
                        catch
                        {
                            return null;
                        }
                    }

                    // Fallback: return null for unrecognized document structures
                    return null;

                case BsonType.Null:
                    context.Reader.ReadNull();
                    return null;

                default:
                    // Skip unknown BSON types
                    context.Reader.SkipValue();
                    return null;
            }
        }

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, Type value)
        {
            if (value == null)
            {
                context.Writer.WriteNull();
            }
            else
            {
                // Store the AssemblyQualifiedName for full type resolution
                context.Writer.WriteString(value.AssemblyQualifiedName ?? value.FullName ?? value.Name);
            }
        }
    }

    /// <summary>
    /// High-performance object serializer that bypasses SafeObject for simple types
    /// </summary>
    public class HighPerformanceObjectSerializer : SerializerBase<object>
    {
        public override object Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
        {
            var currentBsonType = context.Reader.GetCurrentBsonType();

            // Fast path for simple BSON types - no SafeObject overhead
            return currentBsonType switch
            {
                BsonType.String => context.Reader.ReadString(),
                BsonType.Int32 => context.Reader.ReadInt32(),
                BsonType.Int64 => context.Reader.ReadInt64(),
                BsonType.Double => context.Reader.ReadDouble(),
                BsonType.Boolean => context.Reader.ReadBoolean(),
                BsonType.DateTime => context.Reader.ReadDateTime(),
                BsonType.Null => ReadNull(context),
                BsonType.Document => BsonDocumentSerializer.Instance.Deserialize(context, args),
                BsonType.Array => BsonArraySerializer.Instance.Deserialize(context, args),
                _ => BsonValueSerializer.Instance.Deserialize(context, args)
            };
        }

        public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, object value)
        {
            if (value == null)
            {
                context.Writer.WriteNull();
                return;
            }

            // Fast path for simple types - no SafeObject overhead
            switch (value)
            {
                case string s:
                    context.Writer.WriteString(s);
                    break;
                case int i:
                    context.Writer.WriteInt32(i);
                    break;
                case long l:
                    context.Writer.WriteInt64(l);
                    break;
                case double d:
                    context.Writer.WriteDouble(d);
                    break;
                case bool b:
                    context.Writer.WriteBoolean(b);
                    break;
                case DateTime dt:
                    context.Writer.WriteDateTime(BsonUtils.ToMillisecondsSinceEpoch(dt));
                    break;
                default:
                    // Use default serialization for complex types
                    BsonSerializer.Serialize(context.Writer, value.GetType(), value);
                    break;
            }
        }

        private static object ReadNull(BsonDeserializationContext context)
        {
            context.Reader.ReadNull();
            return null;
        }
    }
}