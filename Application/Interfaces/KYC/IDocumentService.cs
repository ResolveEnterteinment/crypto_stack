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
        Task<ResultWrapper<DocumentData>> SaveDocumentRecordAsync(DocumentData documentRecord);

        /// <summary>
        /// Upload and process a document file
        /// </summary>
        Task<ResultWrapper<DocumentUploadResponse>> UploadDocumentAsync(
            Guid userId, 
            Guid sessionId, 
            IFormFile file, 
            string documentType);

        /// <summary>
        /// Get document by ID with security validation
        /// </summary>
        Task<ResultWrapper<DocumentData>> GetDocumentAsync(Guid documentId, Guid? userId = null);

        /// <summary>
        /// Get all documents for a user session
        /// </summary>
        Task<ResultWrapper<List<DocumentData>>> GetSessionDocumentsAsync(Guid sessionId, Guid userId);

        /// <summary>
        /// Download document file with security validation
        /// </summary>
        Task<ResultWrapper<DownloadDocumentDto>> DownloadDocumentAsync(
            Guid documentId, 
            Guid requestingUserId, 
            bool isAdminRequest = false);

        /// <summary>
        /// Delete document (soft delete with audit trail)
        /// </summary>
        Task<ResultWrapper<CrudResult<DocumentData>>> DeleteDocumentAsync(Guid documentId, Guid userId, string reason = "User requested deletion");

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
        public int DocumentsToday { get; set; }
        public long TotalStorageUsed { get; set; }
        public Dictionary<string, int> DocumentTypeBreakdown { get; set; } = new();
        public Dictionary<string, int> StatusBreakdown { get; set; } = new();
    }
}