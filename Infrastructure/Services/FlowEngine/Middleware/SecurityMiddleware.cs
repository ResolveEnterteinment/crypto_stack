using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Infrastructure.Services.FlowEngine.Middleware
{
    public class SecurityMiddleware : IFlowMiddleware
    {
        private readonly IFlowSecurity _security;
        private readonly ILogger<SecurityMiddleware> _logger;

        public SecurityMiddleware(IFlowSecurity security, ILogger<SecurityMiddleware> logger)
        {
            _security = security;
            _logger = logger;
        }

        public async Task InvokeAsync(FlowExecutionContext context, Func<Task> next)
        {
            // Validate user authorization
            if (!await _security.ValidateUserAccessAsync(context.Flow.State.UserId, context.Flow.GetType()))
            {
                _logger.LogWarning("Unauthorized access attempt for flow {FlowId} by user {UserId}", 
                    context.Flow.State.FlowId, context.Flow.State.UserId);
                throw new UnauthorizedAccessException($"User {context.Flow.State.UserId} not authorized for this flow");
            }

            // Validate step permissions
            if (context.CurrentStep != null && 
                !await _security.ValidateStepAccessAsync(context.Flow.State.UserId, context.CurrentStep.Name))
            {
                _logger.LogWarning("Unauthorized step access attempt for step {StepName} by user {UserId}", 
                    context.CurrentStep.Name, context.Flow.State.UserId);
                throw new UnauthorizedAccessException($"User not authorized for step {context.CurrentStep.Name}");
            }

            // Log security event
            _logger.LogInformation("Security validation passed for flow {FlowId}, step {StepName}", 
                context.Flow.State.FlowId, context.CurrentStep?.Name ?? "N/A");

            await next();
        }
    }
}
