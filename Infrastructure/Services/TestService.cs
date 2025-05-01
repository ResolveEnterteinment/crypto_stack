using Application.Interfaces;
using Application.Interfaces.Logging;
using Domain.Constants.Logging;

namespace Infrastructure.Services
{

    public class TestService : ITestService
    {
        private readonly ILoggingService _logger;

        public TestService(ILoggingService logger)
        {
            _logger = logger;
        }

        public async Task PerformActionAsync()
        {
            using var scope = _logger.BeginScope("TestService.PerformActionAsync", new { Service = "TestService", Method = "PerformActionAsync" });

            await _logger.LogTraceAsync("Started performing action.");

            await NestedActionAsync();

            await _logger.LogTraceAsync("Finished performing action.");
        }

        private async Task NestedActionAsync()
        {
            using var scope = _logger.BeginScope("TestService.NestedActionAsync", new { Method = "NestedActionAsync" });

            await _logger.LogTraceAsync("Executing nested action.");

            // Simulate async work
            await Task.Delay(100);

            await _logger.LogTraceAsync("Nested action completed.", level: LogLevel.Critical, requiresResolution: true);
        }
    }
}
