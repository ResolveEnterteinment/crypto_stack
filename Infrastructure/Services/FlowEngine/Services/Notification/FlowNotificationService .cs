using Domain.DTOs.Flow;
using Infrastructure.Hubs;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Engine;
using Infrastructure.Utilities;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.FlowEngine.Services.Notification
{
    public class FlowNotificationService : IFlowNotificationService
    {
        private readonly IHubContext<FlowHub> _hubContext;
        private readonly ILogger<FlowNotificationService> _logger;

        public FlowNotificationService(
            IHubContext<FlowHub> hubContext,
            ILogger<FlowNotificationService> logger)
        {
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task NotifyFlowStatusChanged(Flow flow)
        {
            try
            {
                // Add null check for flow parameter
                if (flow == null)
                {
                    _logger.LogWarning("Cannot notify flow status changed: flow is null");
                    return;
                }

                var flowDetailDto = MapToFlowDetailDto(flow);

                // Send to specific flow group
                await _hubContext.Clients.Group($"flow-{flow.State.FlowId}")
                    .SendAsync("FlowStatusChanged", flowDetailDto);

                // Also send to admin group for dashboard updates
                await _hubContext.Clients.Group("flow-admins")
                    .SendAsync("FlowStatusChanged", flowDetailDto);

                _logger.LogDebug("Sent flow status update for flow {FlowId} with status {Status}",
                    flow.State.FlowId, flow.State.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending flow status notification for flow {FlowId}", flow?.State?.FlowId);
            }
        }

        public async Task NotifyStepStatusChanged(Flow flow, FlowStep step)
        {
            try
            {
                // Add null checks
                if (flow == null)
                {
                    _logger.LogWarning("Cannot notify step status changed: flow is null");
                    return;
                }

                if (step == null)
                {
                    _logger.LogWarning("Cannot notify step status changed: step is null");
                    return;
                }

                // Send BOTH step-specific update AND full flow update
                var stepUpdateDto = new StepStatusUpdateDto
                {
                    FlowId = flow.State.FlowId,
                    StepName = step.Name,
                    StepStatus = step.Status.ToString(),
                    StepResult = step.Result != null ? new StepResultDto
                    {
                        IsSuccess = step.Result.IsSuccess,
                        Message = step.Result.Message,
                        Data = SafelyExtractSingleData(step.Result.Data), // Fixed: Handle single SafeObject
                    } : null,
                    CurrentStepIndex = flow.State.CurrentStepIndex + 1,
                    CurrentStepName = flow.State.CurrentStepName,
                    FlowStatus = flow.State.Status.ToString(),
                    Timestamp = DateTime.UtcNow
                };

                // Send step-specific update
                await _hubContext.Clients.Group($"flow-{flow.State.FlowId}")
                    .SendAsync("StepStatusChanged", stepUpdateDto);

                await _hubContext.Clients.Group("flow-admins")
                    .SendAsync("StepStatusChanged", stepUpdateDto);

                // Also send full flow update to ensure complete state sync
                var flowDetailDto = MapToFlowDetailDto(flow);

                await _hubContext.Clients.Group($"flow-{flow.State.FlowId}")
                    .SendAsync("FlowStatusChanged", flowDetailDto);

                await _hubContext.Clients.Group("flow-admins")
                    .SendAsync("FlowStatusChanged", flowDetailDto);

                _logger.LogDebug("Sent step status update for flow {FlowId}, step {StepName} with status {StepStatus}",
                    flow.State.FlowId, step.Name, step.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending step status notification for flow {FlowId}, step {StepName}",
                    flow?.State?.FlowId, step?.Name);
            }
        }

        public async Task NotifyFlowError(Guid flowId, string error)
        {
            try
            {
                await _hubContext.Clients.Group($"flow-{flowId}")
                    .SendAsync("FlowError", flowId.ToString(), error);

                await _hubContext.Clients.Group("flow-admins")
                    .SendAsync("FlowError", flowId.ToString(), error);

                _logger.LogWarning("Sent flow error notification for flow {FlowId}: {Error}", flowId, error);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending flow error notification for flow {FlowId}", flowId);
            }
        }

        private FlowDetailDto MapToFlowDetailDto(Flow flow)
        {
            // Add comprehensive null checks
            if (flow == null)
            {
                _logger.LogWarning("Cannot map flow to DTO: flow is null");
                return new FlowDetailDto();
            }

            try
            {
                // Map FlowDefinition to FlowDetailDto
                return new FlowDetailDto
                {
                    FlowId = flow.State.FlowId,
                    FlowType = Type.GetType(flow.State.FlowType).Name,
                    Status = flow.State.Status.ToString(),
                    UserId = flow.State.UserId,
                    CorrelationId = flow.State.CorrelationId,
                    CreatedAt = flow.State.CreatedAt,
                    StartedAt = flow.State.StartedAt,
                    CompletedAt = flow.State.CompletedAt,
                    PausedAt = flow.State.PausedAt,
                    CurrentStepName = flow.State.CurrentStepName,
                    CurrentStepIndex = flow.State.CurrentStepIndex + 1,
                    PauseReason = flow.State.PauseReason?.ToString(),
                    PauseMessage = flow.State.PauseMessage,
                    LastError = flow.State.LastError?.Message,
                    Steps = flow.Definition.Steps?.Select(s => new StepDto
                    {
                        Name = s?.Name ?? "Unknown Step",
                        Status = s?.Status.ToString() ?? "Unknown",
                        StepDependencies = s?.StepDependencies ?? [],
                        DataDependencies = s?.DataDependencies?.ToDictionary(kv => kv.Key, kv => kv.Value?.Name ?? "Unknown") ?? [],
                        MaxRetries = s?.MaxRetries ?? 0,
                        RetryDelay = s?.RetryDelay.ToString() ?? "00:00:00",
                        Timeout = s?.Timeout?.ToString(),
                        IsCritical = s?.IsCritical ?? false,
                        IsIdempotent = s?.IsIdempotent ?? false,
                        CanRunInParallel = s?.CanRunInParallel ?? false,
                        Result = s?.Result != null ? new StepResultDto
                        {
                            IsSuccess = s.Result.IsSuccess,
                            Message = s.Result.Message,
                            Data = SafelyExtractSingleData(s.Result.Data) // Fixed: Handle single SafeObject
                        } : null,
                        Error = s?.Error,
                        Branches = s?.Branches?.Select(b => new BranchDto
                        {
                            Name = b?.Name ?? string.Empty,
                            IsDefault = b?.IsDefault ?? false,
                            IsConditional = b?.Condition != null,
                            Steps = b?.Steps?.Select(bs => new SubStepDto
                            {
                                Name = bs?.Name ?? "Unknown SubStep",
                                Status = bs?.Status.ToString() ?? "Unknown",
                                StepDependencies = bs?.StepDependencies ?? [],
                                DataDependencies = bs?.DataDependencies?.ToDictionary(kv => kv.Key, kv => kv.Value?.Name ?? "Unknown") ?? new(),
                                MaxRetries = bs?.MaxRetries ?? 0,
                                RetryDelay = bs?.RetryDelay.ToString() ?? "00:00:00",
                                Timeout = bs?.Timeout?.ToString(),
                                IsCritical = bs?.IsCritical ?? false,
                                IsIdempotent = bs?.IsIdempotent ?? false,
                                CanRunInParallel = bs?.CanRunInParallel ?? false,
                                Result = bs?.Result != null ? new StepResultDto
                                {
                                    IsSuccess = bs.Result.IsSuccess,
                                    Message = bs.Result.Message,
                                    Data = SafelyExtractSingleData(bs.Result.Data) // Fixed: Handle single SafeObject
                                } : null,
                                Error = bs?.Error,
                                Branches = bs?.Branches?.Select(b => new BranchDto
                                {
                                    // Handle recursive branches safely
                                    IsDefault = b?.IsDefault ?? false,
                                    IsConditional = b?.Condition != null
                                }).ToList() ?? [],
                                Priority = (bs as FlowSubStep)?.Priority ?? 0,
                                SourceData = (bs as FlowSubStep)?.SourceData,
                                Index = (bs as FlowSubStep)?.Index ?? 0,
                                Metadata = SafelyExtractData((bs as FlowSubStep)?.Metadata), // Fixed: Handle single SafeObject
                                EstimatedDuration = (bs as FlowSubStep)?.EstimatedDuration,
                                ResourceGroup = (bs as FlowSubStep)?.ResourceGroup

                            }).ToList() ?? [],
                            Priority = b?.Priority ?? 0,
                            ResourceGroup = b?.ResourceGroup,
                        }).ToList() ?? []
                    }).ToList() ?? [],
                    Events = flow.State.Events?.Select(e => new FlowEventDto
                    {
                        EventId = e?.EventId ?? Guid.Empty,
                        FlowId = e?.FlowId ?? flow.State.FlowId,
                        EventType = e?.EventType ?? "Unknown",
                        Description = e?.Description ?? "No description",
                        Timestamp = e?.Timestamp ?? DateTime.UtcNow,
                        Data = SafelyExtractData(e?.Data) // Correct: Handle dictionary
                    }).ToList() ?? [],
                    Data = SafelyExtractData(flow.State.Data), // Correct: Handle dictionary
                    TotalSteps = flow.Definition.Steps?.Count ?? 0
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error mapping flow {FlowId} to DTO", flow.State.FlowId);
                return new FlowDetailDto
                {
                    FlowId = flow.State.FlowId,
                    FlowType = flow.State.FlowType ?? "Unknown",
                    Status = flow.State.Status.ToString(),
                    LastError = $"Mapping error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Safely extracts data from SafeObject dictionary with error handling
        /// </summary>
        private Dictionary<string, object> SafelyExtractData(Dictionary<string, SafeObject> safeData)
        {
            if (safeData == null)
                return new Dictionary<string, object>();

            try
            {
                return safeData.FromSafe();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract SafeObject data, returning empty dictionary");
                return new Dictionary<string, object>
                {
                    ["_extraction_error"] = ex.Message
                };
            }
        }

        /// <summary>
        /// Safely extracts data from a single SafeObject with error handling
        /// </summary>
        private object SafelyExtractSingleData(SafeObject safeObject)
        {
            if (safeObject == null)
                return null;

            try
            {
                return safeObject.ToValue();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract single SafeObject data, returning error message");
                return $"_extraction_error: {ex.Message}";
            }
        }
    }

    // New DTO for step-specific updates
    public class StepStatusUpdateDto
    {
        public Guid FlowId { get; set; }
        public string StepName { get; set; }
        public string StepStatus { get; set; }
        public StepResultDto? StepResult { get; set; }
        public int CurrentStepIndex { get; set; }
        public string CurrentStepName { get; set; }
        public string FlowStatus { get; set; }
        public DateTime Timestamp { get; set; }
    }
}