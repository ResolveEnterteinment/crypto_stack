using Domain.DTOs;
using Domain.Models.Logging;

namespace Application.Interfaces.Logging
{
    public interface ILogExplorerService
    {
        Task<List<TraceLogNodeData>> GetTraceTreeAsync(Guid? rootId = null);
        Task<ResultWrapper<CrudResult>> Resolve(Guid id, string message, Guid resolvedBy);
    }
}
