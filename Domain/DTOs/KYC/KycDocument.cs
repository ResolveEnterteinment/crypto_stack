using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain.DTOs.KYC
{
    public class KycDocument
    {
        public Guid Id { get; set; }
        public string Type { get; set; }
        public string[] FileHashes { get; set; } = [];
        public DateTime UploadDate { get; set; }
        public bool IsLiveCapture { get; set; }
    }
}
