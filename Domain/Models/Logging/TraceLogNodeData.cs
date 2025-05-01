namespace Domain.Models.Logging
{
    public class TraceLogNodeData
    {
        public TraceLogData Log { get; set; } = null!;
        public List<TraceLogNodeData> Children { get; set; } = new();
    }

}
