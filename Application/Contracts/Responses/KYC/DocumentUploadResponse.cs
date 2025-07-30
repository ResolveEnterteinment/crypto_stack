using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Contracts.Responses.KYC
{
    public class DocumentUploadResponse
    {
        public Guid DocumentId { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; }
    }
}
