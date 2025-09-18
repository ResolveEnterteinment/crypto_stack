namespace Domain.DTOs.Flow
{
    public class BranchDto
    {
        public string Name { get; set; }
        public bool IsDefault { get; set; } = false;
        public bool IsConditional { get; set; }
        public int Priority { get; set; } = 0;
        public string? ResourceGroup { get; set; } = null; // For round-robin distribution
        public List<SubStepDto> Steps { get; set; } = [];
    }
}
