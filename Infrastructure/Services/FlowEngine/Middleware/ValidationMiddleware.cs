using Infrastructure.Services.FlowEngine.Core.Exceptions;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.FlowEngine.Middleware
{
    public class ValidationMiddleware : IFlowMiddleware
    {
        private readonly IFlowValidation _validation;
        private readonly ILogger<ValidationMiddleware> _logger;

        public ValidationMiddleware(IFlowValidation validation, ILogger<ValidationMiddleware> logger)
        {
            _validation = validation;
            _logger = logger;
        }

        public async Task InvokeAsync(FlowContext context, Func<Task> next)
        {
            // Validate flow definition
            var flowValidation = await _validation.ValidateFlowAsync(context.Flow);
            if (!flowValidation.IsValid)
            {
                var errors = string.Join(", ", flowValidation.Errors);
                _logger.LogError("Flow validation failed for {FlowId}: {Errors}", context.Flow.FlowId, errors);
                throw new FlowValidationException($"Flow validation failed: {errors}");
            }

            // Validate current step if executing
            if (context.CurrentStep != null)
            {
                var stepValidation = await _validation.ValidateStepAsync(context.CurrentStep, context.Flow);
                if (!stepValidation.IsValid)
                {
                    var errors = string.Join(", ", stepValidation.Errors);
                    _logger.LogError("Step validation failed for {StepName} in flow {FlowId}: {Errors}",
                        context.CurrentStep.Name, context.Flow.FlowId, errors);
                    throw new FlowValidationException($"Step validation failed: {errors}");
                }
            }

            await next();
        }
    }
}