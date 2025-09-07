using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Engine;

namespace Infrastructure.Services.FlowEngine.Core.Interfaces
{
    /// <summary>
    /// Interface for flow validation services
    /// </summary>
    public interface IFlowValidation
    {
        /// <summary>
        /// Validates a flow definition and its data
        /// </summary>
        /// <param name="flow">The flow to validate</param>
        /// <returns>Validation result</returns>
        Task<FlowValidationResult> ValidateFlowAsync(Flow flow);

        /// <summary>
        /// Validates a specific step within a flow
        /// </summary>
        /// <param name="step">The step to validate</param>
        /// <param name="flow">The parent flow context</param>
        /// <returns>Validation result</returns>
        Task<StepValidationResult> ValidateStepAsync(FlowStep step, Flow flow);

        /// <summary>
        /// Validates flow data structure and content
        /// </summary>
        /// <param name="data">Data to validate</param>
        /// <param name="flowType">Type of flow the data belongs to</param>
        /// <returns>Validation result</returns>
        Task<DataValidationResult> ValidateFlowDataAsync(object data, Type flowType);

        /// <summary>
        /// Validates step dependencies and execution order
        /// </summary>
        /// <param name="flow">The flow to validate dependencies for</param>
        /// <returns>Validation result</returns>
        Task<DependencyValidationResult> ValidateDependenciesAsync(Flow flow);
    }

    /// <summary>
    /// Result of flow validation
    /// </summary>
    public class FlowValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public Guid FlowId { get; set; }
        public string FlowType { get; set; }
    }

    /// <summary>
    /// Result of step validation
    /// </summary>
    public class StepValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public string StepName { get; set; }
    }

    /// <summary>
    /// Result of data validation
    /// </summary>
    public class DataValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public Dictionary<string, object> SanitizedData { get; set; } = new();
    }

    /// <summary>
    /// Result of dependency validation
    /// </summary>
    public class DependencyValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> CircularDependencies { get; set; } = new();
        public List<string> MissingDependencies { get; set; } = new();
    }
}