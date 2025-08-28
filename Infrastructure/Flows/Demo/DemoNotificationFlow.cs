using Application.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Definition.Builders;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Flows.Demo
{
    /// <summary>
    /// Simple notification flow triggered by the main demo flow
    /// </summary>
    public class DemoNotificationFlow : FlowDefinition
    {
        private readonly ILogger<DemoNotificationFlow> _logger;
        private readonly INotificationService _notificationService;
        private readonly FlowStepBuilder _builder;

        public DemoNotificationFlow(
            ILogger<DemoNotificationFlow> logger,
            INotificationService notificationService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _builder = new FlowStepBuilder(this);
        }

        protected override void DefineSteps()
        {
            _builder.Step("SendEmailNotification")
                .Execute(async context =>
                {
                    var notificationData = context.GetData<object>("NotificationData");
                    
                    _logger.LogInformation("Sending email notification for completed demo flow");
                    
                    await _notificationService.CreateAndSendNotificationAsync(new()
                    {
                        UserId = context.Flow.UserId,
                        Message = $"Your demo flow has completed successfully. Flow ID: {context.Flow.CorrelationId}"
                    });

                    return StepResult.Success("Email notification sent");
                })
                .WithRetries(maxRetries: 3, delay: TimeSpan.FromSeconds(2))
                .Build();

            _builder.Step("LogCompletion")
                .After("SendEmailNotification")
                .Execute(async context =>
                {
                    _logger.LogInformation("Demo flow notification completed for user {UserId}", context.Flow.UserId);
                    await Task.CompletedTask;
                    return StepResult.Success("Notification flow completed");
                })
                .Build();
        }
    }
}