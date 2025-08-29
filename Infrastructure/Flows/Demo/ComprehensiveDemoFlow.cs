using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;
using Infrastructure.Services.FlowEngine.Definition.Builders;
using Infrastructure.Services.FlowEngine.Middleware;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Flows.Demo
{
    /// <summary>
    /// Comprehensive demonstration flow showcasing all FlowEngine capabilities
    /// </summary>
    public class ComprehensiveDemoFlow : FlowDefinition
    {
        private readonly ILogger<ComprehensiveDemoFlow> _logger;
        private readonly IDemoService _demoService;
        private readonly FlowStepBuilder _builder;

        public ComprehensiveDemoFlow(
            ILogger<ComprehensiveDemoFlow> logger,
            IDemoService demoService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _demoService = demoService ?? throw new ArgumentNullException(nameof(demoService));
            _builder = new FlowStepBuilder(this);
        }

        /// <summary>
        /// Configure flow-level middleware
        /// </summary>
        protected override void ConfigureMiddleware()
        {
            // Method 1: Using the protected method
            UseMiddleware<ValidationMiddleware>();

            // Method 2: Using the builder (alternative approach)
            // _builder.UseMiddleware<SecurityMiddleware>()
            //         .UseMiddleware<ValidationMiddleware>()
            //         .UseMiddleware<MetricsMiddleware>();

            // Method 3: Adding multiple at once
            // UseMiddleware(typeof(SecurityMiddleware), typeof(ValidationMiddleware), typeof(MetricsMiddleware));
        }
        protected override void DefineSteps()
        {

            // Step 1: Basic step with data validation
            _builder.Step("InitializeDemo")
                .RequiresData<DemoRequest>("Request")
                .Execute(async context =>
                {
                    var request = context.GetData<DemoRequest>("Request");
                    _logger.LogInformation("Starting comprehensive demo for user {UserId}", context.Flow.UserId);

                    // Simulate initialization
                    await Task.Delay(100, context.CancellationToken);

                    return StepResult.Success("Demo initialized successfully", new()
                    {
                        ["DemoId"] = Guid.NewGuid(),
                        ["StartTime"] = DateTime.UtcNow,
                        ["ProcessingItems"] = request.Items?.ToList() ?? new List<string>()
                    });
                })
                .WithTimeout(TimeSpan.FromSeconds(30))
                .Critical()
                .Build();

            // Step 2: Conditional step with retries
            _builder.Step("ValidateConfiguration")
                .After("InitializeDemo")
                .OnlyIf(context => context.GetData<DemoRequest>("Request").EnableValidation)
                .Execute(async context =>
                {
                    _logger.LogInformation("Validating configuration...");
                    
                    // Simulate potential failure for demo
                    var random = new Random();
                    if (random.Next(1, 4) == 1) // 25% chance of failure
                    {
                        throw new InvalidOperationException("Configuration validation failed (simulated)");
                    }

                    await Task.Delay(200, context.CancellationToken);
                    return StepResult.Success("Configuration validated");
                })
                .WithRetries(maxRetries: 3, delay: TimeSpan.FromSeconds(2))
                .WithTimeout(TimeSpan.FromSeconds(10))
                .Build();

            // Step 3: Parallel step with dynamic branching
            _builder.Step("ProcessItemsInParallel")
                .After("InitializeDemo")
                .RequiresData<List<string>>("ProcessingItems")
                .WithDynamicBranches(
                    // Data selector: Get items to process
                    context => context.GetData<List<string>>("ProcessingItems"),
                    
                    // Step factory: Create sub-step for each item
                    (item, index) => new FlowSubStep
                    {
                        Name = $"ProcessItem_{item}_{index}",
                        SourceData = item,
                        Priority = index < 3 ? 1 : 2, // First 3 items have higher priority
                        ExecuteAsync = async ctx =>
                        {
                            _logger.LogInformation("Processing item: {Item}", item);
                            
                            // Simulate processing time
                            await Task.Delay(1000 + (index * 200), ctx.CancellationToken);
                            
                            var result = await _demoService.ProcessItemAsync(item);
                            
                            return StepResult.Success($"Processed {item}", new()
                            {
                                ["ProcessedItem"] = item,
                                ["ProcessingTime"] = DateTime.UtcNow,
                                ["Result"] = result
                            });
                        }
                    },
                    ExecutionStrategy.Parallel // Process items in parallel
                )
                .InParallel()
                .Critical()
                .Build();

            // Step 4: Step with pause/resume capability (e.g., waiting for external approval)
            _builder.Step("RequireApproval")
                .After("ProcessItemsInParallel", "ValidateConfiguration")
                .CanPause(context =>
                {
                    _logger.LogInformation("Approval required for demo flow");

                    var request = context.GetData<DemoRequest>("Request");
                    if (request.RequiresApproval)
                    {
                        return PauseCondition.Pause(
                            Services.FlowEngine.Core.Enums.PauseReason.ComplianceReview,
                            "Waiting for manual approval",
                            new Dictionary<string, object>
                            {
                                ["RequesterUserId"] = context.Flow.UserId,
                                ["ApprovalRequestId"] = Guid.NewGuid(),
                                ["ApprovalType"] = "Demo Workflow Approval"
                            });
                    }
                    return PauseCondition.Continue();
                })
                .ResumeOn(resume =>
                {
                    resume.OnEvent("DemoApproval", eventData =>
                    {
                        var approval = eventData as DemoApprovalEvent;
                        return approval?.Approved == true;
                    });

                    resume.AllowManual(["ADMIN"]);
                    
                    resume.WhenCondition(async context =>
                    {
                        // Auto-approve after 5 minutes for demo purposes
                        var pausedAt = context.Flow.PausedAt;
                        
                        return pausedAt.HasValue && 
                               DateTime.UtcNow.Subtract(pausedAt.Value) > TimeSpan.FromMinutes(1);
                    });
                })
                .Execute(async context =>
                {
                    var request = context.GetData<DemoRequest>("Request");
                    var approvalRequestId = context.GetData<DemoRequest>("ApprovalRequestId");

                    // Check if approval is required
                    if (request.RequiresApproval)
                    {
                        _logger.LogInformation("Approval required for demo flow");

                        // Pause the flow for manual approval
                        return StepResult.Success($"Approved request {approvalRequestId}", new()
                        {
                            ["ApprovalStatus"] = "Approved",
                            ["ApprovedAt"] = DateTime.UtcNow,
                        });
                    }

                    return StepResult.Success("No approval required");
                })
                .Build();

            // Step 5: Resource-intensive step with load balancing
            _builder.Step("PerformComplexCalculation")
                .After("RequireApproval")
                .Execute(async context =>
                {
                    _logger.LogInformation("Performing complex calculation...");
                    
                    // Simulate resource-intensive operation
                    var calculation = await _demoService.PerformComplexCalculationAsync();
                    
                    return StepResult.Success("Complex calculation completed", new()
                    {
                        ["CalculationResult"] = calculation.Result,
                        ["ExecutionTime"] = calculation.ExecutionTime,
                        ["ResourceUsage"] = calculation.ResourceUsage
                    });
                })
                .WithTimeout(TimeSpan.FromMinutes(5))
                .Critical()
                .CanPause(context =>
                {
                    // Pause if system resources are low
                    var systemLoad = _demoService.GetSystemLoad();
                    if (systemLoad > 0.8)
                    {
                        return new PauseCondition
                        {
                            Reason = Services.FlowEngine.Core.Enums.PauseReason.ComplianceReview, // Using closest available enum
                            Message = $"System under high load: {systemLoad:P}",
                            Data = new() { ["SystemLoad"] = systemLoad }
                        };
                    }
                    return PauseCondition.Continue();
                })
                .ResumeOn(resume =>
                {
                    resume.OnEvent("SystemResourcesAvailable");
                    resume.WhenCondition(async context =>
                    {
                        return _demoService.GetSystemLoad() < 0.6;
                    });
                })
                .Build();

            // Step 6: External API integration with retries
            _builder.Step("CallExternalAPI")
                .After("PerformComplexCalculation")
                .Execute(async context =>
                {
                    _logger.LogInformation("Calling external API...");
                    
                    var apiResult = await _demoService.CallExternalApiAsync();
                    
                    return StepResult.Success("External API call successful", new()
                    {
                        ["ApiResponse"] = apiResult.Data,
                        ["ApiResponseTime"] = apiResult.ResponseTime,
                        ["ApiStatusCode"] = apiResult.StatusCode
                    });
                })
                .WithRetries(maxRetries: 5, delay: TimeSpan.FromSeconds(3))
                .WithIdempotency()
                .WithTimeout(TimeSpan.FromSeconds(30))
                .AllowFailure()
                .Build();

            // Step 7: Conditional branching
            _builder.Step("ProcessBasedOnResult")
                .After("CallExternalAPI")
                .WithBranches(branches =>
                {
                    branches.When(
                        context =>
                        {
                            var apiResponse = context.GetData<ApiResult>("ApiResponse");
                            return apiResponse != null;
                        },
                        branch => branch.Step("SuccessPath")
                        .Execute(async context =>
                        {
                            _logger.LogInformation("Following success path");
                            await Task.Delay(500, context.CancellationToken);
                            return StepResult.Success("Success path completed");
                        })
                        .Build());

                    branches.Otherwise(
                        branch => branch.Step("ErrorPath")
                        .Execute(async context =>
                        {
                            _logger.LogWarning("Following error path");
                            await Task.Delay(300, context.CancellationToken);
                            return StepResult.Success("Error path completed with recovery");
                        })
                        .JumpTo("PerformComplexCalculation") // Loop back for demo purposes to test idempotency
                        .Build());
                })
                .Build();

            // Step 8: Triggering another flow
            _builder.Step("TriggerNotificationFlow")
                .After("ProcessBasedOnResult")
                .Execute(async context =>
                {
                    _logger.LogInformation("Preparing notification data");
                    
                    var notificationData = new
                    {
                        FlowId = context.Flow.FlowId,
                        UserId = context.Flow.UserId,
                        Status = "Completed",
                        CompletedAt = DateTime.UtcNow,
                        Summary = "Demo flow completed successfully"
                    };

                    return StepResult.Success("Notification flow will be triggered", new()
                    {
                        ["NotificationData"] = notificationData
                    });
                })
                .Triggers<DemoNotificationFlow>()
                .Build();

            // Step 9: Final cleanup and reporting
            _builder.Step("FinalizeDemo")
                .After("TriggerNotificationFlow")
                .Execute(async context =>
                {
                    _logger.LogInformation("Finalizing demo flow");

                    var summary = new DemoFlowSummary
                    {
                        FlowId = context.Flow.FlowId,
                        StartTime = context.GetData<DateTime>("StartTime"),
                        EndTime = DateTime.UtcNow,
                        ProcessedItems = context.GetData<List<string>>("ProcessingItems")?.Count ?? 0,
                        TotalSteps = context.Flow.Steps.Count,
                        Status = "Completed Successfully"
                    };

                    // Persist summary
                    await _demoService.SaveDemoSummaryAsync(summary);

                    return StepResult.Success("Demo flow completed successfully", new()
                    {
                        ["FlowSummary"] = summary,
                        ["ExecutionTime"] = summary.EndTime.Subtract(summary.StartTime)
                    });
                })
                .WithIdempotency()
                .AllowFailure()
                .Build();
        }
    }

    // Supporting classes for the demo
    public class DemoRequest
    {
        public bool EnableValidation { get; set; } = true;
        public bool RequiresApproval { get; set; } = false;
        public List<string> Items { get; set; } = new();
    }

    public class DemoApprovalEvent
    {
        public string FlowId { get; set; }
        public bool Approved { get; set; }
        public string ApprovedBy { get; set; }
        public string Reason { get; set; }
        public DateTime ApprovedAt { get; set; }
    }

    public class DemoFlowSummary
    {
        public Guid FlowId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int ProcessedItems { get; set; }
        public int TotalSteps { get; set; }
        public string Status { get; set; }
    }

    // Demo service interface
    public interface IDemoService
    {
        Task<string> ProcessItemAsync(string item);
        Task<CalculationResult> PerformComplexCalculationAsync();
        Task<ApiResult> CallExternalApiAsync();
        Task SaveDemoSummaryAsync(DemoFlowSummary summary);
        double GetSystemLoad();
    }

    public class CalculationResult
    {
        public object Result { get; set; }
        public TimeSpan ExecutionTime { get; set; }
        public double ResourceUsage { get; set; }
    }

    public class ApiResult
    {
        public object Data { get; set; }
        public TimeSpan ResponseTime { get; set; }
        public int StatusCode { get; set; }
    }
}