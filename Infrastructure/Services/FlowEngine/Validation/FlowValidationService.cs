using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;

// Use aliases to distinguish between our ValidationResult and .NET's ValidationResult
using DataAnnotationValidationResult = System.ComponentModel.DataAnnotations.ValidationResult;
using FlowValidationResult = Infrastructure.Services.FlowEngine.Validation.ValidationResult;

namespace Infrastructure.Services.FlowEngine.Validation
{

    /// <summary>
    /// Default validation service implementation
    /// </summary>
    public sealed class FlowValidationService : IFlowValidation
    {
        private readonly ILogger<FlowValidationService> _logger;

        public FlowValidationService(ILogger<FlowValidationService> logger)
        {
            _logger = logger;
        }

        public async Task<FlowValidationResult> ValidateAsync<T>(T data, CancellationToken cancellationToken) where T : IValidatable
        {
            ArgumentNullException.ThrowIfNull(data);

            // Use DataAnnotations validation with the correct .NET ValidationResult type
            var validationContext = new ValidationContext(data);
            var results = new List<DataAnnotationValidationResult>();

            var isValid = Validator.TryValidateObject(data, validationContext, results, validateAllProperties: true);

            if (isValid)
            {
                return FlowValidationResult.Success();
            }

            // Extract error messages from .NET ValidationResult objects
            var errors = results
                .Where(r => !string.IsNullOrEmpty(r.ErrorMessage))
                .Select(r => r.ErrorMessage!)
                .ToArray();

            return FlowValidationResult.Failure(errors);
        }
    }
}