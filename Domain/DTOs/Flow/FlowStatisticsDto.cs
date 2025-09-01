namespace Domain.DTOs.Flow
{
    public class FlowStatisticsDto
    {
        public string Period { get; set; }
        public int Total { get; set; }
        public int Running { get; set; }
        public int Completed { get; set; }
        public int Failed { get; set; }
        public int Paused { get; set; }
        public int Cancelled { get; set; }
        public double AverageDuration { get; set; }
        public double SuccessRate { get; set; }
        public Dictionary<string, int> FlowsByType { get; set; } = [];
        public Dictionary<string, int> PauseReasons { get; set; } = [];
    }
}
