using Infrastructure.Services.FlowEngine.Core.Models;

namespace Infrastructure.Services.FlowEngine.Core.Interfaces
{
    public interface IFlowRecovery
    {
        Task<RecoveryResult> RecoverAllAsync();
    }
}
