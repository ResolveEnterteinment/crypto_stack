namespace Domain.Models.Logging
{
    public class TraceLogNodeData
    {
        public TraceLog Log { get; set; } = null!;
        public List<TraceLogNodeData> Children { get; set; } = new();
    }

}
