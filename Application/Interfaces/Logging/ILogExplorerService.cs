using Domain.DTOs;
using Domain.Models.Logging;

public interface ILogExplorerService
{
    Task<List<TraceLogNodeData>> GetTraceTreeAsync(Guid? rootId = null);
    Task<ResultWrapper<PaginatedResult<TraceLogNodeData>>> GetTraceTreePaginatedAsync(int page = 1, int pageSize = 20, int filterLevel = 0, Guid? rootId = null);
    Task<ResultWrapper<CrudResult>> Resolve(Guid id, string message, Guid resolvedBy);
    Task<ResultWrapper<CrudResult>> PurgeLogsAsync(int maxLevel);
}