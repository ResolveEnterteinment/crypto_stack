namespace Domain.DTOs.Flow
{
    public class StepResultDto
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public object? Data { get; set; }
    }
}
