
namespace Domain.DTOs.KYC
{
    public class DownloadDocumentDto
    {
        public byte[] FileData { get; set; }
        public string ContentType { get; set; }
        public string FileName { get; set; }
    }
}
