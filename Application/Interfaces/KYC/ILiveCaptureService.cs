using Application.Contracts.Requests.KYC;
using Application.Contracts.Responses.KYC;
using Domain.DTOs;
using Domain.DTOs.KYC;
using Domain.Models.KYC;
using Microsoft.AspNetCore.Http;

namespace Application.Interfaces.KYC
{
    /// <summary>
    /// Service interface for handling KYC document operations with enhanced security and validation
    /// </summary>
    public interface ILiveCaptureService
    {

        /// <summary>
        /// Save a live capture record to the database
        /// </summary>
        Task<ResultWrapper<LiveCaptureData>> SaveLiveCaptureRecordAsync(LiveCaptureData liveCaptureRecord);

        /// <summary>
        /// Process a live document captured document
        /// </summary>
        Task<ResultWrapper<LiveCaptureResponse>> ProcessLiveDocumentCaptureAsync(
            Guid userId,
            LiveDocumentCaptureRequest request);

        /// <summary>
        /// Process a live selfie captured document
        /// </summary>
        Task<ResultWrapper<LiveCaptureResponse>> ProcessLiveSelfieCaptureAsync(
            Guid userId,
            LiveSelfieCaptureRequest request);

        /// <summary>
        /// Get live capture record by ID
        /// </summary>
        Task<ResultWrapper<LiveCaptureData>> GetLiveCaptureAsync(Guid captureId, Guid? userId = null);

        /// <summary>
        /// Get all live captures for a user session
        /// </summary>
        Task<ResultWrapper<List<LiveCaptureData>>> GetSessionLiveCapturesAsync(Guid sessionId, Guid userId);

        /// <summary>
        /// Download live capture file with security validation
        /// </summary>
        Task<ResultWrapper<DownloadLiveCaptureDto>> DownloadLiveCaptureAsync(
            Guid captureId,
            Guid requestingUserId,
            bool isAdminRequest = false);

        /// <summary>
        /// Delete live capture (soft delete with audit trail)
        /// </summary>
        Task<ResultWrapper<CrudResult<LiveCaptureData>>> DeleteLiveCaptureAsync(Guid captureId, Guid userId, string reason = "User requested deletion");

        /// <summary>
        /// Clean up expired temporary files
        /// </summary>
        Task<ResultWrapper<bool>> CleanupExpiredFilesAsync();

        /// <summary>
        /// Get document statistics for admin dashboard
        /// </summary>
        Task<ResultWrapper<LiveCaptureStatistics>> GetDocumentStatisticsAsync();
    }

    /// <summary>
    /// Document statistics for admin dashboard
    /// </summary>
    public class LiveCaptureStatistics
    {
        public int TotalLiveCaptures { get; set; }
        public int LiveCapturesToday { get; set; }
        public long TotalStorageUsed { get; set; }
        public Dictionary<string, int> DocumentTypeBreakdown { get; set; } = new();
        public Dictionary<string, int> StatusBreakdown { get; set; } = new();
    }
}