using Application.Contracts.Requests.KYC;
using Application.Contracts.Responses.KYC;
using Application.Interfaces.Base;
using Application.Interfaces.KYC;
using Application.Interfaces.Logging;
using Domain.Constants;
using Domain.Constants.KYC;
using Domain.Constants.Logging;
using Domain.DTOs;
using Domain.DTOs.Exchange;
using Domain.DTOs.KYC;
using Domain.DTOs.Logging;
using Domain.Exceptions;
using Domain.Models.Asset;
using Domain.Models.KYC;
using Domain.Models.Payment;
using Infrastructure.Services.Base;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Infrastructure.Services.KYC
{
    /// <summary>
    /// Production-ready document service for handling KYC document uploads and live captures
    /// </summary>
    public class DocumentService : BaseService<DocumentData>, IDocumentService
    {
        private readonly IConfiguration _configuration;
        private readonly IDataProtector _dataProtector;
        private readonly string _uploadBasePath;
        private readonly string[] _allowedFileTypes;
        private readonly long _maxFileSize;

        public DocumentService(
            IServiceProvider serviceProvider,
            IConfiguration configuration,
            IDataProtectionProvider dataProtectionProvider)
            : base(serviceProvider)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _dataProtector = dataProtectionProvider.CreateProtector("KYC.Documents");

            _uploadBasePath = _configuration["FileUpload:KycDocuments"] ?? 
                Path.Combine(Directory.GetCurrentDirectory(), "secure", "uploads", "kyc");
            
            _allowedFileTypes = _configuration.GetSection("FileUpload:AllowedTypes").Get<string[]>() ?? 
                new[] { ".jpg", ".jpeg", ".png", ".pdf", ".webp" };
            _maxFileSize = _configuration.GetValue("FileUpload:MaxFileSizeBytes", 10 * 1024 * 1024); // 10MB default

            // Ensure directories exist
            Directory.CreateDirectory(_uploadBasePath);
        }

        public async Task<ResultWrapper<DocumentData>> SaveDocumentRecordAsync(DocumentData documentRecord)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.KYC",
                    FileName = "DocumentService",
                    OperationName = "SaveDocumentRecordAsync(DocumentRecord documentRecord)",
                    State = new()
                    {
                        ["DocumentId"] = documentRecord.Id,
                        ["DocumentType"] = documentRecord.DocumentType,
                        ["DocumentContentType"] = documentRecord.ContentType,
                        ["DocumentOriginalFileName"] = documentRecord.OriginalFileName,
                    },
                    LogLevel = LogLevel.Error,
                },
                async () =>
                {
                    if (documentRecord == null)
                    {
                        throw new ArgumentNullException(nameof(documentRecord), "Document record cannot be null");
                    }

                    // Validate required fields
                    if (documentRecord.UserId == Guid.Empty || documentRecord.SessionId == Guid.Empty)
                    {
                        throw new ArgumentException("UserId and SessionId are required");
                    }

                    var result = await InsertAsync(documentRecord);
                    if (!result.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to create document record: {result.ErrorMessage}");
                    }

                    await _loggingService.LogTraceAsync($"Document record saved: {documentRecord.Id}", "SaveDocumentRecord");
                    return documentRecord;
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<DocumentUploadResponse>> UploadDocumentAsync(
            Guid userId,
            Guid sessionId,
            IFormFile file,
            string documentType)
        {
            return await _resilienceService.CreateBuilder<DocumentUploadResponse>(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.KYC",
                    FileName = "DocumentService",
                    OperationName = "UploadDocumentAsync(Guid userId, Guid sessionId, IFormFile file, string documentType)",
                    State = new()
                    {
                        ["UserId"] = userId,
                        ["SessionId"] = sessionId,
                        ["DocumentType"] = documentType,
                        ["FileSize"] = file.Length,
                        ["ContentType"] = file.ContentType,
                    },
                    LogLevel = LogLevel.Error,
                },
                async () =>
                {
                    // Validate file first (before any processing)
                    var validationResult = await ValidateFileAsync(file, documentType);
                    if (!validationResult.IsSuccess || !validationResult.Data.IsValid)
                    {
                        throw new ValidationException(
                            validationResult.Data?.ErrorMessage ?? "File validation failed",
                            new Dictionary<string, string[]>
                            {
                                ["File"] = new[] { validationResult.Data?.ErrorMessage ?? "Invalid file" }
                            });
                    }

                    // Read file into memory
                    byte[] fileData;
                    using (var ms = new MemoryStream())
                    {
                        await file.CopyToAsync(ms);
                        fileData = ms.ToArray();
                    }

                    // Calculate original hash before encryption for integrity verification
                    string originalHash = CalculateFileHash(fileData);

                    // Encrypt the document data
                    byte[] encryptedData = await EncryptAsync(fileData);

                    // Create secure filename
                    string secureFileName = $"{Guid.NewGuid()}_{sessionId}";

                    // Save encrypted file data
                    string filePath = Path.Combine(_uploadBasePath, secureFileName);
                    await File.WriteAllBytesAsync(filePath, encryptedData);

                    string? savedFilePath = filePath; // Track for cleanup

                    try
                    {
                        // Create document record with encryption info
                        var documentRecord = new DocumentData
                        {
                            Id = Guid.NewGuid(),
                            UserId = userId,
                            SessionId = sessionId,
                            OriginalFileName = file.FileName,
                            SecureFileName = secureFileName,
                            FileSize = file.Length,
                            FilePath = filePath,
                            ContentType = file.ContentType,
                            DocumentType = documentType,
                            FileHash = originalHash,
                            Status = "Uploaded",
                            CreatedAt = DateTime.UtcNow,
                            IsEncrypted = true,
                            EncryptionMethod = "DataProtection"
                        };

                        // Save record to database (this has its own resilience)
                        var saveResult = await SaveDocumentRecordAsync(documentRecord);
                        if (!saveResult.IsSuccess)
                        {
                            throw new DatabaseException($"Failed to save document record: {saveResult.ErrorMessage}");
                        }

                        return new DocumentUploadResponse
                        {
                            DocumentId = documentRecord.Id,
                            Status = documentRecord.Status,
                            UploadedAt = documentRecord.CreatedAt
                        };
                    }
                    catch (Exception)
                    {
                        // Clean up file if database save fails
                        if (savedFilePath != null && File.Exists(savedFilePath))
                        {
                            try
                            {
                                File.Delete(savedFilePath);
                            }
                            catch (Exception cleanupEx)
                            {
                                await   _loggingService.LogTraceAsync(
                                    $"Failed to cleanup file {savedFilePath}: {cleanupEx.Message}",
                                    level: LogLevel.Warning);
                            }
                        }
                        throw; // Re-throw the original exception
                    }
                })
                .WithMongoDbWriteResilience()
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1))
                .WithContext("FileUpload", "KYC Document")
                .OnSuccess(async (response) =>
                {
                    await _loggingService.LogTraceAsync(
                        $"Document uploaded successfully: {response.DocumentId}",
                        "UploadDocument",
                        LogLevel.Information);
                })
                .OnError(async (ex) =>
                {
                    await _loggingService.LogTraceAsync(
                        $"Document upload failed for user {userId}: {ex.Message}",
                        "UploadDocument",
                        LogLevel.Error,
                        requiresResolution: true);
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<DocumentData>> GetDocumentAsync(Guid documentId, Guid? userId = null)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.KYC",
                    FileName = "DocumentService",
                    OperationName = "GetDocumentAsync(Guid documentId, Guid? userId = null)",
                    State = new()
                    {
                        ["DocumentId"] = documentId,
                        ["UserId"] = userId,
                    }
                },
                async () =>
                {
                    var filter = Builders<DocumentData>.Filter.Eq(d => d.Id, documentId);
                
                    if (userId.HasValue)
                    {
                        filter = Builders<DocumentData>.Filter.And(filter,
                            Builders<DocumentData>.Filter.Eq(d => d.UserId, userId.Value));
                    }

                    var document = await GetOneAsync(filter);
                    if (document == null || document.Data == null)
                    {
                        throw new ResourceNotFoundException($"Failed to get document", documentId.ToString());
                    }

                    return document.Data;
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<List<DocumentData>>> GetSessionDocumentsAsync(Guid sessionId, Guid userId)
        {
            return await _resilienceService.CreateBuilder(
                 new Scope
                 {
                     NameSpace = "Infrastructure.Services.KYC",
                     FileName = "DocumentService",
                     OperationName = "GetSessionDocumentsAsync(Guid sessionId, Guid userId)",
                     State = new()
                     {
                         ["SessionId"] = sessionId,
                         ["UserId"] = userId,
                     }
                 },
                 async () =>
                 {
                     var filter = Builders<DocumentData>.Filter.And(
                    Builders<DocumentData>.Filter.Eq(d => d.SessionId, sessionId),
                    Builders<DocumentData>.Filter.Eq(d => d.UserId, userId));

                    var documents = await GetManyAsync(filter);
                    return documents?.Data ?? new List<DocumentData>();
                 })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<DownloadDocumentDto>> DownloadDocumentAsync(
            Guid documentId,
            Guid requestingUserId,
            bool isAdminRequest = false)
        {
            return await _resilienceService.CreateBuilder(
                 new Scope
                 {
                     NameSpace = "Infrastructure.Services.KYC",
                     FileName = "DocumentService",
                     OperationName = "DownloadDocumentAsync(Guid documentId, Guid requestingUserId, bool isAdminRequest = false)",
                     State = new()
                     {
                         ["DocumentId"] = documentId,
                         ["UserId"] = requestingUserId,
                         ["IsAdminRequest"] = isAdminRequest
                     }
                 },
                 async () =>
                 {
                     // Get document record (keep your existing retrieval code)
                     var documentRecord = await GetDocumentAsync(documentId);
                    if (!documentRecord.IsSuccess || documentRecord.Data == null)
                    {
                         throw new ResourceNotFoundException($"Failed to get document", documentId.ToString());
                    }

                    var record = documentRecord.Data;

                    // Security check (keep your existing security validation)
                    if (!isAdminRequest && record.UserId != requestingUserId)
                    {
                         throw new UnauthorizedAccessException("Unauthorized document access");
                    }

                    // Read file data from storage
                    string filePath = Path.Combine(_uploadBasePath, record.SecureFileName);
                    if (!File.Exists(filePath))
                    {
                         throw new FileNotFoundException("Document file not found", filePath);
                    }

                    byte[] fileData = await File.ReadAllBytesAsync(filePath);

                    // Decrypt file if it's encrypted
                    if (record.IsEncrypted)
                    {
                        fileData = await DecryptAsync(fileData);

                        // Verify hash for document integrity
                        string currentHash = CalculateFileHash(fileData);
                        if (currentHash != record.FileHash)
                        {
                            _loggingService.LogWarning($"Document integrity check failed for {documentId}. Hash mismatch.");
                            // We still return the document but log the integrity issue
                        }
                    }
                    return new DownloadDocumentDto()
                    {
                        FileData = fileData,
                        ContentType = record.ContentType,
                        FileName = record.OriginalFileName
                    };
                })
                .WithMongoDbReadResilience()
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10))
                .WithContext("FileUpload", "KYC Document")
                .OnSuccess(async (response) =>
                {
                    await _loggingService.LogTraceAsync(
                        $"Document ID {documentId} downloaded successfully: {response.FileName}",
                        "DownloadDocument",
                        LogLevel.Information);
                })
                .OnError(async (ex) =>
                {
                    await _loggingService.LogTraceAsync(
                        $"Document ID {documentId} download failed for user {requestingUserId}: {ex.Message}",
                        "DownloadDocument",
                        LogLevel.Error,
                        requiresResolution: true);
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<CrudResult<DocumentData>>> DeleteDocumentAsync(Guid documentId, Guid userId, string reason = "User requested deletion")
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.KYC",
                    FileName = "DocumentService",
                    OperationName = "DeleteDocumentAsync(Guid documentId, Guid userId, string reason = \"User requested deletion\")",
                    State = new()
                    {
                        ["DocumentId"] = documentId,
                        ["UserId"] = userId,
                        ["Reason"] = reason,
                    },
                    LogLevel = LogLevel.Error,
                },
                async () =>
                {
                    var documentResult = await GetDocumentAsync(documentId, userId);
                    if (!documentResult.IsSuccess)
                    {
                        throw new ResourceNotFoundException($"Failed to get document", documentId.ToString());
                    }

                    var document = documentResult.Data;

                    // Soft delete - update status instead of physical deletion
                    var updateFields = new Dictionary<string, object>
                    {
                        ["Status"] = "DELETED",
                        ["UpdatedAt"] = DateTime.UtcNow,
                        ["DeletionReason"] = reason
                    };

                    var updateResult = await UpdateAsync(documentId, updateFields);
                    if (!updateResult.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to delete document record: {updateResult.ErrorMessage}");
                    }

                    await _loggingService.LogTraceAsync($"Document soft deleted: {documentId}, Reason: {reason}", "DeleteDocument");
                    return updateResult.Data;
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper> CleanupExpiredFilesAsync()
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.KYC",
                    FileName = "DocumentService",
                    OperationName = "CleanupExpiredFilesAsync()",
                    State = [],
                    LogLevel = LogLevel.Warning,
                },
                async () =>
                {
                    var cleanupTasks = new List<Task>();
                    var deletedCount = 0;

                    // Clean up old temporary files (older than 30 days)
                    var cutoffDate = DateTime.UtcNow.AddDays(-30);

                    // Clean up documents marked as deleted
                    var deletedDocumentsFilter = Builders<DocumentData>.Filter.And(
                        Builders<DocumentData>.Filter.Eq(d => d.Status, "DELETED"),
                        Builders<DocumentData>.Filter.Lt(d => d.CreatedAt, cutoffDate));

                    var deletedDocuments = await GetManyAsync(deletedDocumentsFilter);

                    foreach (var doc in deletedDocuments?.Data ?? new List<DocumentData>())
                    {
                        var filePath = Path.Combine(_uploadBasePath, doc.UserId.ToString(), doc.SecureFileName);
                        if (File.Exists(filePath))
                        {
                            cleanupTasks.Add(Task.Run(() =>
                            {
                                try
                                {
                                    File.Delete(filePath);
                                    Interlocked.Increment(ref deletedCount);
                                }
                                catch (Exception ex)
                                {
                                    _loggingService.LogError($"Failed to delete file {filePath}: {ex.Message}");
                                }
                            }));
                        }
                    }

                    await Task.WhenAll(cleanupTasks).ContinueWith(async (Task task) =>
                    {
                        await _loggingService.LogTraceAsync($"File cleanup completed. Deleted {deletedCount} files.", "CleanupExpiredFiles");
                    });
                })
                .ExecuteAsync();
        }
        public async Task<ResultWrapper<DocumentStatistics>> GetDocumentStatisticsAsync()
        {
            return await _resilienceService.CreateBuilder(
                 new Scope
                 {
                     NameSpace = "Infrastructure.Services.KYC",
                     FileName = "DocumentService",
                     OperationName = "GetDocumentStatisticsAsync()",
                     State = []
                 },
                 async () =>
                 {
                     var today = DateTime.UtcNow.Date;
                     var todayFilter = Builders<DocumentData>.Filter.Gte(d => d.CreatedAt, today);

                     var totalDocuments = (int)await _repository.CountAsync(Builders<DocumentData>.Filter.Empty);
                     var documentsToday = (int)await _repository.CountAsync(todayFilter);

                     // Calculate storage usage (simplified)
                     var allDocuments = await _repository.GetAllAsync();
                     var totalStorageUsed = allDocuments?.Sum(d => d.FileSize) ?? 0;

                     var statistics = new DocumentStatistics
                     {
                         TotalDocuments = totalDocuments,
                         DocumentsToday = documentsToday,
                         TotalStorageUsed = totalStorageUsed,
                         DocumentTypeBreakdown = new Dictionary<string, int>(),
                         StatusBreakdown = new Dictionary<string, int>()
                     };

                     // Group by document type and status
                     if (allDocuments?.Any() == true)
                     {
                         statistics.DocumentTypeBreakdown = allDocuments
                             .GroupBy(d => d.DocumentType)
                             .ToDictionary(g => g.Key, g => g.Count());

                         statistics.StatusBreakdown = allDocuments
                             .GroupBy(d => d.Status)
                             .ToDictionary(g => g.Key, g => g.Count());
                     }

                     return statistics;
                 })
                .WithMongoDbReadResilience()
                .ExecuteAsync();
        }

        // Private helper methods
        private async Task<ResultWrapper<ValidationResult>> ValidateFileAsync(IFormFile file, string documentType)
        {
            var errors = new List<string>();

            if (file == null || file.Length == 0)
            {
                errors.Add("No file uploaded");
            }
            else
            {
                // Check file size
                if (file.Length > _maxFileSize)
                {
                    errors.Add($"File size exceeds maximum limit of {_maxFileSize / 1024 / 1024}MB");
                }

                // Check file type
                var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!_allowedFileTypes.Contains(fileExtension))
                {
                    errors.Add($"File type not allowed. Supported types: {string.Join(", ", _allowedFileTypes)}");
                }

                // Check document type specific requirements
                if (DocumentType.RequiresLiveCapture(documentType))
                {
                    errors.Add($"Document type '{documentType}' requires live capture, not file upload");
                }

                // Basic malware scanning (simplified)
                if (await ContainsSuspiciousContentAsync(file))
                {
                    errors.Add("File contains potentially malicious content");
                }
            }

            var result = new ValidationResult
            {
                IsValid = !errors.Any(),
                ErrorMessage = string.Join("; ", errors),
                Issues = errors
            };

            return ResultWrapper<ValidationResult>.Success(result);
        }

        private async Task<byte[]> EncryptAsync(byte[] documentData)
        {
            try
            {
                // Use DataProtection for document encryption
                // First convert to Base64 since DataProtection works with strings
                var base64Data = Convert.ToBase64String(documentData);
                var protectedData = _dataProtector.Protect(base64Data);

                // Return as byte array for storage
                return Encoding.UTF8.GetBytes(protectedData);
            }
            catch (Exception ex)
            {
                await _loggingService.LogTraceAsync("Error encrypting document data", level: LogLevel.Error);
                throw;
            }
        }

        private async Task<byte[]> DecryptAsync(byte[] encryptedData)
        {
            try
            {
                // Convert encrypted bytes back to protected string
                var protectedString = Encoding.UTF8.GetString(encryptedData);

                // Unprotect using DataProtection
                var base64Data = _dataProtector.Unprotect(protectedString);

                // Convert back to original document bytes
                return Convert.FromBase64String(base64Data);
            }
            catch (Exception ex)
            {
                await _loggingService.LogTraceAsync("Error decrypting document data", level: Domain.Constants.Logging.LogLevel.Error);
                throw;
            }
        }

        // Calculate hash for document integrity verification
        private string CalculateFileHash(byte[] fileData)
        {
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(fileData);
            return Convert.ToBase64String(hashBytes);
        }

        private async Task<bool> ContainsSuspiciousContentAsync(IFormFile file)
        {
            try
            {
                using var reader = new StreamReader(file.OpenReadStream());
                var content = await reader.ReadToEndAsync();
                file.OpenReadStream().Position = 0; // Reset stream

                // Basic malicious pattern detection
                var suspiciousPatterns = new[]
                {
                    @"<script", @"javascript:", @"vbscript:", @"<?php", @"<%",
                    @"exec\(", @"eval\(", @"system\(", @"shell_exec"
                };

                return suspiciousPatterns.Any(pattern =>
                    Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase));
            }
            catch
            {
                return false; // If we can't read it, assume it's binary and safe
            }
        }
    }
}