namespace Domain.DTOs.KYC
{
    /// <summary>
    /// Result of EXIF data analysis for tamper detection
    /// </summary>
    public class ExifAnalysisResult
    {
        public bool IsConsistent { get; set; }
        public List<string> InconsistentFields { get; set; } = new();
        public Dictionary<string, object> ExifData { get; set; } = new();
    }
}