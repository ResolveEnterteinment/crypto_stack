namespace Domain.DTOs.KYC
{
    /// <summary>
    /// Result of document data extraction using OCR/ML
    /// </summary>
    public class DocumentExtractionResult
    {
        public bool IsSuccess { get; set; }
        public Dictionary<string, object> Data { get; set; } = new();
        public double ConfidenceScore { get; set; }
        public List<string> ExtractedFields { get; set; } = new();
        public string ErrorMessage { get; set; } = string.Empty;
    }
}