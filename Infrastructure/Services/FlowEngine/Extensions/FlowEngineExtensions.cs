using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Models;

namespace Infrastructure.Services.FlowEngine.Extensions
{
    /// <summary>
    /// Extension methods for FlowEngine to provide additional helper functionality
    /// </summary>
    public static class FlowEngineExtensions
    {
        #region Bulk Operations

        /// <summary>
        /// Start multiple flows in parallel and wait for all to complete
        /// </summary>
        public static async Task<List<FlowResult<TFlow>>> StartBatch<TFlow>(
            this Type _,
            IEnumerable<Dictionary<string, object>> dataItems,
            string userId = null,
            int maxConcurrency = 10)
            where TFlow : FlowDefinition, new()
        {
            var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
            var tasks = dataItems.Select(async data =>
            {
                await semaphore.WaitAsync();
                try
                {
                    return await Engine.FlowEngine.Start<TFlow>(data, userId);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            return (await Task.WhenAll(tasks)).ToList();
        }

        /// <summary>
        /// Resume multiple paused flows by their IDs
        /// </summary>
        public static async Task<Dictionary<Guid, bool>> ResumeBatch(
            this Type _,
            IEnumerable<Guid> flowIds,
            string userId,
            string reason = null)
        {
            var results = new Dictionary<Guid, bool>();

            var tasks = flowIds.Select(async flowId =>
            {
                try
                {
                    var success = await Engine.FlowEngine.ResumeManually(flowId, userId, reason);
                    return (flowId, success);
                }
                catch
                {
                    return (flowId, false);
                }
            });

            var completedTasks = await Task.WhenAll(tasks);
            foreach (var (flowId, success) in completedTasks)
            {
                results[flowId] = success;
            }

            return results;
        }

        #endregion

        #region Query Helpers

        /// <summary>
        /// Get all flows by status with optional filtering
        /// </summary>
        public static async Task<PagedResult<FlowSummary>> GetFlowsByStatus(
            this Type _,
            FlowStatus status,
            string userId = null,
            DateTime? createdAfter = null,
            int pageSize = 50)
        {
            var query = new FlowQuery
            {
                Status = status,
                UserId = userId,
                CreatedAfter = createdAfter,
                PageSize = pageSize
            };

            return await Engine.FlowEngine.Query(query);
        }

        /// <summary>
        /// Get all running flows
        /// </summary>
        public static async Task<PagedResult<FlowSummary>> GetRunningFlows(this Type _, string userId = null)
        {
            return await Engine.FlowEngine.GetFlowsByStatus(FlowStatus.Running, userId);
        }

        /// <summary>
        /// Get all failed flows from the last N days
        /// </summary>
        public static async Task<PagedResult<FlowSummary>> GetRecentFailures(this Type _, int days = 7, string userId = null)
        {
            return await Engine.FlowEngine.GetFlowsByStatus(
                FlowStatus.Failed,
                userId,
                DateTime.UtcNow.AddDays(-days));
        }

        /// <summary>
        /// Get flows paused for a specific reason
        /// </summary>
        public static async Task<PagedResult<FlowSummary>> GetFlowsPausedFor(this Type _, PauseReason reason)
        {
            var query = new FlowQuery
            {
                Status = FlowStatus.Paused,
                PauseReason = reason
            };

            return await Engine.FlowEngine.Query(query);
        }

        #endregion

        #region Statistics & Monitoring

        /// <summary>
        /// Get flow execution statistics
        /// </summary>
        public static async Task<FlowStatistics> GetStatistics(this Type _, TimeSpan? period = null)
        {
            var since = period.HasValue ? DateTime.UtcNow.Subtract(period.Value) : DateTime.UtcNow.AddDays(-30);

            var allFlows = await Engine.FlowEngine.Query(new FlowQuery
            {
                CreatedAfter = since,
                PageSize = 1000 // Get a large sample
            });

            var stats = new FlowStatistics
            {
                Period = period ?? TimeSpan.FromDays(30),
                TotalFlows = allFlows.TotalCount
            };

            // You'd typically get these from your persistence layer
            // This is a simplified example
            stats.CompletedFlows = allFlows.Items.Count(f => f.Status == FlowStatus.Completed);
            stats.FailedFlows = allFlows.Items.Count(f => f.Status == FlowStatus.Failed);
            stats.RunningFlows = allFlows.Items.Count(f => f.Status == FlowStatus.Running);
            stats.PausedFlows = allFlows.Items.Count(f => f.Status == FlowStatus.Paused);

            if (stats.TotalFlows > 0)
            {
                stats.SuccessRate = (double)stats.CompletedFlows / stats.TotalFlows * 100;
            }

            return stats;
        }

        /// <summary>
        /// Get health check information
        /// </summary>
        public static async Task<FlowEngineHealth> GetHealth(this Type _)
        {
            var runningFlows = await Engine.FlowEngine.GetRunningFlows();
            var pausedFlows = await Engine.FlowEngine.GetPausedFlows();
            var recentFailures = await Engine.FlowEngine.GetRecentFailures(1); // Last 24 hours

            return new FlowEngineHealth
            {
                IsHealthy = recentFailures.TotalCount < 10, // Arbitrary threshold
                RunningFlowsCount = runningFlows.TotalCount,
                PausedFlowsCount = pausedFlows.TotalCount,
                RecentFailuresCount = recentFailures.TotalCount,
                CheckedAt = DateTime.UtcNow
            };
        }

        #endregion

        #region Event Publishing Helpers

        /// <summary>
        /// Publish balance update event for crypto flows
        /// </summary>
        public static async Task PublishBalanceUpdate(this Type _, string currency, decimal newBalance, string exchange = null)
        {
            await Engine.FlowEngine.PublishEvent("BalanceTopUp", new
            {
                Currency = currency,
                Amount = newBalance,
                Exchange = exchange,
                Timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Publish compliance approval event
        /// </summary>
        public static async Task PublishComplianceApproval(this Type _, string tradeId, bool approved, string reviewedBy, string reason = null)
        {
            await Engine.FlowEngine.PublishEvent("ComplianceApproval", new
            {
                TradeId = tradeId,
                Approved = approved,
                ReviewedBy = reviewedBy,
                Reason = reason,
                Timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Publish maintenance window events
        /// </summary>
        public static async Task PublishMaintenanceWindow(this Type _, bool isStarting, string reason = null)
        {
            var eventType = isStarting ? "MaintenanceStart" : "MaintenanceEnd";
            await Engine.FlowEngine.PublishEvent(eventType, new
            {
                Reason = reason,
                Timestamp = DateTime.UtcNow
            });
        }

        #endregion

        #region Maintenance Operations

        /// <summary>
        /// Cancel all flows matching criteria
        /// </summary>
        public static async Task<int> CancelFlowsWhere(
            this Type _,
            Func<FlowSummary, bool> predicate,
            string reason = "Bulk cancellation")
        {
            var allFlows = await Engine.FlowEngine.Query(new FlowQuery { PageSize = 1000 });
            var flowsToCancel = allFlows.Items.Where(predicate).ToList();

            var cancelTasks = flowsToCancel.Select(flow => Engine.FlowEngine.Cancel(flow.FlowId, reason));
            var results = await Task.WhenAll(cancelTasks);

            return results.Count(success => success);
        }

        /// <summary>
        /// Emergency stop - cancel all running flows
        /// </summary>
        public static async Task<int> EmergencyStop(this Type _, string reason = "Emergency stop initiated")
        {
            return await Engine.FlowEngine.CancelFlowsWhere(
                flow => flow.Status == FlowStatus.Running || flow.Status == FlowStatus.Paused,
                reason);
        }

        /// <summary>
        /// Resume all flows paused for a specific reason
        /// </summary>
        public static async Task<int> ResumeAllPausedFor(this Type _, PauseReason pauseReason, string userId, string reason = null)
        {
            var pausedFlows = await Engine.FlowEngine.GetFlowsPausedFor(pauseReason);
            var resumeTasks = pausedFlows.Items.Select(flow => Engine.FlowEngine.ResumeManually(flow.FlowId, userId, reason));
            var results = await Task.WhenAll(resumeTasks);

            return results.Count(success => success);
        }

        #endregion
    }

    #region Extension Data Models

    /// <summary>
    /// Flow execution statistics
    /// </summary>
    public class FlowStatistics
    {
        public TimeSpan Period { get; set; }
        public int TotalFlows { get; set; }
        public int CompletedFlows { get; set; }
        public int FailedFlows { get; set; }
        public int RunningFlows { get; set; }
        public int PausedFlows { get; set; }
        public double SuccessRate { get; set; }
        public TimeSpan AverageExecutionTime { get; set; }
    }

    /// <summary>
    /// Flow engine health status
    /// </summary>
    public class FlowEngineHealth
    {
        public bool IsHealthy { get; set; }
        public int RunningFlowsCount { get; set; }
        public int PausedFlowsCount { get; set; }
        public int RecentFailuresCount { get; set; }
        public DateTime CheckedAt { get; set; }
        public string Status => IsHealthy ? "Healthy" : "Degraded";
    }

    #endregion
}

// ========================================
// USAGE EXAMPLES
// ========================================

/*
// Bulk operations
var results = await FlowEngine.StartBatch<PaymentFlow>(paymentDataList, "user123", maxConcurrency: 5);
var resumeResults = await FlowEngine.ResumeBatch(flowIds, "admin", "Manual intervention");

// Query helpers
var runningFlows = await FlowEngine.GetRunningFlows("user123");
var failedFlows = await FlowEngine.GetRecentFailures(days: 3);
var balanceIssues = await FlowEngine.GetFlowsPausedFor(PauseReason.InsufficientResources);

// Statistics and monitoring
var stats = await FlowEngine.GetStatistics(TimeSpan.FromDays(7));
var health = await FlowEngine.GetHealth();
Console.WriteLine($"Success rate: {stats.SuccessRate:F1}%, Health: {health.Status}");

// Event publishing shortcuts
await FlowEngine.PublishBalanceUpdate("BTC", 5000.00m, "Binance");
await FlowEngine.PublishComplianceApproval("trade_123", approved: true, "compliance_officer");
await FlowEngine.PublishMaintenanceWindow(isStarting: true, "Scheduled maintenance");

// Flow templates
var paymentResult = await FlowEngine.StartPaymentFlow<CryptoPaymentFlow>(
    amount: 1000.00m, 
    currency: "BTC", 
    userId: "user123");

var apiResult = await FlowEngine.StartApiFlow<DataSyncFlow>(
    endpoint: "/api/users", 
    requestData: new { page = 1 });

// Maintenance operations
var cancelledCount = await FlowEngine.CancelFlowsWhere(
    flow => flow.CreatedAt < DateTime.UtcNow.AddDays(-30),
    "Cleanup old flows");

var emergencyStopCount = await FlowEngine.EmergencyStop("System maintenance");
var resumedCount = await FlowEngine.ResumeAllPausedFor(
    PauseReason.InsufficientResources, 
    "admin", 
    "Balance issues resolved");
*/

