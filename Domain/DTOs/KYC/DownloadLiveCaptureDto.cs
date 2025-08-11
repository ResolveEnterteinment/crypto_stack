
namespace Domain.DTOs.KYC
{
    public class DownloadLiveCaptureDto
    {
        public List<byte[]> FileDatas { get; set; }
        public string ContentType { get; set; }
        public List<string> FileNames { get; set; }
    }
}
