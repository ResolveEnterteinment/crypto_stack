using Infrastructure.Flows.Demo;
using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FlowEngineTestController : ControllerBase
    {
        private readonly IFlowEngineService _flowEngineService;
        private readonly IFlowAutoResumeService _autoResumeService;
        private readonly ILogger<FlowEngineTestController> _logger;

        public FlowEngineTestController(
            IFlowEngineService flowEngineService,
            IFlowAutoResumeService autoResumeService,
            ILogger<FlowEngineTestController> logger)
        {
            _flowEngineService = flowEngineService ?? throw new ArgumentNullException(nameof(flowEngineService));
            _autoResumeService = autoResumeService ?? throw new ArgumentNullException(nameof(autoResumeService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Start a comprehensive demo flow showcasing all FlowEngine capabilities
        /// </summary>
        [HttpPost("demo/start")]
        public async Task<IActionResult> StartDemoFlow([FromBody] StartDemoRequest request)
        {
            try
            {
                string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                var demoRequest = new DemoRequest
                {
                    EnableValidation = request.EnableValidation,
                    RequiresApproval = request.RequiresApproval,
                    SimulateExternalApiFailure = request.SimulateExternalApiFailure,
                    SimulateValidationFailure = request.SimulateValidationFailure,
                    Items = request.Items.Count > 0 ? request.Items  :  new List<string> { "Item1", "Item2", "Item3", "Item4", "Item5" }
                };

                var result = await _flowEngineService.StartAsync<ComprehensiveDemoFlow>(
                    new() {
                        ["Request"] = demoRequest
                    },
                    userId ?? "demo-user",
                    $"demo-{Guid.NewGuid()}");

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start demo flow");
                return BadRequest(new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Get the status and timeline of a flow
        /// </summary>
        [HttpGet("{flowId}/status")]
        public async Task<IActionResult> GetFlowStatus(Guid flowId)
        {
            try
            {
                var status = _flowEngineService.GetStatus(flowId);
                var timeline = await _flowEngineService.GetTimelineAsync(flowId);

                return Ok(new
                {
                    FlowId = flowId,
                    Status = status.ToString(),
                    Timeline = timeline.Events.Select(e => new
                    {
                        e.Timestamp,
                        e.EventType,
                        e.StepName,
                        e.Status,
                        e.Message
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get flow status for {FlowId}", flowId);
                return BadRequest(new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Resume a paused flow manually
        /// </summary>
        [HttpPost("{flowId}/resume")]
        public async Task<IActionResult> ResumeFlow(Guid flowId, [FromBody] ResumeFlowRequest request)
        {
            try
            {
                var success = await _flowEngineService.ResumeManuallyAsync(
                    flowId,
                    request.UserId ?? "admin",
                    request.Reason);

                return Ok(new
                {
                    FlowId = flowId,
                    Resumed = success,
                    ResumedBy = request.UserId,
                    Reason = request.Reason,
                    ResumedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resume flow {FlowId}", flowId);
                return BadRequest(new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Cancel a running flow
        /// </summary>
        [HttpPost("{flowId}/cancel")]
        public async Task<IActionResult> CancelFlow(Guid flowId, [FromBody] CancelFlowRequest request)
        {
            try
            {
                var success = await _flowEngineService.CancelAsync(flowId, request.Reason);

                return Ok(new
                {
                    FlowId = flowId,
                    Cancelled = success,
                    Reason = request.Reason,
                    CancelledAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cancel flow {FlowId}", flowId);
                return BadRequest(new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Publish an event to trigger flow resumes
        /// </summary>
        [HttpPost("events/publish")]
        public async Task<IActionResult> PublishEvent([FromBody] PublishEventRequest request)
        {
            try
            {
                await _flowEngineService.PublishEventAsync(
                    request.EventType,
                    request.EventData,
                    request.CorrelationId);

                return Ok(new
                {
                    EventType = request.EventType,
                    PublishedAt = DateTime.UtcNow,
                    CorrelationId = request.CorrelationId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish event {EventType}", request.EventType);
                return BadRequest(new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Approve a demo flow (publishes approval event)
        /// </summary>
        [HttpPost("demo/approve")]
        public async Task<IActionResult> ApproveDemoFlow([FromBody] ApproveDemoRequest request)
        {
            try
            {
                var approvalEvent = new DemoApprovalEvent
                {
                    FlowId = request.FlowId,
                    Approved = request.Approved,
                    ApprovedBy = request.ApprovedBy ?? "system",
                    Reason = request.Reason ?? "Manual approval",
                    ApprovedAt = DateTime.UtcNow
                };

                await _flowEngineService.PublishEventAsync("DemoApproval", approvalEvent, request.FlowId);

                return Ok(new
                {
                    FlowId = request.FlowId,
                    Approved = request.Approved,
                    ApprovedBy = approvalEvent.ApprovedBy,
                    ApprovedAt = approvalEvent.ApprovedAt
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to approve demo flow {FlowId}", request.FlowId);
                return BadRequest(new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Get all paused flows
        /// </summary>
        [HttpGet("paused")]
        public async Task<IActionResult> GetPausedFlows([FromQuery] string userId = null, [FromQuery] int pageSize = 20)
        {
            try
            {
                var query = new FlowQuery
                {
                    Status = FlowStatus.Paused,
                    UserId = userId,
                    PageSize = pageSize
                };

                var result = await _flowEngineService.QueryAsync(query);

                return Ok(new
                {
                    TotalCount = result.TotalCount,
                    PageSize = result.PageSize,
                    PageNumber = result.PageNumber,
                    Flows = result.Items.Select(f => new
                    {
                        f.FlowId,
                        f.FlowType,
                        f.Status,
                        f.UserId,
                        f.CreatedAt,
                        f.CurrentStepName,
                        f.PauseReason
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get paused flows");
                return BadRequest(new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Run automated checks for flows that can be auto-resumed
        /// </summary>
        [HttpPost("auto-resume/check")]
        public async Task<IActionResult> CheckAutoResume()
        {
            try
            {
                var resumedCount = await _autoResumeService.CheckAndResumeFlowsAsync();

                return Ok(new
                {
                    ResumedFlowsCount = resumedCount,
                    CheckedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check auto-resume conditions");
                return BadRequest(new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Recover any crashed flows
        /// </summary>
        [HttpPost("recovery/run")]
        public async Task<IActionResult> RecoverFlows()
        {
            try
            {
                var result = await _flowEngineService.RecoverCrashedFlowsAsync();

                return Ok(new
                {
                    TotalFlowsChecked = result.TotalFlowsChecked,
                    FlowsRecovered = result.FlowsRecovered,
                    FlowsFailed = result.FlowsFailed,
                    RecoveredFlowIds = result.RecoveredFlowIds,
                    FailedFlowIds = result.FailedFlowsDict,
                    Duration = result.Duration
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to recover flows");
                return BadRequest(new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Clean up old completed flows
        /// </summary>
        [HttpPost("cleanup")]
        public async Task<IActionResult> CleanupFlows([FromQuery] int olderThanDays = 30)
        {
            try
            {
                var cleanedCount = await _flowEngineService.CleanupAsync(TimeSpan.FromDays(olderThanDays));

                return Ok(new
                {
                    CleanedFlowsCount = cleanedCount,
                    OlderThanDays = olderThanDays,
                    CleanedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to cleanup flows");
                return BadRequest(new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Get flow execution statistics
        /// </summary>
        [HttpGet("statistics")]
        public async Task<IActionResult> GetStatistics([FromQuery] int days = 7)
        {
            try
            {
                var query = new FlowQuery
                {
                    CreatedAfter = DateTime.UtcNow.AddDays(-days),
                    PageSize = 1000
                };

                var flows = await _flowEngineService.QueryAsync(query);

                var stats = new
                {
                    Period = $"Last {days} days",
                    TotalFlows = flows.TotalCount,
                    CompletedFlows = flows.Items.Count(f => f.Status == FlowStatus.Completed),
                    FailedFlows = flows.Items.Count(f => f.Status == FlowStatus.Failed),
                    RunningFlows = flows.Items.Count(f => f.Status == FlowStatus.Running),
                    PausedFlows = flows.Items.Count(f => f.Status == FlowStatus.Paused),
                    CancelledFlows = flows.Items.Count(f => f.Status == FlowStatus.Cancelled),
                    SuccessRate = flows.TotalCount > 0 ? 
                        (double)flows.Items.Count(f => f.Status == FlowStatus.Completed) / flows.TotalCount * 100 : 0,
                    AverageExecutionTime = flows.Items
                        .Where(f => f.Duration.HasValue)
                        .Select(f => f.Duration.Value.TotalMilliseconds)
                        .DefaultIfEmpty()
                        .Average()
                };

                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get flow statistics");
                return BadRequest(new { Error = ex.Message });
            }
        }

        /// <summary>
        /// Fire-and-forget demo flow (for testing asynchronous execution)
        /// </summary>
        [HttpPost("demo/fire")]
        public async Task<IActionResult> FireDemoFlow([FromBody] StartDemoRequest request)
        {
            try
            {
                string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                var demoRequest = new DemoRequest
                {
                    EnableValidation = request.EnableValidation,
                    RequiresApproval = false, // No approval for fire-and-forget
                    Items = request.Items ?? new List<string> { "FireItem1", "FireItem2" }
                };

                await _flowEngineService.FireAsync<ComprehensiveDemoFlow>(new()
                {
                    ["DemoRequest"] = demoRequest,
                }, userId ?? "demo-user");

                return Ok(new
                {
                    Message = "Demo flow fired successfully",
                    FiredAt = DateTime.UtcNow,
                    Note = "Flow is running asynchronously - check logs for progress"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fire demo flow");
                return BadRequest(new { Error = ex.Message });
            }
        }
    }

    // Request/Response DTOs
    public class StartDemoRequest
    {
        public bool EnableValidation { get; set; } = true;
        public bool RequiresApproval { get; set; } = false;
        public bool SimulateValidationFailure { get; set; } = false;
        public bool SimulateExternalApiFailure { get; set; } = false;
        public List<string> Items { get; set; }
    }

    public class ResumeFlowRequest
    {
        public string UserId { get; set; }
        public string Reason { get; set; }
    }

    public class CancelFlowRequest
    {
        public string Reason { get; set; }
    }

    public class PublishEventRequest
    {
        public string EventType { get; set; }
        public object EventData { get; set; }
        public string CorrelationId { get; set; }
    }

    public class ApproveDemoRequest
    {
        public string FlowId { get; set; }
        public bool Approved { get; set; } = true;
        public string ApprovedBy { get; set; }
        public string Reason { get; set; }
    }
}