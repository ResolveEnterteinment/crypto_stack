using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace Application.Contracts.Requests.KYC
{
    public class DocumentUploadRequest
    {
        [Required]
        public string SessionId { get; set; } = string.Empty;

        [Required]
        public IFormFile File { get; set; } = default!;

        [Required]
        public string DocumentType { get; set; } = string.Empty;
    }
}
