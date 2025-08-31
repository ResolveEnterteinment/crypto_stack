using Application.Extensions;
using Domain.Constants;
using Domain.DTOs;
using Infrastructure.Hubs;
using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;
using Infrastructure.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Controllers
{
    /// <summary>
    /// FlowEngine Admin API Controller
    /// Provides comprehensive flow management endpoints for the admin panel
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "ADMIN")] // Add appropriate authorization
    public class FlowController : ControllerBase
    {
        private readonly IFlowEngineService _flowEngineService;
        private readonly IFlowExecutor _flowExecutor;
        private readonly IFlowPersistence _flowPersistence;
        private readonly IFlowRecovery _flowRecovery;
        private readonly IFlowAutoResumeService _autoResumeService;
        private readonly IHubContext<FlowHub> _hubContext;
        private readonly ILogger<FlowController> _logger;

        public FlowController(
            IFlowEngineService flowEngineService,
            IFlowExecutor flowExecutor,
            IFlowPersistence flowPersistence,
            IFlowRecovery flowRecovery,
            IFlowAutoResumeService autoResumeService,
            IHubContext<FlowHub> hubContext,
            ILogger<FlowController> logger)
        {
            _flowEngineService = flowEngineService ?? throw new ArgumentNullException(nameof(flowEngineService));
            _flowExecutor = flowExecutor ?? throw new ArgumentNullException(nameof(flowExecutor));
            _flowPersistence = flowPersistence ?? throw new ArgumentNullException(nameof(flowPersistence));
            _flowRecovery = flowRecovery ?? throw new ArgumentNullException(nameof(flowRecovery));
            _autoResumeService = autoResumeService ?? throw new ArgumentNullException(nameof(autoResumeService));
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region Flow Query Endpoints

        /// <summary>
        /// Get paginated list of flows with optional filtering
        /// </summary>
        [HttpGet("flows")]
        [ProducesResponseType(typeof(PagedResult<FlowSummaryDto>), 200)]
        public async Task<IActionResult> GetFlows(
            [FromQuery] FlowStatus? status = null,
            [FromQuery] string? userId = null,
            [FromQuery] string? flowType = null,
            [FromQuery] string? correlationId = null,
            [FromQuery] DateTime? createdAfter = null,
            [FromQuery] DateTime? createdBefore = null,
            [FromQuery] PauseReason? pauseReason = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            try
            {
                var query = new FlowQuery
                {
                    Status = status,
                    UserId = userId,
                    FlowType = flowType,
                    CorrelationId = correlationId,
                    CreatedAfter = createdAfter,
                    CreatedBefore = createdBefore,
                    PauseReason = pauseReason,
                    PageNumber = page,
                    PageSize = pageSize
                };

                var result = await _flowEngineService.QueryAsync(query);

                if(result == null)
                {
                    return ResultWrapper.Failure(FailureReason.NullReturnValue, "Failed to retrieve flows")
                        .ToActionResult(this);
                }

                // Convert to DTO with additional computed fields
                var dtoResult = new PagedResult<FlowSummaryDto>
                {
                    Items = result.Items.Select(flow => MapToFlowSummaryDto(flow)).ToList(),
                    TotalCount = result.TotalCount,
                    PageNumber = result.PageNumber,
                    PageSize = result.PageSize,
                };

                return ResultWrapper.Success(dtoResult)
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving flows");
                return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
            }
        }

        /// <summary>
        /// Get detailed information about a specific flow
        /// </summary>
        [HttpGet("flows/{flowId}")]
        [ProducesResponseType(typeof(FlowDetailDto), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetFlowById(Guid flowId)
        {
            try
            {
                var flow = await _flowEngineService.GetFlowById(flowId);
                if (flow == null)
                {
                    return ResultWrapper.NotFound("Flow", flowId.ToString())
                        .ToActionResult(this);
                }

                var detailDto = MapToFlowDetailDto(flow);
                return ResultWrapper.Success(detailDto)
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving flow {FlowId}", flowId);
                return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
            }
        }

        /// <summary>
        /// Get flow execution timeline
        /// </summary>
        [HttpGet("flows/{flowId}/timeline")]
        [ProducesResponseType(typeof(FlowTimeline), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetFlowTimeline(Guid flowId)
        {
            try
            {
                var timeline = await _flowEngineService.GetTimelineAsync(flowId);
                if (timeline == null)
                {
                    return ResultWrapper.NotFound("Flow", flowId.ToString())
                        .ToActionResult(this);
                }

                return ResultWrapper.Success(timeline)
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving flow timeline for {FlowId}", flowId);
                return ResultWrapper.InternalServerError().ToActionResult(this);
            }
        }

        /// <summary>
        /// Get flow statistics
        /// </summary>
        [HttpGet("statistics")]
        [ProducesResponseType(typeof(FlowStatisticsDto), 200)]
        public async Task<IActionResult> GetStatistics(
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            try
            {
                var stats = await CalculateStatistics(startDate, endDate);
                return ResultWrapper.Success(stats)
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating statistics");
                return ResultWrapper.InternalServerError().ToActionResult(this);
            }
        }

        #endregion

        #region Flow Control Endpoints

        /// <summary>
        /// Pause a running flow
        /// </summary>
        [HttpPost("flows/{flowId}/pause")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> PauseFlow(Guid flowId, [FromBody] PauseRequestDto? request = null)
        {
            try
            {
                var flow = await _flowEngineService.GetFlowById(flowId);
                if (flow == null)
                {
                    return ResultWrapper.NotFound("Flow", flowId.ToString())
                        .ToActionResult(this);
                }

                if (flow.Status != FlowStatus.Running)
                {
                    return ResultWrapper.Failure(FailureReason.InvalidOperation, $"Flow is not running. Current status: {flow.Status}")
                        .ToActionResult(this);
                }

                var pauseReason = request?.Reason ?? PauseReason.ManualIntervention;
                var message = request?.Message ?? $"Manually paused by {User.Identity?.Name ?? "Admin"}";

                var pasuseCondition = PauseCondition.Pause(pauseReason, message);

                await _flowExecutor.PauseFlowAsync(flow, pasuseCondition);

                // Notify clients via SignalR
                await _hubContext.Clients.All.SendAsync("FlowStatusChanged", new
                {
                    flowId,
                    status = FlowStatus.Paused,
                    pauseReason,
                    message,
                    timestamp = DateTime.UtcNow
                });

                return ResultWrapper.Success("Flow paused successfully")
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error pausing flow {FlowId}", flowId);
                return ResultWrapper.InternalServerError().ToActionResult(this);
            }
        }

        /// <summary>
        /// Resume a paused flow
        /// </summary>
        [HttpPost("flows/{flowId}/resume")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> ResumeFlow(Guid flowId, [FromBody] ResumeRequestDto? request = null)
        {
            try
            {
                var flow = await _flowEngineService.GetFlowById(flowId);
                if (flow == null)
                {
                    return ResultWrapper.NotFound("Flow", flowId.ToString()).ToActionResult(this);
                }

                if (flow.Status != FlowStatus.Paused)
                {
                    return ResultWrapper.Failure(FailureReason.InvalidOperation, 
                        $"Flow is not paused. Current status: {flow.Status}")
                        .ToActionResult(this);
                }

                var resumeData = request?.ResumeData ?? new Dictionary<string, object>();
                var result = await _flowEngineService.ResumeRuntimeAsync(flowId);

                if (result.Error == null && result.Flow.Status != FlowStatus.Paused)
                {
                    // Notify clients via SignalR
                    await _hubContext.Clients.All.SendAsync("FlowStatusChanged", new
                    {
                        flowId,
                        status = FlowStatus.Running,
                        message = "Flow resumed",
                        timestamp = DateTime.UtcNow
                    });

                    return ResultWrapper.Success(result, "Flow resumed successfully")
                        .ToActionResult(this);
                }

                return ResultWrapper.Failure(FailureReason.Unknown, "Failed to resume flow")
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resuming flow {FlowId}", flowId);
                return ResultWrapper.InternalServerError().ToActionResult(this);
            }
        }

        /// <summary>
        /// Cancel a running or paused flow
        /// </summary>
        [HttpPost("flows/{flowId}/cancel")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> CancelFlow(Guid flowId, [FromBody] CancelRequestDto? request = null)
        {
            try
            {
                var flow = await _flowEngineService.GetFlowById(flowId);
                if (flow == null)
                {
                    return ResultWrapper.NotFound("Flow", flowId.ToString()).ToActionResult(this);
                }

                if (flow.Status == FlowStatus.Completed || flow.Status == FlowStatus.Cancelled)
                {
                    return ResultWrapper.Failure(FailureReason.InvalidOperation, 
                        $"Flow has already ended. Status: {flow.Status}")
                        .ToActionResult(this);
                }

                var reason = request?.Reason ?? $"Cancelled by {User.Identity?.Name ?? "Admin"}";
                var result = await _flowEngineService.CancelAsync(flowId, reason);

                if (result)
                {
                    // Notify clients via SignalR
                    await _hubContext.Clients.All.SendAsync("FlowStatusChanged", new
                    {
                        flowId,
                        status = FlowStatus.Cancelled,
                        reason,
                        timestamp = DateTime.UtcNow
                    });

                    return ResultWrapper.Success(result, "Flow cancelled successfully")
                        .ToActionResult(this);
                }

                return ResultWrapper.Failure(FailureReason.Unknown, "Failed to cancel flow")
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling flow {FlowId}", flowId);
                return ResultWrapper.InternalServerError().ToActionResult(this);
            }
        }

        /// <summary>
        /// Resolve a failed flow
        /// </summary>
        [HttpPost("flows/{flowId}/resolve")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> ResolveFlow(Guid flowId, [FromBody] ResolveRequestDto? request = null)
        {
            try
            {
                var flow = await _flowEngineService.GetFlowById(flowId);
                if (flow == null)
                {
                    return ResultWrapper.NotFound("Flow", flowId.ToString()).ToActionResult(this);
                }

                if (flow.Status != FlowStatus.Failed)
                {
                    return BadRequest(new { error = $"Flow is not failed. Current status: {flow.Status}" });
                }

                var resolution = request?.Resolution ?? "Manually resolved";
                var resolvedBy = User.Identity?.Name ?? "Admin";

                // Mark flow as resolved (you might want to add a Resolved status to your enum)
                flow.Status = FlowStatus.Completed;
                flow.Events.Add(new FlowEvent
                {
                    FlowId = flowId.ToString(),
                    EventType = "FlowResolved",
                    Description = $"Flow resolved by {resolvedBy}: {resolution}",
                    Data = new Dictionary<string, object>
                    {
                        ["ResolvedBy"] = resolvedBy,
                        ["Resolution"] = resolution,
                        ["OriginalError"] = flow.LastError?.Message ?? "Unknown"
                    }.ToSafe()
                });

                await _flowPersistence.SaveFlowStateAsync(flow);

                // Notify clients via SignalR
                await _hubContext.Clients.All.SendAsync("FlowStatusChanged", new
                {
                    flowId,
                    status = FlowStatus.Completed,
                    resolution,
                    resolvedBy,
                    timestamp = DateTime.UtcNow
                });

                return Ok(new { success = true, message = "Flow resolved successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resolving flow {FlowId}", flowId);
                return ResultWrapper.InternalServerError().ToActionResult(this);
            }
        }

        /// <summary>
        /// Retry a failed flow from the last failed step
        /// </summary>
        [HttpPost("flows/{flowId}/retry")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> RetryFlow(Guid flowId)
        {
            try
            {
                var flow = await _flowEngineService.GetFlowById(flowId);
                if (flow == null)
                {
                    return ResultWrapper.NotFound("Flow", flowId.ToString()).ToActionResult(this);
                }

                if (flow.Status != FlowStatus.Failed)
                {
                    return BadRequest(new { error = $"Flow is not failed. Current status: {flow.Status}" });
                }

                // Reset the failed step and restart
                var currentStep = flow.Steps[flow.CurrentStepIndex];
                currentStep.Status = StepStatus.Pending;
                flow.Status = FlowStatus.Running;
                flow.LastError = null;

                await _flowPersistence.SaveFlowStateAsync(flow);

                // Re-execute the flow
                var result = await _flowEngineService.ResumeRuntimeAsync(flowId);

                if (result.Error == null && result.Flow.Status != FlowStatus.Paused)
                {
                    return Ok(new { success = true, message = "Flow retry initiated successfully" });
                }

                return BadRequest(new { error = "Failed to retry flow", message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrying flow {FlowId}", flowId);
                return ResultWrapper.InternalServerError().ToActionResult(this);
            }
        }

        #endregion

        #region Batch Operations

        /// <summary>
        /// Perform batch operations on multiple flows
        /// </summary>
        [HttpPost("flows/batch/{operation}")]
        [ProducesResponseType(typeof(BatchOperationResultDto), 200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> BatchOperation(
            string operation,
            [FromBody] BatchOperationRequestDto request)
        {
            if (request?.FlowIds == null || !request.FlowIds.Any())
            {
                return BadRequest(new { error = "No flow IDs provided" });
            }

            var validOperations = new[] { "pause", "resume", "cancel", "resolve" };
            if (!validOperations.Contains(operation.ToLower()))
            {
                return BadRequest(new { error = $"Invalid operation. Valid operations: {string.Join(", ", validOperations)}" });
            }

            var result = new BatchOperationResultDto
            {
                Operation = operation,
                TotalFlows = request.FlowIds.Count,
                SuccessCount = 0,
                FailureCount = 0,
                Results = new List<BatchOperationItemResult>()
            };

            foreach (var flowId in request.FlowIds)
            {
                try
                {
                    bool success = false;
                    string message = "";

                    var flow = await _flowEngineService.GetFlowById(flowId);

                    if (flow == null)
                    {
                        success = false;
                        message = "Flow not found";

                        result.Results.Add(new BatchOperationItemResult
                        {
                            FlowId = flowId,
                            Success = success,
                            Message = message
                        });

                        continue;
                    }

                    switch (operation.ToLower())
                    {
                        case "pause":
                            var pauseResult = await _flowExecutor.PauseFlowAsync(flow, new PauseCondition
                            {
                                Reason = PauseReason.ManualIntervention,
                                Message = "Batch pause operation"
                            });
                            success = pauseResult.Error == null && pauseResult.Flow.Status == FlowStatus.Paused;
                            message = success ? "Paused successfully" : "Failed to pause";
                            break;

                        case "resume":
                            var resumeResult = await _flowEngineService.ResumeRuntimeAsync(flowId);

                            if (resumeResult == null)
                            {
                                success = false;
                                message = "Resume result returned null";
                                break;
                            }
                            success = resumeResult.Error == null && resumeResult.Flow.Status != FlowStatus.Paused;
                            message = resumeResult.Message;
                            break;

                        case "cancel":
                            success = await _flowEngineService.CancelAsync(flowId, "Batch cancel operation");
                            message = success ? "Cancelled successfully" : "Failed to cancel";
                            break;

                        case "resolve":
                            if (flow != null && flow.Status == FlowStatus.Failed)
                            {
                                flow.Status = FlowStatus.Completed;
                                await _flowPersistence.SaveFlowStateAsync(flow);
                                success = true;
                                message = "Resolved successfully";
                            }
                            else
                            {
                                message = "Flow not in failed state";
                            }
                            break;
                    }

                    if (success)
                    {
                        result.SuccessCount++;
                    }
                    else
                    {
                        result.FailureCount++;
                    }

                    result.Results.Add(new BatchOperationItemResult
                    {
                        FlowId = flowId,
                        Success = success,
                        Message = message
                    });
                }
                catch (Exception ex)
                {
                    result.FailureCount++;
                    result.Results.Add(new BatchOperationItemResult
                    {
                        FlowId = flowId,
                        Success = false,
                        Message = ex.Message
                    });

                    _logger.LogError(ex, "Error during batch {Operation} for flow {FlowId}", operation, flowId);
                }
            }

            // Notify clients of batch operation completion
            await _hubContext.Clients.All.SendAsync("BatchOperationCompleted", result);

            return Ok(result);
        }

        #endregion

        #region Recovery Operations

        /// <summary>
        /// Recover crashed flows
        /// </summary>
        [HttpPost("recovery/crashed")]
        [ProducesResponseType(typeof(RecoveryResultDto), 200)]
        public async Task<IActionResult> RecoverCrashedFlows()
        {
            try
            {
                var result = await _flowEngineService.RecoverCrashedFlowsAsync();

                var dto = new RecoveryResultDto
                {
                    RecoveredCount = result.FlowsRecovered,
                    FailedCount = result.FlowsFailed,
                    RecoveredFlows = result.RecoveredFlowIds,
                    FailedFlows = result.FailedFlowsDict.Select(kvp => new FailedRecoveryDto
                    {
                        FlowId = kvp.Key,
                        Error = kvp.Value.Message
                    }).ToList()
                };

                return Ok(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recovering crashed flows");
                return ResultWrapper.InternalServerError().ToActionResult(this);
            }
        }

        /// <summary>
        /// Restore flow runtime (useful after server restart)
        /// </summary>
        [HttpPost("recovery/restore-runtime")]
        [ProducesResponseType(200)]
        public async Task<IActionResult> RestoreFlowRuntime()
        {
            try
            {
                await _flowEngineService.RestoreFlowRuntime();
                return Ok(new { success = true, message = "Flow runtime restored successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error restoring flow runtime");
                return ResultWrapper.InternalServerError().ToActionResult(this);
            }
        }

        #endregion

        #region Helper Methods

        private FlowSummaryDto MapToFlowSummaryDto(FlowSummary flow)
        {
            return new FlowSummaryDto
            {
                FlowId = flow.FlowId,
                FlowType = flow.FlowType,
                Status = flow.Status.ToString(),
                UserId = flow.UserId,
                CorrelationId = flow.CorrelationId,
                CreatedAt = flow.CreatedAt,
                StartedAt = flow.StartedAt,
                CompletedAt = flow.CompletedAt,
                CurrentStepName = flow.CurrentStepName,
                PauseReason = flow.PauseReason?.ToString(),
                ErrorMessage = flow.ErrorMessage,
                Duration = flow.Duration?.TotalSeconds,
                // Additional computed fields for the admin panel
                CurrentStepIndex = 0, // Would need to be fetched from full flow data
                TotalSteps = 0 // Would need to be fetched from full flow data
            };
        }

        private FlowDetailDto MapToFlowDetailDto(FlowDefinition flow)
        {
            var dto = new FlowDetailDto
            {
                FlowId = flow.FlowId,
                FlowType = flow.GetType().Name,
                Status = flow.Status.ToString(),
                UserId = flow.UserId,
                CorrelationId = flow.CorrelationId,
                CreatedAt = flow.CreatedAt,
                StartedAt = flow.StartedAt,
                CompletedAt = flow.CompletedAt,
                CurrentStepName = flow.CurrentStepName,
                CurrentStepIndex = flow.CurrentStepIndex,
                PausedAt = flow.PausedAt,
                PauseReason = flow.PauseReason?.ToString(),
                PauseMessage = flow.PauseMessage,
                LastError = flow.LastError?.Message,
                Steps = flow.Steps.Select(step => new StepDto
                {
                    Name = step.Name,
                    Status = step.Status.ToString(),
                    StepDependencies = step.StepDependencies,
                    DataDependencies = step.DataDependencies?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Name),
                    MaxRetries = step.MaxRetries,
                    RetryDelay = step.RetryDelay.ToString(),
                    Timeout = step.Timeout?.ToString(),
                    IsCritical = step.IsCritical,
                    IsIdempotent = step.IsIdempotent,
                    CanRunInParallel = step.CanRunInParallel,
                    Result = step.Result != null ? new StepResultDto
                    {
                        IsSuccess = step.Result.IsSuccess,
                        Message = step.Result.Message,
                        Data = step.Result.Data?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.Value)
                    } : null,
                    Branches = step.Branches?.Select(b => new BranchDto
                    {
                        Steps = b.Steps?.Select(s => s.Name).ToList() ?? new List<string>(),
                        IsDefault = b.IsDefault,
                        Condition = b.Condition?.Method.Name ?? "Unknown"
                    }).ToList()
                }).ToList(),
                Events = flow.Events,
                Data = flow.Data?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.Value),
                TotalSteps = flow.Steps.Count
            };

            return dto;
        }

        private async Task<FlowStatisticsDto> CalculateStatistics(DateTime? startDate, DateTime? endDate)
        {
            var start = startDate ?? DateTime.UtcNow.AddDays(-30);
            var end = endDate ?? DateTime.UtcNow;

            // Get all flows in the date range
            var query = new FlowQuery
            {
                CreatedAfter = start,
                CreatedBefore = end,
                PageSize = 10000 // Large page size to get all flows
            };

            var flows = await _flowEngineService.QueryAsync(query);

            var stats = new FlowStatisticsDto
            {
                Period = $"{start:yyyy-MM-dd} to {end:yyyy-MM-dd}",
                Total = flows.TotalCount,
                Running = flows.Items.Count(f => f.Status == FlowStatus.Running),
                Completed = flows.Items.Count(f => f.Status == FlowStatus.Completed),
                Failed = flows.Items.Count(f => f.Status == FlowStatus.Failed),
                Paused = flows.Items.Count(f => f.Status == FlowStatus.Paused),
                Cancelled = flows.Items.Count(f => f.Status == FlowStatus.Cancelled),

                // Calculate average duration for completed flows
                AverageDuration = flows.Items
                    .Where(f => f.Status == FlowStatus.Completed && f.Duration.HasValue)
                    .Select(f => f.Duration.Value.TotalSeconds)
                    .DefaultIfEmpty(0)
                    .Average(),

                // Group by flow type
                FlowsByType = flows.Items
                    .GroupBy(f => f.FlowType)
                    .ToDictionary(g => g.Key, g => g.Count()),

                // Group by pause reason
                PauseReasons = flows.Items
                    .Where(f => f.PauseReason.HasValue)
                    .GroupBy(f => f.PauseReason.Value.ToString())
                    .ToDictionary(g => g.Key, g => g.Count()),

                // Calculate success rate
                SuccessRate = flows.TotalCount > 0
                    ? (double)flows.Items.Count(f => f.Status == FlowStatus.Completed) / flows.TotalCount * 100
                    : 0
            };

            return stats;
        }

        #endregion
    }

    #region DTOs

    public class FlowSummaryDto
    {
        public Guid FlowId { get; set; }
        public string FlowType { get; set; }
        public string Status { get; set; }
        public string UserId { get; set; }
        public string CorrelationId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string CurrentStepName { get; set; }
        public string PauseReason { get; set; }
        public string ErrorMessage { get; set; }
        public double? Duration { get; set; }
        public int CurrentStepIndex { get; set; }
        public int TotalSteps { get; set; }
    }

    public class FlowDetailDto
    {
        public Guid FlowId { get; set; }
        public string FlowType { get; set; }
        public string Status { get; set; }
        public string UserId { get; set; }
        public string CorrelationId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? PausedAt { get; set; }
        public string CurrentStepName { get; set; }
        public int CurrentStepIndex { get; set; }
        public string PauseReason { get; set; }
        public string PauseMessage { get; set; }
        public string LastError { get; set; }
        public List<StepDto> Steps { get; set; } = [];
        public List<FlowEvent> Events { get; set; } = [];
        public Dictionary<string, object> Data { get; set; } = [];
        public int TotalSteps { get; set; }
    }

    public class StepDto
    {
        public string Name { get; set; }
        public string Status { get; set; }
        public List<string> StepDependencies { get; set; } = [];
        public Dictionary<string, string> DataDependencies { get; set; } = [];
        public int MaxRetries { get; set; }
        public string RetryDelay { get; set; }
        public string Timeout { get; set; }
        public bool IsCritical { get; set; }
        public bool IsIdempotent { get; set; }
        public bool CanRunInParallel { get; set; }
        public StepResultDto? Result { get; set; }
        public List<BranchDto> Branches { get; set; } = [];
    }

    public class StepResultDto
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public Dictionary<string, object> Data { get; set; } = [];
    }

    public class BranchDto
    {
        public bool IsDefault { get; set; } = false;
        public List<string> Steps { get; set; } = [];
        public string Target { get; set; }
        public string Condition { get; set; }
    }

    public class FlowStatisticsDto
    {
        public string Period { get; set; }
        public int Total { get; set; }
        public int Running { get; set; }
        public int Completed { get; set; }
        public int Failed { get; set; }
        public int Paused { get; set; }
        public int Cancelled { get; set; }
        public double AverageDuration { get; set; }
        public double SuccessRate { get; set; }
        public Dictionary<string, int> FlowsByType { get; set; } = [];
        public Dictionary<string, int> PauseReasons { get; set; } = [];
    }

    public class PauseRequestDto
    {
        public PauseReason Reason { get; set; }
        public string Message { get; set; }
    }

    public class ResumeRequestDto
    {
        public Dictionary<string, object> ResumeData { get; set; } = [];
    }

    public class CancelRequestDto
    {
        public string Reason { get; set; }
    }

    public class ResolveRequestDto
    {
        public string Resolution { get; set; }
    }

    public class BatchOperationRequestDto
    {
        public List<Guid> FlowIds { get; set; } = [];
        public Dictionary<string, object> Options { get; set; } = [];
    }

    public class BatchOperationResultDto
    {
        public string Operation { get; set; }
        public int TotalFlows { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public List<BatchOperationItemResult> Results { get; set; } = [];
    }

    public class BatchOperationItemResult
    {
        public Guid FlowId { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
    }

    public class RecoveryResultDto
    {
        public int RecoveredCount { get; set; }
        public int FailedCount { get; set; }
        public List<Guid> RecoveredFlows { get; set; } = [];
        public List<FailedRecoveryDto> FailedFlows { get; set; } = [];
    }

    public class FailedRecoveryDto
    {
        public Guid FlowId { get; set; }
        public string Error { get; set; }
    }

    #endregion
}