namespace Domain.DTOs.KYC
{
    /// <summary>
    /// Result of compression artifact analysis
    /// </summary>
    public class CompressionAnalysisResult
    {
        public bool SuspiciousPatterns { get; set; }
        public List<string> DetectedArtifacts { get; set; } = new();
        public double CompressionQuality { get; set; }
        public bool ConsistentCompression { get; set; }
    }
}