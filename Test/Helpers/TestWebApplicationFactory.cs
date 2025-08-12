using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Application.Interfaces;
using Moq;

namespace crypto_investment_project.Tests.Helpers
{
    public class TestWebApplicationFactory<TStartup> : WebApplicationFactory<TStartup> where TStartup : class
    {
        public Mock<IIdempotencyService> IdempotencyServiceMock { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                // Remove the existing IIdempotencyService registration
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IIdempotencyService));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                // Create mock
                IdempotencyServiceMock = new Mock<IIdempotencyService>();

                // Add mock implementation
                services.AddSingleton(IdempotencyServiceMock.Object);

                // Configure test logging
                services.AddLogging(builder =>
                {
                    builder.AddConsole()
                           .SetMinimumLevel(LogLevel.Warning);
                });
            });

            builder.UseEnvironment("Testing");
        }
    }
}