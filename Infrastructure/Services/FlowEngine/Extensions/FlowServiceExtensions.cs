using Infrastructure.Flows.Demo;
using Infrastructure.Services.FlowEngine.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Services.FlowEngine.Extensions
{
    /// <summary>
    /// Extension methods for registering flow definitions with the DI container
    /// </summary>
    public static class FlowServiceExtensions
    {
        /// <summary>
        /// Registers all flows from the Infrastructure.Flows namespace automatically
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection AddFlows(this IServiceCollection services)
        {
            // Get the assembly containing the flows
            services.AddScoped<ComprehensiveDemoFlow>();
            services.AddScoped<DemoNotificationFlow>();

            return services;
        }

        /// <summary>
        /// Validates that all registered flows can be resolved from the container
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <returns>The service collection for chaining</returns>
        public static IServiceCollection ValidateFlowRegistrations(this IServiceCollection services)
        {
            // Build a temporary service provider to validate registrations
            using var serviceProvider = services.BuildServiceProvider();

            // Get all registered flow types
            var flowDescriptors = services
                .Where(sd => sd.ServiceType.IsSubclassOf(typeof(Flow)))
                .ToList();

            foreach (var descriptor in flowDescriptors)
            {
                try
                {
                    // Attempt to resolve each flow to ensure all dependencies are satisfied
                    var flow = serviceProvider.GetRequiredService(descriptor.ServiceType);
                    
                    if (flow == null)
                    {
                        throw new InvalidOperationException($"Failed to resolve flow type {descriptor.ServiceType.Name}");
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Flow validation failed for {descriptor.ServiceType.Name}. " +
                        $"Ensure all dependencies are registered. Error: {ex.Message}", ex);
                }
            }

            return services;
        }
    }
}