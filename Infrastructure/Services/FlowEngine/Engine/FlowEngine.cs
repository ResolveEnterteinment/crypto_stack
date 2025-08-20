using Infrastructure.Services.FlowEngine.Configuration;
using Infrastructure.Services.FlowEngine.Configuration.Options;
using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace Infrastructure.Services.FlowEngine.Engine
{
    /// <summary>
    /// Ultimate Flow Orchestration Engine - Static API for maximum developer productivity
    /// Integrates: Middleware Pipeline, Self-Executing Steps, Dynamic Branching, 
    /// Complete Persistence, Step-Triggered Flows, Loophole Protection
    /// </summary>
    public static class FlowEngine
    {
        private static IServiceProvider _serviceProvider;
        private static FlowEngineConfiguration _config;
        private static ILogger _logger; // Fixed: Removed generic type parameter
        private static readonly object _lockObject = new object();
        private static volatile bool _isInitialized = false;
        private static readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();

        #region Configuration & Setup

        /// <summary>
        /// Dead-simple setup - one method call to rule them all
        /// </summary>
        public static FlowEngineBuilder Configure()
        {
            return new FlowEngineBuilder();
        }

        /// <summary>
        /// Initialize the FlowEngine (called internally by builder)
        /// </summary>
        internal static void Initialize(IServiceProvider serviceProvider, FlowEngineConfiguration config)
        {
            lock (_lockObject)
            {
                _serviceProvider = serviceProvider;
                _config = config;
                _logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("FlowEngine"); // Fixed
                _isInitialized = true;
            }
        }

        private static void EnsureInitialized()
        {
            if (_isInitialized) return;
            
            _lock.EnterReadLock();
            try
            {
                if (!_isInitialized)
                {
                    _lock.ExitReadLock();
                    _lock.EnterWriteLock();
                    try
                    {
                        if (!_isInitialized)
                            throw new InvalidOperationException("FlowEngine not initialized. Call FlowEngine.Configure().Build() first.");
                    }
                    finally
                    {
                        _lock.ExitWriteLock();
                    }
                    return;
                }
            }
            finally
            {
                if (_lock.IsReadLockHeld)
                    _lock.ExitReadLock();
            }
        }

        #endregion

        #region Pause/Resume Flow Management

        /// <summary>
        /// Resume a paused flow manually (admin/user intervention)
        /// </summary>
        public static async Task<bool> ResumeManually(string flowId, string userId, string reason = null)
        {
            EnsureInitialized();
            var service = _serviceProvider.GetRequiredService<IFlowEngineService>();
            return await service.ResumeManuallyAsync(flowId, userId, reason);
        }

        /// <summary>
        /// Resume a paused flow via event trigger
        /// </summary>
        public static async Task<bool> ResumeByEvent(string flowId, string eventType, object eventData = null)
        {
            EnsureInitialized();
            var service = _serviceProvider.GetRequiredService<IFlowEngineService>();
            return await service.ResumeByEventAsync(flowId, eventType, eventData);
        }

        /// <summary>
        /// Get all currently paused flows
        /// </summary>
        public static async Task<PagedResult<FlowSummary>> GetPausedFlows(FlowQuery query = null)
        {
            EnsureInitialized();
            var service = _serviceProvider.GetRequiredService<IFlowEngineService>();
            return await service.GetPausedFlowsAsync(query);
        }

        /// <summary>
        /// Set resume condition for a paused flow
        /// </summary>
        public static async Task<bool> SetResumeCondition(string flowId, ResumeCondition condition)
        {
            EnsureInitialized();
            var service = _serviceProvider.GetRequiredService<IFlowEngineService>();
            return await service.SetResumeConditionAsync(flowId, condition);
        }

        /// <summary>
        /// Publish an event that may trigger flow resumes
        /// </summary>
        public static async Task PublishEvent(string eventType, object eventData, string correlationId = null)
        {
            EnsureInitialized();
            var service = _serviceProvider.GetRequiredService<IFlowEngineService>();
            await service.PublishEventAsync(eventType, eventData, correlationId);
        }

        /// <summary>
        /// Check and auto-resume flows based on their conditions
        /// </summary>
        public static async Task<int> CheckAutoResumeConditions()
        {
            EnsureInitialized();
            var service = _serviceProvider.GetRequiredService<IFlowEngineService>();
            return await service.CheckAutoResumeConditionsAsync();
        }

        #endregion

        #region Core Flow Execution API

        /// <summary>
        /// Start a new flow - Ultra simple syntax
        /// </summary>
        /// <typeparam name="TFlow">Flow type</typeparam>
        /// <param name="initialData">Initial flow data</param>
        /// <param name="userId">User executing the flow (for security)</param>
        /// <param name="correlationId">Optional correlation ID for tracking</param>
        /// <returns>Flow execution result</returns>
        public static async Task<FlowResult<TFlow>> Start<TFlow>(
            object initialData = null,
            string userId = null,
            string correlationId = null,
            CancellationToken cancellationToken = default)
            where TFlow : FlowDefinition, new()
        {
            EnsureInitialized();
            var service = _serviceProvider.GetRequiredService<IFlowEngineService>();
            return await service.StartAsync<TFlow>(initialData, userId, correlationId, cancellationToken);
        }

        /// <summary>
        /// Resume a previously started flow
        /// </summary>
        public static async Task<FlowResult<TFlow>> Resume<TFlow>(
            string flowId,
            CancellationToken cancellationToken = default)
            where TFlow : FlowDefinition, new()
        {
            EnsureInitialized();
            var service = _serviceProvider.GetRequiredService<IFlowEngineService>();
            return await service.ResumeAsync<TFlow>(flowId, cancellationToken);
        }

        /// <summary>
        /// Quick fire-and-forget flow execution
        /// </summary>
        public static async Task Fire<TFlow>(
            object initialData = null,
            string userId = null)
            where TFlow : FlowDefinition, new()
        {
            EnsureInitialized();
            var service = _serviceProvider.GetRequiredService<IFlowEngineService>();
            await service.FireAsync<TFlow>(initialData, userId);
        }

        /// <summary>
        /// Trigger a flow from another flow step
        /// </summary>
        public static async Task<FlowResult<TTriggered>> Trigger<TTriggered>(
            FlowContext context,
            object triggerData = null)
            where TTriggered : FlowDefinition, new()
        {
            EnsureInitialized();
            var service = _serviceProvider.GetRequiredService<IFlowEngineService>();
            return await service.TriggerAsync<TTriggered>(context, triggerData);
        }

        #endregion

        #region Flow Management API

        /// <summary>
        /// Get flow status and progress
        /// </summary>
        public static async Task<FlowStatus> GetStatus(string flowId)
        {
            EnsureInitialized();
            var service = _serviceProvider.GetRequiredService<IFlowEngineService>();
            return await service.GetStatusAsync(flowId);
        }

        /// <summary>
        /// Cancel a running flow
        /// </summary>
        public static async Task<bool> Cancel(string flowId, string reason = null)
        {
            EnsureInitialized();
            var service = _serviceProvider.GetRequiredService<IFlowEngineService>();
            return await service.CancelAsync(flowId, reason);
        }

        /// <summary>
        /// Get flow execution timeline
        /// </summary>
        public static async Task<FlowTimeline> GetTimeline(string flowId)
        {
            EnsureInitialized();
            var service = _serviceProvider.GetRequiredService<IFlowEngineService>();
            return await service.GetTimelineAsync(flowId);
        }

        /// <summary>
        /// Query flows with filters
        /// </summary>
        public static async Task<PagedResult<FlowSummary>> Query(FlowQuery query)
        {
            EnsureInitialized();
            var service = _serviceProvider.GetRequiredService<IFlowEngineService>();
            return await service.QueryAsync(query);
        }

        /// <summary>
        /// Get all flows by status with optional filtering - Extension method support
        /// </summary>
        public static async Task<PagedResult<FlowSummary>> GetFlowsByStatus(
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

            return await Query(query);
        }

        /// <summary>
        /// Get all running flows - Extension method support
        /// </summary>
        public static async Task<PagedResult<FlowSummary>> GetRunningFlows(string userId = null)
        {
            return await GetFlowsByStatus(FlowStatus.Running, userId);
        }

        /// <summary>
        /// Get all failed flows from the last N days - Extension method support
        /// </summary>
        public static async Task<PagedResult<FlowSummary>> GetRecentFailures(int days = 7, string userId = null)
        {
            return await GetFlowsByStatus(
                FlowStatus.Failed,
                userId,
                DateTime.UtcNow.AddDays(-days));
        }

        /// <summary>
        /// Get flows paused for a specific reason - Extension method support
        /// </summary>
        public static async Task<PagedResult<FlowSummary>> GetFlowsPausedFor(PauseReason reason)
        {
            var query = new FlowQuery
            {
                Status = FlowStatus.Paused,
                PauseReason = reason
            };

            return await Query(query);
        }

        /// <summary>
        /// Cancel all flows matching criteria - Extension method support
        /// </summary>
        public static async Task<int> CancelFlowsWhere(
            Func<FlowSummary, bool> predicate,
            string reason = "Bulk cancellation")
        {
            var allFlows = await Query(new FlowQuery { PageSize = 1000 });
            var flowsToCancel = allFlows.Items.Where(predicate).ToList();

            var cancelTasks = flowsToCancel.Select(flow => Cancel(flow.FlowId, reason));
            var results = await Task.WhenAll(cancelTasks);

            return results.Count(success => success);
        }

        #endregion

        #region Recovery & Maintenance

        /// <summary>
        /// Recover all crashed flows
        /// </summary>
        public static async Task<RecoveryResult> RecoverCrashedFlows()
        {
            EnsureInitialized();
            var service = _serviceProvider.GetRequiredService<IFlowEngineService>();
            return await service.RecoverCrashedFlowsAsync();
        }

        /// <summary>
        /// Cleanup completed flows older than specified time
        /// </summary>
        public static async Task<int> Cleanup(TimeSpan olderThan)
        {
            EnsureInitialized();
            var service = _serviceProvider.GetRequiredService<IFlowEngineService>();
            return await service.CleanupAsync(olderThan);
        }

        #endregion
    }
}