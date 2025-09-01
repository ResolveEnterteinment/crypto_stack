namespace Domain.DTOs.Flow
{
    public class FailedRecoveryDto
    {
        public Guid FlowId { get; set; }
        public string Error { get; set; }
    }
}
