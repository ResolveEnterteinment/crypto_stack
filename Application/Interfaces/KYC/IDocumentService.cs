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
    public interface IDocumentService
    {
        /// <summary>
        /// Save a document record to the database
        /// </summary>
        Task<ResultWrapper<DocumentRecord>> SaveDocumentRecordAsync(DocumentRecord documentRecord);

        /// <summary>
        /// Save a live capture record to the database
        /// </summary>
        Task<ResultWrapper<LiveCaptureRecord>> SaveLiveCaptureRecordAsync(LiveCaptureRecord liveCaptureRecord);

        /// <summary>
        /// Upload and process a document file
        /// </summary>
        Task<ResultWrapper<DocumentUploadResponse>> UploadDocumentAsync(
            Guid userId, 
            Guid sessionId, 
            IFormFile file, 
            string documentType);

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
        /// Get document by ID with security validation
        /// </summary>
        Task<ResultWrapper<DocumentRecord>> GetDocumentAsync(Guid documentId, Guid? userId = null);

        /// <summary>
        /// Get live capture record by ID
        /// </summary>
        Task<ResultWrapper<LiveCaptureRecord>> GetLiveCaptureAsync(Guid captureId, Guid? userId = null);

        /// <summary>
        /// Get all documents for a user session
        /// </summary>
        Task<ResultWrapper<List<DocumentRecord>>> GetSessionDocumentsAsync(Guid sessionId, Guid userId);

        /// <summary>
        /// Get all live captures for a user session
        /// </summary>
        Task<ResultWrapper<List<LiveCaptureRecord>>> GetSessionLiveCapturesAsync(Guid sessionId, Guid userId);

        /// <summary>
        /// Download document file with security validation
        /// </summary>
        Task<ResultWrapper<(byte[] FileData, string ContentType, string FileName)>> DownloadDocumentAsync(
            Guid documentId, 
            Guid requestingUserId, 
            bool isAdminRequest = false);

        /// <summary>
        /// Download live capture file with security validation
        /// </summary>
        Task<ResultWrapper<(List<byte[]> FileDatas, string ContentType, List<string> FileNames)>> DownloadLiveCaptureAsync(
            Guid captureId,
            Guid requestingUserId,
            bool isAdminRequest = false);

        /// <summary>
        /// Validate file before upload
        /// </summary>
        Task<ResultWrapper<ValidationResult>> ValidateFileAsync(IFormFile file, string documentType);

        /// <summary>
        /// Validate live capture data
        /// </summary>
        ResultWrapper<ValidationResult> ValidateLiveDocumentCapture(LiveDocumentCaptureRequest request, Guid userId);

        /// <summary>
        /// Delete document (soft delete with audit trail)
        /// </summary>
        Task<ResultWrapper> DeleteDocumentAsync(Guid documentId, Guid userId, string reason = "User requested deletion");

        /// <summary>
        /// Delete live capture (soft delete with audit trail)
        /// </summary>
        Task<ResultWrapper> DeleteLiveCaptureAsync(Guid captureId, Guid userId, string reason = "User requested deletion");

        /// <summary>
        /// Generate secure file hash for file path
        /// </summary>
        Task<string> GenerateFileHashAsync(string filePath);

        /// <summary>
        /// Verify file integrity using hash
        /// </summary>
        Task<bool> VerifyFileIntegrityAsync(string filePath, string expectedHash);

        /// <summary>
        /// Clean up expired temporary files
        /// </summary>
        Task<ResultWrapper> CleanupExpiredFilesAsync();

        /// <summary>
        /// Get document statistics for admin dashboard
        /// </summary>
        Task<ResultWrapper<DocumentStatistics>> GetDocumentStatisticsAsync();
    }

    

    /// <summary>
    /// Document statistics for admin dashboard
    /// </summary>
    public class DocumentStatistics
    {
        public int TotalDocuments { get; set; }
        public int TotalLiveCaptures { get; set; }
        public int DocumentsToday { get; set; }
        public int LiveCapturesToday { get; set; }
        public long TotalStorageUsed { get; set; }
        public Dictionary<string, int> DocumentTypeBreakdown { get; set; } = new();
        public Dictionary<string, int> StatusBreakdown { get; set; } = new();
    }
}