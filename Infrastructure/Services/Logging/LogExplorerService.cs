using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Domain.Constants.Logging;
using Domain.DTOs;
using Domain.Models.Logging;
using MongoDB.Driver;

namespace Infrastructure.Services.Logging
{
    public class LogExplorerService : ILogExplorerService
    {
        private readonly ICrudRepository<TraceLogData> Repository;

        public LogExplorerService(ICrudRepository<TraceLogData> repository)
        {
            Repository = repository;
        }

        public async Task<ResultWrapper<CrudResult>> Resolve(Guid id, string message, Guid resolvedBy)
        {
            try
            {
                var updateFields = new { ResolutionComment = message, ResolvedBy = resolvedBy, ResolvedAt = DateTime.UtcNow };

                return await Repository.UpdateAsync(id, new
                {
                    ResolutionComment = message,
                    ResolutionStatus = ResolutionStatus.Reconciled,
                    RequiresResolution = false,
                    ResolvedBy = resolvedBy,
                    ResolvedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return ResultWrapper<CrudResult>.FromException(ex);
            }
        }

        public async Task<List<TraceLogNodeData>> GetTraceTreeAsync(Guid? rootId = null)
        {
            var allLogs = await Repository.GetAllAsync();
            var allCorrelations = allLogs.Select(a => a.CorrelationId).ToList();

            var rootFilter = new FilterDefinitionBuilder<TraceLogData>()
                .Where(l => l.ParentCorrelationId == null || !allCorrelations.Contains(l.ParentCorrelationId));
            var rootLogs = await Repository.GetAllAsync(rootFilter);
            var rootNodes = rootLogs.Select(l => new TraceLogNodeData { Log = l }).ToList();

            var childFilter = new FilterDefinitionBuilder<TraceLogData>()
                .Where(l => l.ParentCorrelationId != null && allCorrelations.Contains(l.ParentCorrelationId));
            var childLogs = await Repository.GetAllAsync(childFilter);

            rootNodes.ForEach(root => MarchChildTree(root, childLogs));

            return rootNodes;
        }

        private static void MarchChildTree(TraceLogNodeData parent, List<TraceLogData> childLogs)
        {
            foreach (var log in childLogs.ToList())
            {
                if (log.ParentCorrelationId == parent.Log.CorrelationId)
                {
                    var childNode = new TraceLogNodeData { Log = log };
                    parent.Children.Add(childNode);
                    childLogs.Remove(log);
                    MarchChildTree(childNode, childLogs);
                }
            }
        }
    }
}
