using Infrastructure.Services.FlowEngine.Middleware;
using Infrastructure.Services.FlowEngine.Security;
using Infrastructure.Services.FlowEngine.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Services.FlowEngine.Configuration
{
    /// <summary>
    /// Fluent builder for Flow Engine configuration with security tracking
    /// </summary>
    public sealed class FlowEngineBuilder
    {
        private readonly IServiceCollection _services;
        private readonly IConfiguration _configuration;

        internal FlowEngineBuilder(IServiceCollection services, IConfiguration configuration)
        {
            _services = services;
            _configuration = configuration;
        }

        /// <summary>
        /// Tracks if custom security service has been configured
        /// </summary>
        internal bool HasCustomSecurity { get; private set; }

        /// <summary>
        /// Add custom validation service
        /// </summary>
        public FlowEngineBuilder WithValidation<TValidation>()
            where TValidation : class, IFlowValidation
        {
            _services.AddScoped<IFlowValidation, TValidation>();
            return this;
        }

        /// <summary>
        /// Add custom security service
        /// </summary>
        public FlowEngineBuilder WithSecurity<TSecurity>()
            where TSecurity : class, IFlowSecurity
        {
            _services.AddScoped<IFlowSecurity, TSecurity>();
            HasCustomSecurity = true;
            return this;
        }

        /// <summary>
        /// Add middleware to the flow execution pipeline
        /// </summary>
        public FlowEngineBuilder UseMiddleware<TMiddleware>()
            where TMiddleware : class, IFlowMiddleware
        {
            _services.AddScoped<IFlowMiddleware, TMiddleware>();
            return this;
        }

        /// <summary>
        /// Add custom services
        /// </summary>
        public FlowEngineBuilder AddServices(Action<IServiceCollection> configure)
        {
            configure(_services);
            return this;
        }
    }
}
