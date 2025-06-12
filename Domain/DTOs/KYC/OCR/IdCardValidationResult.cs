namespace Domain.DTOs.KYC.OCR
{
    public class IdCardValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public string? ExtractedText { get; set; }
        public bool ContainsName { get; set; }
        public bool ContainsDateOfBirth { get; set; }
        public bool ContainsDocumentNumber { get; set; }
        public double ConfidenceScore { get; set; }
    }
}
