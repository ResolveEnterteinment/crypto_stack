namespace Domain.DTOs.KYC
{
    public class LiveCaptureDto
    {
        public Guid Id { get; set; }
        public string Type { get; set; }
        public long FileSize { get; set; }
        public DateTime UploadedAt { get; set; }
        public string Status { get; set; }
        public string[]? Issues { get; set; } = [];
    }
}
