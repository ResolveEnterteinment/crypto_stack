using System.ComponentModel.DataAnnotations;

namespace Application.Contracts.Requests.KYC
{
    /// <summary>
    /// Tamper detection information for live capture
    /// </summary>
    public class TamperDetectionInfo
    {
        /// <summary>
        /// Indicates if the document appears to be captured live
        /// </summary>
        public bool IsLive { get; set; }

        /// <summary>
        /// Confidence score for liveness detection (0.0-1.0)
        /// </summary>
        [Range(0.0, 1.0)]
        public double Confidence { get; set; }

        /// <summary>
        /// Additional tamper detection flags
        /// </summary>
        public Dictionary<string, object> Flags { get; set; } = new();
    }
}