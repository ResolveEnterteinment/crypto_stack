using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Infrastructure.Services.FlowEngine.Services.Validation
{
    /// <summary>
    /// Implementation of flow validation service
    /// </summary>
    public class FlowValidationService : IFlowValidation
    {
        private readonly ILogger<FlowValidationService> _logger;

        public FlowValidationService(ILogger<FlowValidationService> logger)
        {
            _logger = logger;
        }

        public async Task<FlowValidationResult> ValidateFlowAsync(FlowDefinition flow)
        {
            var result = new FlowValidationResult
            {
                FlowId = flow.FlowId,
                FlowType = flow.GetType().Name
            };

            try
            {
                // Basic flow validation
                if (string.IsNullOrEmpty(flow.FlowId))
                {
                    result.Errors.Add("Flow ID cannot be empty");
                }

                if (string.IsNullOrEmpty(flow.UserId))
                {
                    result.Errors.Add("User ID cannot be empty");
                }

                if (flow.CreatedAt == default)
                {
                    result.Errors.Add("Created date must be set");
                }

                // Validate steps
                if (flow.Steps == null || !flow.Steps.Any())
                {
                    result.Warnings.Add("Flow has no steps defined");
                }
                else
                {
                    foreach (var step in flow.Steps)
                    {
                        var stepValidation = await ValidateStepAsync(step, flow);
                        if (!stepValidation.IsValid)
                        {
                            result.Errors.AddRange(stepValidation.Errors.Select(e => $"Step '{step.Name}': {e}"));
                            result.Warnings.AddRange(stepValidation.Warnings.Select(w => $"Step '{step.Name}': {w}"));
                        }
                    }
                }

                // Validate dependencies
                var dependencyValidation = await ValidateDependenciesAsync(flow);
                if (!dependencyValidation.IsValid)
                {
                    result.Errors.AddRange(dependencyValidation.Errors);
                }

                // Validate flow data if present
                if (flow.Data != null)
                {
                    var dataValidation = await ValidateFlowDataAsync(flow.Data, flow.GetType());
                    if (!dataValidation.IsValid)
                    {
                        result.Errors.AddRange(dataValidation.Errors.Select(e => $"Flow data: {e}"));
                        result.Warnings.AddRange(dataValidation.Warnings.Select(w => $"Flow data: {w}"));
                    }
                }

                result.IsValid = !result.Errors.Any();

                _logger.LogInformation(
                    "Flow validation completed for {FlowType} {FlowId}. Valid: {IsValid}, Errors: {ErrorCount}, Warnings: {WarningCount}",
                    result.FlowType, result.FlowId, result.IsValid, result.Errors.Count, result.Warnings.Count);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating flow {FlowId}", flow.FlowId);
                result.Errors.Add($"Validation error: {ex.Message}");
                result.IsValid = false;
                return result;
            }
        }

        public async Task<StepValidationResult> ValidateStepAsync(FlowStep step, FlowDefinition flow)
        {
            var result = new StepValidationResult
            {
                StepName = step.Name
            };

            try
            {
                // Basic step validation
                if (string.IsNullOrEmpty(step.Name))
                {
                    result.Errors.Add("Step name cannot be empty");
                }

                if (step.ExecuteAsync == null)
                {
                    result.Errors.Add("Step must have an ExecuteAsync function");
                }

                // Validate timeout settings
                if (step.Timeout.HasValue && step.Timeout.Value <= TimeSpan.Zero)
                {
                    result.Errors.Add("Step timeout must be positive");
                }

                // Validate retry settings
                if (step.MaxRetries < 0)
                {
                    result.Errors.Add("Max retries cannot be negative");
                }

                if (step.RetryDelay <= TimeSpan.Zero && step.MaxRetries > 0)
                {
                    result.Warnings.Add("Retry delay should be positive when retries are enabled");
                }

                // Validate dependencies
                if (step.Dependencies?.Any() == true)
                {
                    foreach (var dependency in step.Dependencies)
                    {
                        if (!flow.Steps.Any(s => s.Name == dependency))
                        {
                            result.Errors.Add($"Dependency '{dependency}' not found in flow steps");
                        }
                    }
                }

                // Validate branches
                if (step.Branches?.Any() == true)
                {
                    var hasDefaultBranch = false;
                    var branchIndex = 0;

                    foreach (var branch in step.Branches)
                    {
                        branchIndex++;
                        var branchIdentifier = branch.IsDefault ? "default branch" : $"branch #{branchIndex}";

                        // Validate that non-default branches have conditions
                        if (!branch.IsDefault && branch.Condition == null)
                        {
                            result.Errors.Add($"Non-default {branchIdentifier} must have a condition");
                        }

                        // Validate that default branches don't have conditions
                        if (branch.IsDefault && branch.Condition != null)
                        {
                            result.Warnings.Add($"Default {branchIdentifier} should not have a condition (it will be ignored)");
                        }

                        // Check for multiple default branches
                        if (branch.IsDefault)
                        {
                            if (hasDefaultBranch)
                            {
                                result.Errors.Add("Step can only have one default branch");
                            }
                            hasDefaultBranch = true;
                        }

                        // Validate sub-steps within the branch
                        if (branch.SubSteps?.Any() == true)
                        {
                            foreach (var subStep in branch.SubSteps)
                            {
                                if (string.IsNullOrEmpty(subStep.Name))
                                {
                                    result.Errors.Add($"Sub-step in {branchIdentifier} must have a name");
                                }

                                if (subStep.ExecuteAsync == null)
                                {
                                    result.Errors.Add($"Sub-step '{subStep.Name}' in {branchIdentifier} must have an ExecuteAsync function");
                                }
                            }
                        }
                        else
                        {
                            result.Warnings.Add($"{branchIdentifier} has no sub-steps defined");
                        }
                    }

                    // Warn if no default branch exists (could lead to no execution)
                    if (!hasDefaultBranch)
                    {
                        result.Warnings.Add("Step has conditional branches but no default branch - execution may be skipped if no conditions match");
                    }
                }

                result.IsValid = !result.Errors.Any();
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating step {StepName}", step.Name);
                result.Errors.Add($"Step validation error: {ex.Message}");
                result.IsValid = false;
                return result;
            }
        }

        public async Task<DataValidationResult> ValidateFlowDataAsync(object data, Type flowType)
        {
            var result = new DataValidationResult();

            try
            {
                if (data == null)
                {
                    result.IsValid = true;
                    return result;
                }

                // Serialize and deserialize to validate JSON structure
                try
                {
                    var json = JsonSerializer.Serialize(data);
                    var deserializedData = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                    result.SanitizedData = deserializedData ?? new Dictionary<string, object>();
                }
                catch (JsonException ex)
                {
                    result.Errors.Add($"Invalid JSON structure: {ex.Message}");
                    result.IsValid = false;
                    return result;
                }

                // Validate data annotations if data implements validation
                if (data is IValidatableObject validatable)
                {
                    var validationContext = new ValidationContext(data);
                    var validationResults = validatable.Validate(validationContext);

                    foreach (var validationResult in validationResults)
                    {
                        if (validationResult != ValidationResult.Success)
                        {
                            result.Errors.Add(validationResult.ErrorMessage ?? "Unknown validation error");
                        }
                    }
                }

                // Basic data validation rules
                if (data is Dictionary<string, object> dict)
                {
                    foreach (var kvp in dict)
                    {
                        // Check for potential security issues
                        if (kvp.Key.ToLower().Contains("password") || kvp.Key.ToLower().Contains("secret"))
                        {
                            result.Warnings.Add($"Potentially sensitive field detected: {kvp.Key}");
                        }

                        // Check for SQL injection patterns (basic check)
                        if (kvp.Value is string strValue)
                        {
                            if (strValue.Contains("'") || strValue.ToLower().Contains("union") ||
                                strValue.ToLower().Contains("select") || strValue.ToLower().Contains("drop"))
                            {
                                result.Warnings.Add($"Potential SQL injection pattern in field: {kvp.Key}");
                            }
                        }
                    }
                }

                result.IsValid = !result.Errors.Any();
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating flow data for type {FlowType}", flowType.Name);
                result.Errors.Add($"Data validation error: {ex.Message}");
                result.IsValid = false;
                return result;
            }
        }

        public async Task<DependencyValidationResult> ValidateDependenciesAsync(FlowDefinition flow)
        {
            var result = new DependencyValidationResult();

            try
            {
                if (flow.Steps == null || !flow.Steps.Any())
                {
                    result.IsValid = true;
                    return result;
                }

                var stepNames = flow.Steps.Select(s => s.Name).ToHashSet();

                // Check for missing dependencies
                foreach (var step in flow.Steps)
                {
                    if (step.Dependencies?.Any() == true)
                    {
                        foreach (var dependency in step.Dependencies)
                        {
                            if (!stepNames.Contains(dependency))
                            {
                                result.MissingDependencies.Add($"Step '{step.Name}' depends on missing step '{dependency}'");
                            }
                        }
                    }
                }

                // Check for circular dependencies
                var visited = new HashSet<string>();
                var recursionStack = new HashSet<string>();

                foreach (var step in flow.Steps)
                {
                    if (!visited.Contains(step.Name))
                    {
                        if (HasCircularDependency(step.Name, flow.Steps, visited, recursionStack))
                        {
                            result.CircularDependencies.Add($"Circular dependency detected involving step '{step.Name}'");
                        }
                    }
                }

                result.Errors.AddRange(result.MissingDependencies);
                result.Errors.AddRange(result.CircularDependencies);
                result.IsValid = !result.Errors.Any();

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating dependencies for flow {FlowId}", flow.FlowId);
                result.Errors.Add($"Dependency validation error: {ex.Message}");
                result.IsValid = false;
                return result;
            }
        }

        private bool HasCircularDependency(string stepName, List<FlowStep> steps, HashSet<string> visited, HashSet<string> recursionStack)
        {
            visited.Add(stepName);
            recursionStack.Add(stepName);

            var currentStep = steps.FirstOrDefault(s => s.Name == stepName);
            if (currentStep?.Dependencies != null)
            {
                foreach (var dependency in currentStep.Dependencies)
                {
                    if (!visited.Contains(dependency))
                    {
                        if (HasCircularDependency(dependency, steps, visited, recursionStack))
                        {
                            return true;
                        }
                    }
                    else if (recursionStack.Contains(dependency))
                    {
                        return true;
                    }
                }
            }

            recursionStack.Remove(stepName);
            return false;
        }
    }
}