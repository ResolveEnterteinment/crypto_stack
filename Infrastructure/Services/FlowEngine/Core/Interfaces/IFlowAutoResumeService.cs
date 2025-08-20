namespace Infrastructure.Services.FlowEngine.Core.Interfaces
{
    public interface IFlowAutoResumeService
    {
        Task<int> CheckAndResumeFlowsAsync();
        Task StartBackgroundCheckingAsync();
        Task StopBackgroundCheckingAsync();
    }
}
