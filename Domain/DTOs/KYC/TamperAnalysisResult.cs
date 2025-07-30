namespace Domain.DTOs.KYC
{
    /// <summary>
    /// Result of tamper detection analysis
    /// </summary>
    public class TamperAnalysisResult
    {
        public bool IsAuthentic { get; set; }
        public List<string> Issues { get; set; } = new();
        public double ConfidenceScore { get; set; }
    }
}