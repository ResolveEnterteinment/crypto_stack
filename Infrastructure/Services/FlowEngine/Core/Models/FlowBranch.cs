namespace Infrastructure.Services.FlowEngine.Core.Models
{
    public class FlowBranch
    {
        public Func<FlowContext, bool> Condition { get; set; }
        public bool IsDefault { get; set; } = false;
        public List<FlowStep> Steps { get; set; } = new();
    }
}
