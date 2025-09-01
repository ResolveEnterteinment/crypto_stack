namespace Domain.DTOs.Flow
{
    public class BranchDto
    {
        public bool IsDefault { get; set; } = false;
        public string Condition { get; set; }
        public List<SubStepDto> Steps { get; set; } = [];
    }
}
