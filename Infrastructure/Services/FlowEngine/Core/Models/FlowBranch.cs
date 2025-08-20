namespace Infrastructure.Services.FlowEngine.Core.Models
{
    public class FlowBranch
    {
        public Func<FlowContext, bool> Condition { get; set; }
        public bool IsDefault { get; set; }
        public List<FlowSubStep> SubSteps { get; set; } = new();
    }
}
