namespace Domain.DTOs.Flow
{
    public class StepDto
    {
        public string Name { get; set; }
        public string Status { get; set; }
        public List<string> StepDependencies { get; set; } = [];
        public Dictionary<string, string> DataDependencies { get; set; } = [];
        public int MaxRetries { get; set; }
        public string RetryDelay { get; set; }
        public string Timeout { get; set; }
        public bool IsCritical { get; set; }
        public bool IsIdempotent { get; set; }
        public bool CanRunInParallel { get; set; }
        public StepResultDto? Result { get; set; }
        public List<BranchDto> Branches { get; set; } = [];
    }
}
