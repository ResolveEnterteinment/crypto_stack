using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.Contracts.Responses.KYC
{
    // Add the missing class definition for LiveCaptureResponse
    public class LiveCaptureResponse
    {
        public Guid CaptureId { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; }
    }
}
