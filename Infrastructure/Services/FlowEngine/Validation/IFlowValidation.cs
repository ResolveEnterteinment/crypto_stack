namespace Infrastructure.Services.FlowEngine.Validation
{
    /// <summary>
    /// Flow validation service interface
    /// </summary>
    public interface IFlowValidation
    {
        Task<ValidationResult> ValidateAsync<T>(T data, CancellationToken cancellationToken) where T : IValidatable;
    }
}
