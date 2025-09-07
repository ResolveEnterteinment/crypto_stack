using Infrastructure.Flows.Demo;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services.Demo
{
    public class DemoService : IDemoService
    {
        private readonly ILogger<DemoService> _logger;

        public DemoService(ILogger<DemoService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<string> ProcessItemAsync(string item)
        {
            _logger.LogInformation("Processing item: {Item}", item);
            
            // Simulate processing time
            await Task.Delay(Random.Shared.Next(500, 2000));
            
            return $"Processed_{item}_{DateTime.UtcNow:HHmmss}";
        }

        public async Task<CalculationResult> PerformComplexCalculationAsync()
        {
            _logger.LogInformation("Starting complex calculation");
            
            var startTime = DateTime.UtcNow;
            
            // Simulate complex calculation
            await Task.Delay(Random.Shared.Next(2000, 5000));
            
            var endTime = DateTime.UtcNow;
            
            return new CalculationResult
            {
                Result = new { Pi = Math.PI, Calculation = "Complex mathematical operation" },
                ExecutionTime = endTime.Subtract(startTime),
                ResourceUsage = Random.Shared.NextDouble() * 0.5 + 0.2 // 20-70% usage
            };
        }

        public async Task<ApiResult> CallExternalApiAsync()
        {
            _logger.LogInformation("Calling external API");
            
            var startTime = DateTime.UtcNow;
            
            // Simulate API call
            await Task.Delay(Random.Shared.Next(1000, 3000));
            
            // Simulate occasional API failures
            if (Random.Shared.Next(1, 10) > 7) // 70% chance of success
            {
                throw new HttpRequestException("External API temporarily unavailable");
            }
            
            var endTime = DateTime.UtcNow;
            
            return new ApiResult
            {
                Data = new { Message = "API call successful", Timestamp = DateTime.UtcNow },
                ResponseTime = endTime.Subtract(startTime),
                StatusCode = 200
            };
        }

        public async Task SaveDemoSummaryAsync(DemoFlowSummary summary)
        {
            _logger.LogInformation("Saving demo summary for flow {FlowId}", summary.FlowId);
            
            // Simulate saving to database
            await Task.Delay(200);
            
            _logger.LogInformation("Demo summary saved successfully");
        }

        public double GetSystemLoad()
        {
            // Simulate varying system load
            return Random.Shared.NextDouble();
        }
    }

}