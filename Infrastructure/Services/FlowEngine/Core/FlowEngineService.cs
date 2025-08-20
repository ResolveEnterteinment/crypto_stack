using Infrastructure.Services.FlowEngine.BackgroundServices;
using Infrastructure.Services.FlowEngine.Configuration;
using Infrastructure.Services.FlowEngine.Events;
using Infrastructure.Services.FlowEngine.Exceptions;
using Infrastructure.Services.FlowEngine.Execution;
using Infrastructure.Services.FlowEngine.Models;
using Infrastructure.Services.FlowEngine.Persistence;
using Infrastructure.Services.FlowEngine.Security;
using Infrastructure.Services.FlowEngine.Utilities;
using Infrastructure.Services.FlowEngine.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Infrastructure.Services.FlowEngine.Core
{
    

    /// <summary>
    /// Production-ready Flow Engine Service Implementation
    /// </summary>
    public sealed class FlowEngineService : IFlowEngineService
    {
        private readonly IFlowExecutor _executor;
        private readonly IFlowPersistence _persistence;
        private readonly IFlowSecurity _security;
        private readonly IFlowValidation _validation;
        private readonly IFlowEventService _eventService;
        private readonly IFlowAuditService _auditService;
        private readonly ILogger<FlowEngineService> _logger;
        private readonly FlowEngineOptions _options;
        private readonly IBackgroundTaskQueue _backgroundTaskQueue;

        public FlowEngineService(
            IFlowExecutor executor,
            IFlowPersistence persistence,
            IFlowSecurity security,
            IFlowValidation validation,
            IFlowEventService eventService,
            IFlowAuditService auditService,
            ILogger<FlowEngineService> logger,
            IOptions<FlowEngineOptions> options,
            IBackgroundTaskQueue backgroundTaskQueue)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            _security = security ?? throw new ArgumentNullException(nameof(security));
            _validation = validation ?? throw new ArgumentNullException(nameof(validation));
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
            _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _backgroundTaskQueue = backgroundTaskQueue ?? throw new ArgumentNullException(nameof(backgroundTaskQueue));
        }

        public async Task<FlowResult<TFlow>> StartAsync<TFlow, TInit>(
            TInit initialData,
            string userId = null,
            string correlationId = null,
            CancellationToken cancellationToken = default)
            where TFlow : FlowDefinition, new()
            where TInit : class, IValidatable
        {
            ArgumentNullException.ThrowIfNull(initialData);

            var flowId = Guid.NewGuid().ToString();
            userId ??= "system";
            correlationId ??= Guid.NewGuid().ToString();

            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["FlowId"] = flowId,
                ["FlowType"] = typeof(TFlow).Name,
                ["UserId"] = userId,
                ["CorrelationId"] = correlationId
            });

            using var activity = FlowEngineActivity.StartActivity("flow.start");
            activity?.SetTag("flow.id", flowId);
            activity?.SetTag("flow.type", typeof(TFlow).Name);

            try
            {
                // 1. Validate initial data
                var validationResult = await _validation.ValidateAsync(initialData, cancellationToken).ConfigureAwait(false);
                if (!validationResult.IsValid)
                {
                    _logger.LogWarning("Flow validation failed: {Errors}", string.Join(", ", validationResult.Errors));
                    return FlowResult<TFlow>.ValidationFailure(validationResult.Errors);
                }

                // 2. Check security permissions
                var canStart = await _security.CanStartFlowAsync<TFlow>(userId, initialData, cancellationToken).ConfigureAwait(false);
                if (!canStart)
                {
                    _logger.LogWarning("User {UserId} not authorized to start flow {FlowType}", userId, typeof(TFlow).Name);
                    return FlowResult<TFlow>.Unauthorized("Not authorized to start this flow type");
                }

                // 3. Create flow instance
                var flow = new TFlow
                {
                    FlowId = flowId,
                    UserId = userId,
                    CorrelationId = correlationId,
                    CreatedAt = DateTime.UtcNow,
                    Status = FlowStatus.Initializing
                };

                flow.SetData(initialData);

                // 4. Record flow creation event
                var dataHash = await StreamingHashCalculator.ComputeStreamingHashAsync(initialData).ConfigureAwait(false);
                await _auditService.RecordEventAsync(new FlowEvent
                {
                    FlowId = flowId,
                    EventType = FlowEventType.Created,
                    UserId = userId,
                    Data = new { FlowType = typeof(TFlow).Name, InitialDataHash = dataHash },
                    Timestamp = DateTime.UtcNow
                }, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation("Starting flow {FlowType} with ID {FlowId}", typeof(TFlow).Name, flowId);

                // 5. Execute flow
                var result = await _executor.ExecuteAsync(flow, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation("Flow {FlowId} completed with status {Status}", flowId, result.Status);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start flow {FlowType} with ID {FlowId}", typeof(TFlow).Name, flowId);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

                await _auditService.RecordEventAsync(new FlowEvent
                {
                    FlowId = flowId,
                    EventType = FlowEventType.Failed,
                    UserId = userId,
                    Data = new { Error = ex.Message, ex.StackTrace },
                    Timestamp = DateTime.UtcNow
                }, CancellationToken.None).ConfigureAwait(false);

                throw;
            }
        }

        public async Task<FlowResult<TFlow>> ResumeAsync<TFlow>(
            string flowId,
            CancellationToken cancellationToken = default)
            where TFlow : FlowDefinition, new()
        {
            ArgumentException.ThrowIfNullOrEmpty(flowId);

            using var scope = _logger.BeginScope(new Dictionary<string, object> { ["FlowId"] = flowId });
            using var activity = FlowEngineActivity.StartActivity("flow.resume");
            activity?.SetTag("flow.id", flowId);

            try
            {
                var flow = await _persistence.LoadFlowAsync<TFlow>(flowId, cancellationToken).ConfigureAwait(false);
                if (flow == null)
                {
                    throw new FlowNotFoundException($"Flow {flowId} not found");
                }

                // FIXED: Add consistent audit logging
                await _auditService.RecordEventAsync(new FlowEvent
                {
                    FlowId = flowId,
                    EventType = FlowEventType.Resumed,
                    UserId = flow.UserId,
                    Data = new { ResumeReason = ResumeReason.System, ResumeType = "Direct", FromStep = flow.CurrentStepName },
                    Timestamp = DateTime.UtcNow
                }, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation("Resuming flow {FlowId} from step {CurrentStep}", flowId, flow.CurrentStepName);

                return await _executor.ExecuteAsync(flow, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resume flow {FlowId}", flowId);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

                // Record failure audit event
                await _auditService.RecordEventAsync(new FlowEvent
                {
                    FlowId = flowId,
                    EventType = FlowEventType.Failed,
                    UserId = "system",
                    Data = new { ResumeError = ex.Message, ResumeType = "Direct" },
                    Timestamp = DateTime.UtcNow
                }, CancellationToken.None).ConfigureAwait(false);

                throw;
            }
        }

        public async Task FireAsync<TFlow, TInit>(
            TInit initialData,
            string userId = null,
            CancellationToken cancellationToken = default)
            where TFlow : FlowDefinition, new()
            where TInit : class, IValidatable
        {
            // FIXED: Use proper background queue instead of Task.Run
            var fireTask = new FireAndForgetTask<TFlow, TInit>
            {
                InitialData = initialData,
                UserId = userId,
                CorrelationId = Guid.NewGuid().ToString(),
                QueuedAt = DateTime.UtcNow
            };

            await _backgroundTaskQueue.QueueBackgroundWorkItemAsync(async (serviceProvider, ct) =>
            {
                try
                {
                    using var scope = serviceProvider.CreateScope();
                    var flowEngine = scope.ServiceProvider.GetRequiredService<IFlowEngineService>();

                    _logger.LogInformation("Processing fire-and-forget flow {FlowType} queued at {QueuedAt}",
                        typeof(TFlow).Name, fireTask.QueuedAt);

                    var result = await flowEngine.StartAsync<TFlow, TInit>(
                        fireTask.InitialData,
                        fireTask.UserId,
                        fireTask.CorrelationId,
                        ct).ConfigureAwait(false);

                    if (result.IsSuccess)
                    {
                        _logger.LogInformation("Fire-and-forget flow {FlowType} completed successfully: {FlowId}",
                            typeof(TFlow).Name, result.Flow?.FlowId);
                    }
                    else
                    {
                        _logger.LogWarning("Fire-and-forget flow {FlowType} failed: {Message}",
                            typeof(TFlow).Name, result.Message);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fire-and-forget flow {FlowType} failed with exception", typeof(TFlow).Name);

                    // Record failure for monitoring
                    await RecordFireAndForgetFailureAsync(fireTask, ex, serviceProvider).ConfigureAwait(false);
                }
            }, cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("Fire-and-forget flow {FlowType} queued for background processing", typeof(TFlow).Name);
        }

        private async Task RecordFireAndForgetFailureAsync<TFlow, TInit>(
            FireAndForgetTask<TFlow, TInit> task,
            Exception exception,
            IServiceProvider serviceProvider)
            where TFlow : FlowDefinition, new()
            where TInit : class, IValidatable
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var auditService = scope.ServiceProvider.GetService<IFlowAuditService>();

                if (auditService != null)
                {
                    await auditService.RecordEventAsync(new FlowEvent
                    {
                        FlowId = "fire-and-forget-failed",
                        EventType = FlowEventType.Failed,
                        UserId = task.UserId ?? "system",
                        Data = new
                        {
                            FlowType = typeof(TFlow).Name,
                            task.QueuedAt,
                            Error = exception.Message,
                            exception.StackTrace
                        },
                        Timestamp = DateTime.UtcNow
                    }, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception auditEx)
            {
                _logger.LogError(auditEx, "Failed to record fire-and-forget failure audit event");
            }
        }

        public async Task<FlowResult<TTriggered>> TriggerAsync<TTriggered, TTriggerData>(
            FlowContext context,
            TTriggerData triggerData,
            CancellationToken cancellationToken = default)
            where TTriggered : FlowDefinition, new()
            where TTriggerData : class, IValidatable
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(triggerData);

            var correlationId = $"{context.Flow.CorrelationId}:triggered:{typeof(TTriggered).Name}";

            return await StartAsync<TTriggered, TTriggerData>(
                triggerData,
                context.Flow.UserId,
                correlationId,
                cancellationToken).ConfigureAwait(false);
        }

        public async Task<FlowStatus> GetStatusAsync(string flowId, string requestingUserId, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(flowId);
            ArgumentException.ThrowIfNullOrEmpty(requestingUserId);

            var canAccess = await _security.CanAccessFlowAsync(flowId, requestingUserId, cancellationToken).ConfigureAwait(false);
            if (!canAccess)
            {
                // FIXED: Add audit logging for access denied
                await _auditService.RecordEventAsync(new FlowEvent
                {
                    FlowId = flowId,
                    EventType = FlowEventType.Failed,
                    UserId = requestingUserId,
                    Data = new { Action = "GetStatus", Error = "Access denied" },
                    Timestamp = DateTime.UtcNow
                }, cancellationToken).ConfigureAwait(false);

                throw new UnauthorizedAccessException($"User {requestingUserId} cannot access flow {flowId}");
            }

            // FIXED: Add audit logging for status access
            await _auditService.RecordEventAsync(new FlowEvent
            {
                FlowId = flowId,
                EventType = FlowEventType.StepCompleted, // Using generic event type
                UserId = requestingUserId,
                Data = new { Action = "GetStatus", AccessGranted = true },
                Timestamp = DateTime.UtcNow
            }, cancellationToken).ConfigureAwait(false);

            return await _persistence.GetFlowStatusAsync(flowId, cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> CancelAsync(string flowId, string userId, string reason = null, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(flowId);
            ArgumentException.ThrowIfNullOrEmpty(userId);

            var canCancel = await _security.CanCancelFlowAsync(flowId, userId, cancellationToken).ConfigureAwait(false);
            if (!canCancel)
            {
                throw new UnauthorizedAccessException($"User {userId} cannot cancel flow {flowId}");
            }

            await _auditService.RecordEventAsync(new FlowEvent
            {
                FlowId = flowId,
                EventType = FlowEventType.Cancelled,
                UserId = userId,
                Data = new { Reason = reason },
                Timestamp = DateTime.UtcNow
            }, cancellationToken).ConfigureAwait(false);

            return await _persistence.CancelFlowAsync(flowId, reason, cancellationToken).ConfigureAwait(false);
        }

        public async Task<FlowTimeline> GetTimelineAsync(string flowId, string requestingUserId, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(flowId);
            ArgumentException.ThrowIfNullOrEmpty(requestingUserId);

            var canAccess = await _security.CanAccessFlowAsync(flowId, requestingUserId, cancellationToken).ConfigureAwait(false);
            if (!canAccess)
            {
                throw new UnauthorizedAccessException($"User {requestingUserId} cannot access flow {flowId}");
            }

            return await _persistence.GetFlowTimelineAsync(flowId, cancellationToken).ConfigureAwait(false);
        }

        public async Task<PagedResult<FlowSummary>> QueryAsync(FlowQuery query, string requestingUserId, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(query);
            ArgumentException.ThrowIfNullOrEmpty(requestingUserId);

            // Apply security filtering
            var secureQuery = await _security.FilterQueryAsync(query, requestingUserId, cancellationToken).ConfigureAwait(false);

            return await _persistence.QueryFlowsAsync(secureQuery, cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> ResumeManuallyAsync(string flowId, string userId, string reason = null, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(flowId);
            ArgumentException.ThrowIfNullOrEmpty(userId);

            // SECURITY ENFORCEMENT: Check permissions before resume
            var canResume = await _security.CanResumeFlowAsync(flowId, userId, cancellationToken).ConfigureAwait(false);
            if (!canResume)
            {
                throw new UnauthorizedAccessException($"User {userId} cannot resume flow {flowId}");
            }

            // Load flow for additional security checks
            var flow = await _persistence.LoadFlowAsync<FlowDefinition>(flowId, cancellationToken).ConfigureAwait(false);
            if (flow == null)
            {
                throw new FlowNotFoundException($"Flow {flowId} not found");
            }

            // BUSINESS RULE ENFORCEMENT: Check if manual resume is allowed for current step
            if (!await _security.CanResumeFromStepAsync(flow, flow.CurrentStepName, userId, cancellationToken).ConfigureAwait(false))
            {
                throw new UnauthorizedAccessException($"Manual resume not allowed for step {flow.CurrentStepName}");
            }

            _logger.LogInformation("Manual resume requested for flow {FlowId} by user {UserId}", flowId, userId);

            await _auditService.RecordEventAsync(new FlowEvent
            {
                FlowId = flowId,
                EventType = FlowEventType.Resumed,
                UserId = userId,
                Data = new { ResumeReason = ResumeReason.Manual, Reason = reason, StepName = flow.CurrentStepName },
                Timestamp = DateTime.UtcNow
            }, cancellationToken).ConfigureAwait(false);

            return await _persistence.ResumeFlowAsync(flowId, ResumeReason.Manual, userId, reason, cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> ResumeByEventAsync(string flowId, SignedEvent signedEvent, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(flowId);
            ArgumentNullException.ThrowIfNull(signedEvent);

            // SECURITY ENFORCEMENT: Validate event signature first
            var isValidSignature = await _security.ValidateEventSignatureAsync(signedEvent, cancellationToken).ConfigureAwait(false);
            if (!isValidSignature)
            {
                _logger.LogWarning("Invalid event signature for flow resume: {FlowId}, {EventType}", flowId, signedEvent.EventType);
                return false;
            }

            // Load flow for security and business rule checks
            var flow = await _persistence.LoadFlowAsync<FlowDefinition>(flowId, cancellationToken).ConfigureAwait(false);
            if (flow == null)
            {
                _logger.LogWarning("Flow {FlowId} not found for event resume", flowId);
                return false;
            }

            // SECURITY ENFORCEMENT: Check if this event can resume this flow/step
            var canResumeWithEvent = await _security.CanResumeWithEventAsync(flow, signedEvent, cancellationToken).ConfigureAwait(false);
            if (!canResumeWithEvent)
            {
                _logger.LogWarning("Event {EventType} cannot resume flow {FlowId} at step {StepName}",
                    signedEvent.EventType, flowId, flow.CurrentStepName);
                return false;
            }

            // BUSINESS RULE ENFORCEMENT: Validate event payload matches expected step requirements
            var isValidForStep = await _security.ValidateEventPayloadForStepAsync(flow, flow.CurrentStepName, signedEvent, cancellationToken).ConfigureAwait(false);
            if (!isValidForStep)
            {
                _logger.LogWarning("Event payload validation failed for flow {FlowId} step {StepName}", flowId, flow.CurrentStepName);
                return false;
            }

            _logger.LogInformation("Event-based resume for flow {FlowId} triggered by {EventType}", flowId, signedEvent.EventType);

            await _auditService.RecordEventAsync(new FlowEvent
            {
                FlowId = flowId,
                EventType = FlowEventType.Resumed,
                UserId = signedEvent.PublishedBy,
                Data = new
                {
                    ResumeReason = ResumeReason.Event,
                    signedEvent.EventType,
                    signedEvent.EventId,
                    StepName = flow.CurrentStepName,
                    EventSignature = signedEvent.Signature
                },
                Timestamp = DateTime.UtcNow
            }, cancellationToken).ConfigureAwait(false);

            return await _persistence.ResumeFlowAsync(flowId, ResumeReason.Event, signedEvent.PublishedBy, $"Event: {signedEvent.EventType}", cancellationToken).ConfigureAwait(false);
        }

        public async Task PublishEventAsync(string eventType, object eventData, string publishedBy, string correlationId = null, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(eventType);
            ArgumentNullException.ThrowIfNull(eventData);
            ArgumentException.ThrowIfNullOrEmpty(publishedBy);

            var signedEvent = await _security.SignEventAsync(eventType, eventData, publishedBy, correlationId, cancellationToken).ConfigureAwait(false);

            await _eventService.PublishAsync(signedEvent, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Event {EventType} published by {PublishedBy} with correlation {CorrelationId}",
                eventType, publishedBy, correlationId);
        }

        public async Task<PagedResult<FlowSummary>> GetPausedFlowsAsync(FlowQuery query, string requestingUserId, CancellationToken cancellationToken = default)
        {
            query ??= new FlowQuery();
            var pausedQuery = query with { Status = FlowStatus.Paused };

            return await QueryAsync(pausedQuery, requestingUserId, cancellationToken).ConfigureAwait(false);
        }
    }
}
