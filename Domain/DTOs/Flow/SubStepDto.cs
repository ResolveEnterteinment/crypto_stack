namespace Domain.DTOs.Flow
{
    public class SubStepDto : StepDto
    {
        public int Priority { get; set; } = 0;
        public object? SourceData { get; set; } = null;
        public int Index { get; set; } = -1;
        public Dictionary<string, object> Metadata { get; set; } = [];
        public TimeSpan? EstimatedDuration { get; set; }
        public string? ResourceGroup { get; set; } // For round-robin distribution
    }
}
