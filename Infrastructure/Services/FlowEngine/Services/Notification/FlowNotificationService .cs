using Domain.DTOs.Flow;
using Infrastructure.Hubs;
using Infrastructure.Services.FlowEngine.Core.Enums;
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
                await _hubContext.Clients.Group($"flow-{flow.Id}")
                    .SendAsync("FlowStatusChanged", flowDetailDto);

                // Also send to admin group for dashboard updates
                await _hubContext.Clients.Group("flow-admins")
                    .SendAsync("FlowStatusChanged", flowDetailDto);

                _logger.LogDebug("Sent flow status update for flow {FlowId} with status {Status}",
                    flow.Id, flow.Status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending flow status notification for flow {FlowId}", flow?.Id);
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

                var flowDetailDto = MapToFlowDetailDto(flow);

                // Send detailed update to subscribed clients
                await _hubContext.Clients.Group($"flow-{flow.Id}")
                    .SendAsync("FlowStatusChanged", flowDetailDto);

                _logger.LogDebug("Sent step status update for flow {FlowId}, step {StepName}",
                    flow.Id, step.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending step status notification for flow {FlowId}", flow?.Id);
            }
        }

        public async Task NotifyFlowError(Guid flowId, string error)
        {
            try
            {
                await _hubContext.Clients.Group($"flow-{flowId}")
                    .SendAsync("FlowError", error);

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

            // Map FlowDefinition to FlowDetailDto
            // You can use AutoMapper or manual mapping here
            return new FlowDetailDto
            {
                FlowId = flow.State.FlowId,
                FlowType = flow.GetType().FullName,
                Status = flow.State.Status.ToString(),
                UserId = flow.State.UserId,
                CorrelationId = flow.State.CorrelationId,
                CreatedAt = flow.State.CreatedAt,
                StartedAt = flow.State.StartedAt,
                CompletedAt = flow.State.CompletedAt,
                PausedAt = flow.State.PausedAt,
                CurrentStepName = flow.State.CurrentStepName,
                CurrentStepIndex = flow.State.CurrentStepIndex,
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
                        Data = s.Result.Data?.FromSafe() ?? []
                    } : null,
                    Branches = s?.Branches?.Select(b => new BranchDto
                    {
                        IsDefault = b?.IsDefault ?? false,
                        Condition = b?.Condition?.Method?.Name ?? "Unknown",
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
                                Data = bs.Result.Data?.FromSafe() ?? []
                            } : null,
                            Branches = bs?.Branches?.Select(b => new BranchDto
                            {
                                // Handle recursive branches safely
                                IsDefault = b?.IsDefault ?? false,
                                Condition = b?.Condition?.Method?.Name ?? "Unknown"
                            }).ToList() ?? [],
                            Priority = (bs as FlowSubStep)?.Priority ?? 0,
                            SourceData = (bs as FlowSubStep)?.SourceData,
                            Index = (bs as FlowSubStep)?.Index ?? 0,
                            Metadata = (bs as FlowSubStep)?.Metadata?.FromSafe() ?? [],
                            EstimatedDuration = (bs as FlowSubStep)?.EstimatedDuration,
                            ResourceGroup = (bs as FlowSubStep)?.ResourceGroup

                        }).ToList() ?? []
                    }).ToList() ?? []
                }).ToList() ?? [],
                Events = flow.State.Events?.Select(e => new FlowEventDto
                {
                    EventId = e?.EventId ?? Guid.Empty,
                    FlowId = e?.FlowId ?? flow.Id,
                    EventType = e?.EventType ?? "Unknown",
                    Description = e?.Description ?? "No description",
                    Timestamp = e?.Timestamp ?? DateTime.UtcNow,
                    Data = e?.Data?.FromSafe() ?? new Dictionary<string, object>()
                }).ToList() ?? [],
                Data = flow.State.Data?.FromSafe() ?? new Dictionary<string, object>(),
                TotalSteps = flow.Definition.Steps?.Count ?? 0
            };
        }
    }
}