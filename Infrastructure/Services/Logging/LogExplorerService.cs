using Application.Interfaces.Base;
using Domain.Constants;
using Domain.Constants.Logging;
using Domain.DTOs;
using Domain.Models.Logging;
using MongoDB.Driver;

namespace Infrastructure.Services.Logging
{
    public class LogExplorerService : ILogExplorerService
    {
        private readonly ICrudRepository<TraceLogData> _repository;

        public LogExplorerService(ICrudRepository<TraceLogData> repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }

        public async Task<ResultWrapper<CrudResult>> Resolve(Guid id, string message, Guid resolvedBy)
        {
            try
            {
                var updateFields = new { ResolutionComment = message, ResolvedBy = resolvedBy, ResolvedAt = DateTime.UtcNow };

                return await _repository.UpdateAsync(id, new
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

        // Keep the original method for backward compatibility
        public async Task<List<TraceLogNodeData>> GetTraceTreeAsync(Guid? rootId = null)
        {
            var result = await GetTraceTreePaginatedAsync(page: 1, pageSize: int.MaxValue, rootId: rootId);
            return result.IsSuccess ? result.Data.Items.ToList() : new List<TraceLogNodeData>();
        }

        public async Task<ResultWrapper<PaginatedResult<TraceLogNodeData>>> GetTraceTreePaginatedAsync(int page = 1, int pageSize = 20, int filterLevel = 0, Guid? rootId = null)
        {
            try
            {
                // Validate pagination parameters
                page = Math.Max(1, page);
                pageSize = Math.Clamp(pageSize, 1, 100);

                // Build filter for root nodes
                var filterBuilder = Builders<TraceLogData>.Filter;
                FilterDefinition<TraceLogData> rootFilter;

                if (rootId.HasValue)
                {
                    // If rootId is specified, get that specific root and its tree
                    rootFilter = filterBuilder.Eq(l => l.CorrelationId, rootId.Value);
                }
                else
                {
                    // Get all root nodes (those without parent correlation or parent not in the system)
                    var allCorrelations = await GetAllCorrelationIds();
                    rootFilter = filterBuilder.Or(
                        filterBuilder.Eq(l => l.ParentCorrelationId, null),
                        filterBuilder.Not(filterBuilder.In(l => l.ParentCorrelationId, allCorrelations))
                    );
                }

                // Get paginated root logs
                var sortDefinition = Builders<TraceLogData>.Sort.Descending(l => l.CreatedAt);
                var rootLogsResult = await _repository.GetPaginatedAsync(rootFilter, sortDefinition, page, pageSize);

                if (rootLogsResult == null || rootLogsResult?.Items == null)
                {
                    return ResultWrapper<PaginatedResult<TraceLogNodeData>>.Failure(
                        FailureReason.DatabaseError,
                        "Failed to retrieve root logs");
                }

                var rootLogs = rootLogsResult.Items.ToList();
                var rootNodes = rootLogs.Select(log => new TraceLogNodeData { Log = log }).ToList();

                // If we have root nodes, build their children trees
                if (rootNodes.Any())
                {
                    await BuildChildrenTreesAsync(rootNodes);
                }

                // Filter out root nodes that don't contain any nodes with level >= filterLevel
                if (filterLevel > 0)
                {
                    rootNodes = rootNodes.Where(rootNode => ContainsLogWithMinLevel(rootNode, (LogLevel)filterLevel)).ToList();
                }

                // Create the paginated result
                var paginatedResult = new PaginatedResult<TraceLogNodeData>
                {
                    Items = rootNodes,
                    Page = rootLogsResult.Page,
                    PageSize = rootLogsResult.PageSize,
                    TotalCount = rootNodes.Count // Update count to reflect filtered results
                };

                return ResultWrapper<PaginatedResult<TraceLogNodeData>>.Success(paginatedResult);
            }
            catch (Exception ex)
            {
                return ResultWrapper<PaginatedResult<TraceLogNodeData>>.FromException(ex);
            }
        }

        /// <summary>
        /// Purges logs with level equal to or below the specified maximum level
        /// </summary>
        /// <param name="maxLevel">The maximum log level to purge (logs with level <= maxLevel will be deleted)</param>
        /// <returns>Result containing the number of deleted logs</returns>
        public async Task<ResultWrapper<CrudResult>> PurgeLogsAsync(int maxLevel)
        {
            try
            {
                // Validate the log level parameter
                if (maxLevel < (int)LogLevel.Trace || maxLevel > (int)LogLevel.Critical)
                {
                    return ResultWrapper<CrudResult>.ValidationError(
                        new Dictionary<string, string[]>
                        {
                            ["maxLevel"] = new[] { $"Log level must be between {(int)LogLevel.Trace} (Trace) and {(int)LogLevel.Critical} (Critical)" }
                        },
                        "Invalid log level specified");
                }

                // Build filter to select logs with level <= maxLevel
                var filterBuilder = Builders<TraceLogData>.Filter;
                var purgeFilter = filterBuilder.Lte(l => l.Level, (LogLevel)maxLevel);

                // Get count of logs to be deleted for logging purposes
                var countToDelete = await _repository.CountAsync(purgeFilter);

                if (countToDelete == 0)
                {
                    return ResultWrapper<CrudResult>.Success(
                        new CrudResult<TraceLogData>
                        {
                            Documents = new List<TraceLogData>(),
                            AffectedIds = new List<Guid>()
                        },
                        $"No logs found with level {(LogLevel)maxLevel} or below to purge");
                }

                // Execute the purge operation
                var deleteResult = await _repository.DeleteManyAsync(purgeFilter);

                if (deleteResult == null || !deleteResult.IsSuccess)
                {
                    return ResultWrapper<CrudResult>.Failure(
                        FailureReason.DatabaseError,
                        $"Failed to purge logs: {deleteResult?.ErrorMessage ?? "Delete operation returned null"}");
                }

                return ResultWrapper<CrudResult>.Success(
                    deleteResult,
                    $"Successfully purged {countToDelete} log(s) with level {(LogLevel)maxLevel} and below");
            }
            catch (Exception ex)
            {
                return ResultWrapper<CrudResult>.FromException(ex);
            }
        }

        /// <summary>
        /// Recursively checks if a tree node or any of its descendants has a log level equal to or above the specified minimum level
        /// </summary>
        /// <param name="node">The tree node to check</param>
        /// <param name="minLevel">The minimum log level to match</param>
        /// <returns>True if the node or any descendant has level >= minLevel</returns>
        private bool ContainsLogWithMinLevel(TraceLogNodeData node, LogLevel minLevel)
        {
            // Check the current node's level
            if (node.Log.Level >= minLevel)
            {
                return true;
            }

            // Recursively check all children
            return node.Children.Any(child => ContainsLogWithMinLevel(child, minLevel));
        }

        private async Task<List<Guid?>> GetAllCorrelationIds()
        {
            try
            {
                var allLogs = await _repository.GetAllAsync();
                return allLogs.Select(l => l.CorrelationId).ToList();
            }
            catch
            {
                // Fallback to empty list if we can't get all correlation IDs
                return new List<Guid?>();
            }
        }

        private async Task BuildChildrenTreesAsync(List<TraceLogNodeData> rootNodes)
        {
            // Get all correlation IDs from root nodes
            var rootCorrelationIds = rootNodes.Select(n => n.Log.CorrelationId).ToList();

            // Build a filter to get all descendants of these root nodes
            var childFilter = Builders<TraceLogData>.Filter.In(l => l.ParentCorrelationId, rootCorrelationIds);
            var childLogs = await _repository.GetAllAsync(childFilter);

            // Build the complete tree for each root node
            foreach (var rootNode in rootNodes)
            {
                await BuildChildTreeRecursiveAsync(rootNode, childLogs.ToList());
            }
        }

        private async Task BuildChildTreeRecursiveAsync(TraceLogNodeData parent, List<TraceLogData> allChildLogs)
        {
            // Find direct children of this parent
            var directChildren = allChildLogs
                .Where(log => log.ParentCorrelationId == parent.Log.CorrelationId)
                .ToList();

            foreach (var childLog in directChildren)
            {
                var childNode = new TraceLogNodeData { Log = childLog };
                parent.Children.Add(childNode);

                // Recursively build children for this child
                await BuildChildTreeRecursiveAsync(childNode, allChildLogs);
            }

            // If we need more children and they're not in our current list, fetch them
            var childCorrelationIds = parent.Children.Select(c => c.Log.CorrelationId).ToList();
            if (childCorrelationIds.Any())
            {
                var additionalChildFilter = Builders<TraceLogData>.Filter.In(l => l.ParentCorrelationId, childCorrelationIds);
                var additionalChildren = await _repository.GetAllAsync(additionalChildFilter);

                var newChildren = additionalChildren.Where(ac => !allChildLogs.Any(existing => existing.Id == ac.Id)).ToList();
                if (newChildren.Any())
                {
                    allChildLogs.AddRange(newChildren);

                    // Process the new children
                    foreach (var child in parent.Children.ToList())
                    {
                        await BuildChildTreeRecursiveAsync(child, allChildLogs);
                    }
                }
            }
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