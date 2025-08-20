using Infrastructure.Services.FlowEngine.Models;
using Infrastructure.Services.FlowEngine.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Services.FlowEngine.Core
{
    /// <summary>
    /// Static facade for convenience - pure forwarding to DI-registered singleton
    /// ZERO static state - completely stateless thin wrapper
    /// </summary>
    public static class FlowEngine
    {
        private static IServiceProvider? _serviceProvider;

        /// <summary>
        /// Initialize with service provider (called by DI container startup)
        /// </summary>
        internal static void Initialize(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        private static IFlowEngineService Engine
        {
            get
            {
                if (_serviceProvider == null)
                {
                    throw new InvalidOperationException(
                        "FlowEngine not initialized. Call services.AddFlowEngine() in your DI container.");
                }

                return _serviceProvider.GetRequiredService<IFlowEngineService>();
            }
        }

#if DEBUG
        /// <summary>
        /// Reset for testing - only available in DEBUG builds
        /// </summary>
        public static void ResetForTesting()
        {
            _serviceProvider = null;
        }
#endif

        // Pure forwarding methods - no state held in static class
        public static Task<FlowResult<TFlow>> Start<TFlow, TInit>(
            TInit initialData,
            string userId = null,
            string correlationId = null,
            CancellationToken cancellationToken = default)
            where TFlow : FlowDefinition, new()
            where TInit : class, IValidatable
        {
            return Engine.StartAsync<TFlow, TInit>(initialData, userId, correlationId, cancellationToken);
        }

        public static Task<FlowResult<TFlow>> Resume<TFlow>(
            string flowId,
            CancellationToken cancellationToken = default)
            where TFlow : FlowDefinition, new()
        {
            return Engine.ResumeAsync<TFlow>(flowId, cancellationToken);
        }

        public static Task Fire<TFlow, TInit>(
            TInit initialData,
            string userId = null,
            CancellationToken cancellationToken = default)
            where TFlow : FlowDefinition, new()
            where TInit : class, IValidatable
        {
            return Engine.FireAsync<TFlow, TInit>(initialData, userId, cancellationToken);
        }

        public static Task<FlowStatus> GetStatus(string flowId, string requestingUserId, CancellationToken cancellationToken = default)
        {
            return Engine.GetStatusAsync(flowId, requestingUserId, cancellationToken);
        }

        public static Task<bool> Cancel(string flowId, string userId, string reason = null, CancellationToken cancellationToken = default)
        {
            return Engine.CancelAsync(flowId, userId, reason, cancellationToken);
        }

        public static Task<PagedResult<FlowSummary>> Query(FlowQuery query, string requestingUserId, CancellationToken cancellationToken = default)
        {
            return Engine.QueryAsync(query, requestingUserId, cancellationToken);
        }

        public static Task<bool> ResumeManually(string flowId, string userId, string reason = null, CancellationToken cancellationToken = default)
        {
            return Engine.ResumeManuallyAsync(flowId, userId, reason, cancellationToken);
        }

        public static Task PublishEvent(string eventType, object eventData, string publishedBy, string correlationId = null, CancellationToken cancellationToken = default)
        {
            return Engine.PublishEventAsync(eventType, eventData, publishedBy, correlationId, cancellationToken);
        }
    }
}
