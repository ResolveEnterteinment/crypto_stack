namespace Infrastructure.Services.FlowEngine.Core.Models
{
    public class StepResult
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public Dictionary<string, object> Data { get; set; } = new();

        public static StepResult Success(string message = null, Dictionary<string, object> data = null) =>
            new() { IsSuccess = true, Message = message, Data = data ?? new() };

        public static StepResult Failure(string message) =>
            new() { IsSuccess = false, Message = message };
    }
}
