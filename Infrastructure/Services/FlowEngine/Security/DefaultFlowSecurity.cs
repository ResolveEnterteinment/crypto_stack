using Infrastructure.Services.FlowEngine.Configuration;
using Infrastructure.Services.FlowEngine.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Infrastructure.Services.FlowEngine.Security
{
    /// <summary>
    /// Default security service - DEVELOPMENT ONLY, extend for production
    /// </summary>
    public sealed class DefaultFlowSecurity : IFlowSecurity
    {
        private readonly ILogger<DefaultFlowSecurity> _logger;
        private readonly FlowEngineOptions _options;
        private readonly bool _isProduction;

        public DefaultFlowSecurity(ILogger<DefaultFlowSecurity> logger, IOptions<FlowEngineOptions> options, IConfiguration configuration)
        {
            _logger = logger;
            _options = options.Value;

            var environment = configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["Environment"] ?? "Production";
            _isProduction = !environment.Equals("Development", StringComparison.OrdinalIgnoreCase);

            // FIXED: Warn if using dev security in production
            if (_isProduction)
            {
                _logger.LogCritical("⚠️ DefaultFlowSecurity is being used in {Environment} environment! " +
                                  "This is insecure and should only be used in Development. " +
                                  "Please configure EnterpriseFlowSecurity for production.", environment);
            }
        }

        public async Task<bool> CanStartFlowAsync<TFlow>(string userId, object initialData, CancellationToken cancellationToken)
        {
            var canStart = !string.IsNullOrEmpty(userId);
            if (!canStart)
            {
                _logger.LogWarning("Flow start denied - no user ID provided for {FlowType}", typeof(TFlow).Name);
            }
            return canStart;
        }

        public async Task<bool> CanAccessFlowAsync(string flowId, string userId, CancellationToken cancellationToken)
        {
            var canAccess = !string.IsNullOrEmpty(userId);
            if (!canAccess)
            {
                _logger.LogWarning("Flow access denied - no user ID provided for flow {FlowId}", flowId);
            }
            return canAccess;
        }

        public async Task<bool> CanCancelFlowAsync(string flowId, string userId, CancellationToken cancellationToken)
        {
            return !string.IsNullOrEmpty(userId);
        }

        public async Task<bool> CanResumeFlowAsync(string flowId, string userId, CancellationToken cancellationToken)
        {
            return !string.IsNullOrEmpty(userId);
        }

        public async Task<FlowQuery> FilterQueryAsync(FlowQuery query, string userId, CancellationToken cancellationToken)
        {
            // Non-admin users only see their own flows
            if (!IsAdminUser(userId))
            {
                query = query with { UserId = userId };
            }
            return query;
        }

        public async Task<SignedEvent> SignEventAsync(string eventType, object eventData, string publishedBy, string correlationId, CancellationToken cancellationToken)
        {
            // Simple signing for development
            var eventJson = JsonSerializer.Serialize(eventData);
            var signature = ComputeSimpleSignature(eventType, eventJson, publishedBy, correlationId);

            return new SignedEvent
            {
                EventType = eventType,
                EventData = eventData,
                PublishedBy = publishedBy,
                CorrelationId = correlationId ?? Guid.NewGuid().ToString(),
                Signature = signature,
                SigningKeyId = "default-dev-key"
            };
        }

        public async Task<bool> ValidateEventSignatureAsync(SignedEvent signedEvent, CancellationToken cancellationToken)
        {
            if (!_options.Security.RequireSignedEvents)
            {
                return true;
            }

            // Check timestamp expiry
            if (DateTime.UtcNow - signedEvent.Timestamp > _options.Security.EventSignatureExpiry)
            {
                return false;
            }

            // Simple signature validation for development
            var eventJson = JsonSerializer.Serialize(signedEvent.EventData);
            var expectedSignature = ComputeSimpleSignature(
                signedEvent.EventType, eventJson, signedEvent.PublishedBy, signedEvent.CorrelationId);

            return signedEvent.Signature == expectedSignature;
        }

        public async Task<bool> CanResumeFromStepAsync(FlowDefinition flow, string stepName, string userId, CancellationToken cancellationToken)
        {
            // Permissive for development
            return await CanResumeFlowAsync(flow.FlowId, userId, cancellationToken).ConfigureAwait(false);
        }

        public async Task<bool> CanResumeWithEventAsync(FlowDefinition flow, SignedEvent signedEvent, CancellationToken cancellationToken)
        {
            // Basic validation in development
            return DateTime.UtcNow - signedEvent.Timestamp <= _options.Security.EventSignatureExpiry;
        }

        public async Task<bool> ValidateEventPayloadForStepAsync(FlowDefinition flow, string stepName, SignedEvent signedEvent, CancellationToken cancellationToken)
        {
            // Permissive validation for development
            return signedEvent.EventData != null;
        }

        public async Task<bool> CanExecuteStepAsync(FlowDefinition flow, string stepName, string userId, CancellationToken cancellationToken)
        {
            // Permissive for development
            return await CanAccessFlowAsync(flow.FlowId, userId, cancellationToken).ConfigureAwait(false);
        }

        private bool IsAdminUser(string userId)
        {
            return userId?.Contains("admin", StringComparison.OrdinalIgnoreCase) == true;
        }

        private string ComputeSimpleSignature(string eventType, string eventData, string publishedBy, string correlationId)
        {
            var payload = $"{eventType}:{eventData}:{publishedBy}:{correlationId}";
            var bytes = Encoding.UTF8.GetBytes(payload);
            var hash = SHA256.HashData(bytes);
            return Convert.ToBase64String(hash);
        }
    }
}
