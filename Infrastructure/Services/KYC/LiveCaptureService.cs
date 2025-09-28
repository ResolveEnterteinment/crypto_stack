using Application.Contracts.Requests.KYC;
using Application.Contracts.Responses.KYC;
using Application.Interfaces.KYC;
using Domain.Constants.Logging;
using Domain.DTOs;
using Domain.DTOs.KYC;
using Domain.DTOs.Logging;
using Domain.Exceptions;
using Domain.Models.KYC;
using Infrastructure.Services.Base;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Infrastructure.Services.KYC
{
    /// <summary>
    /// Production-ready document service for handling KYC document uploads and live captures
    /// </summary>
    public class LiveCaptureService : BaseService<LiveCaptureData>, ILiveCaptureService
    {
        private readonly IConfiguration _configuration;
        private readonly IDataProtector _dataProtector;
        private readonly string _liveCaptureBasePath;
        private readonly long _maxFileSize;

        public LiveCaptureService(
            IServiceProvider serviceProvider,
            IConfiguration configuration,
            IDataProtectionProvider dataProtectionProvider)
            : base(serviceProvider)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _dataProtector = dataProtectionProvider.CreateProtector("KYC.Documents");

            _liveCaptureBasePath = _configuration["FileUpload:LiveCaptures"] ?? 
                Path.Combine(Directory.GetCurrentDirectory(), "secure", "live-captures");
            
            _maxFileSize = _configuration.GetValue("FileUpload:MaxFileSizeBytes", 10 * 1024 * 1024); // 10MB default

            // Ensure directories exist
            Directory.CreateDirectory(_liveCaptureBasePath);
        }

        public async Task<ResultWrapper<LiveCaptureData>> SaveLiveCaptureRecordAsync(LiveCaptureData liveCaptureRecord)
        {
            return await _resilienceService.CreateBuilder<LiveCaptureData>(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.KYC",
                    FileName = "LiveCaptureService",
                    OperationName = "SaveLiveCaptureRecordAsync(LiveCaptureRecord liveCaptureRecord)",
                    State = new()
                    {
                        ["DocumentId"] = liveCaptureRecord.Id,
                        ["DocumentType"] = liveCaptureRecord.DocumentType,
                    },
                    LogLevel = LogLevel.Error,
                },
                async () =>
                {
                    if (liveCaptureRecord == null)
                    {
                        throw new ArgumentNullException(nameof(liveCaptureRecord), "Live capture record cannot be null");
                    }

                    // Validate required fields
                    if (liveCaptureRecord.UserId == Guid.Empty || liveCaptureRecord.SessionId == Guid.Empty)
                    {
                        throw new ArgumentException("UserId and SessionId are required");
                    }

                    var result = await InsertAsync(liveCaptureRecord);
                    if (!result.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to create document record: {result.ErrorMessage}");
                    }

                    await   _loggingService.LogTraceAsync($"Live capture record saved: {liveCaptureRecord.Id}", "SaveLiveCaptureRecord");
                    return liveCaptureRecord;
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<LiveCaptureResponse>> ProcessLiveDocumentCaptureAsync(
            Guid userId,
            LiveDocumentCaptureRequest request)
        {
            return await _resilienceService.CreateBuilder<LiveCaptureResponse>(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.KYC",
                    FileName = "LiveCaptureService",
                    OperationName = "ProcessLiveDocumentCaptureAsync(Guid userId, LiveDocumentCaptureRequest request)",
                    State = new()
                    {
                        ["UserId"] = userId,
                        ["SessionId"] = request.SessionId,
                        ["DocumentType"] = request.DocumentType,
                        ["IsDuplex"] = request.IsDuplex,
                        ["IsLive"] = request.IsLive,
                        ["ImageCount"] = request.ImageData.Length,
                    },
                    LogLevel = LogLevel.Error,
                },
                async () =>
                {
                    // Validate live capture data first
                    var validationResult = ValidateLiveDocumentCapture(request, userId);
                    if (!validationResult.IsSuccess || !validationResult.Data.IsValid)
                    {
                        throw new ValidationException(
                            validationResult.Data?.ErrorMessage ?? "Live capture validation failed",
                            new Dictionary<string, string[]>
                            {
                                ["LiveCapture"] = new[] { validationResult.Data?.ErrorMessage ?? "Invalid live capture data" }
                            });
                    }

                    // Check if document type requires duplex
                    var requiresDuplex = DocumentType.RequiresDuplexCapture(request.DocumentType);
                    if (requiresDuplex && !request.IsDuplex)
                    {
                        throw new ValidationException(
                            "This document type requires duplex capture (front and back sides)",
                            new Dictionary<string, string[]>
                            {
                                ["DocumentType"] = new[] { "Duplex capture required for this document type" }
                            });
                    }

                    var captureId = Guid.NewGuid();
                    var userDirectory = Path.Combine(_liveCaptureBasePath, request.SessionId);
                    Directory.CreateDirectory(userDirectory);

                    // Process front side
                    var sideName = request.IsDuplex ? "_front" : "";
                    var frontImageBytes = Convert.FromBase64String(request.ImageData[0].ImageData.Split(',')[1]);

                    // Read file into memory
                    byte[] frontFileData;
                    using (var ms = new MemoryStream())
                    {
                        frontFileData = frontImageBytes.ToArray();
                    }

                    // Calculate original hash before encryption for integrity verification
                    string frontOriginalHash = CalculateFileHash(frontFileData);

                    // Encrypt the document data
                    byte[] frontEncryptedData = await EncryptAsync(frontFileData);

                    // Save encrypted file data
                    var frontFilePath = Path.Combine(userDirectory, $"_{captureId}{sideName}.jpg");
                    await File.WriteAllBytesAsync(frontFilePath, frontEncryptedData);

                    string? backFilePath = null; // Track for cleanup

                    try
                    {
                        // Create live capture record
                        var liveCaptureRecord = new LiveCaptureData
                        {
                            Id = captureId,
                            UserId = userId,
                            SessionId = Guid.Parse(request.SessionId),
                            DocumentType = request.DocumentType,
                            SecureFilePath = frontFilePath,
                            FileHash = frontOriginalHash,
                            IsDuplex = request.IsDuplex,
                            DeviceFingerprint = request.CaptureMetadata.DeviceFingerprint,
                            CaptureTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(request.CaptureMetadata.Timestamp),
                            ProcessedAt = DateTime.UtcNow,
                            Status = "CAPTURED",
                            IsEncrypted = true,
                            EncryptionMethod = "DataProtection"
                        };

                        // Process back side if duplex
                        if (request.IsDuplex && !string.IsNullOrEmpty(request.ImageData[1].ImageData))
                        {
                            var backImageBytes = Convert.FromBase64String(request.ImageData[1].ImageData.Split(',')[1]);

                            // Read file into memory
                            byte[] backFileData;
                            using (var ms = new MemoryStream())
                            {
                                backFileData = backImageBytes.ToArray();
                            }

                            // Calculate original hash before encryption for integrity verification
                            string backOriginalHash = CalculateFileHash(backFileData);

                            // Encrypt the document data
                            byte[] backEncryptedData = await EncryptAsync(backFileData);

                            // Save encrypted file data
                            backFilePath = Path.Combine(userDirectory, $"_{captureId}_back.jpg");
                            await File.WriteAllBytesAsync(backFilePath, backEncryptedData);

                            liveCaptureRecord.BackSideFilePath = backFilePath;
                            liveCaptureRecord.BackSideFileHash = backOriginalHash;
                        }

                        // Save to database (this has its own resilience)
                        var saveResult = await SaveLiveCaptureRecordAsync(liveCaptureRecord);
                        if (!saveResult.IsSuccess)
                        {
                            throw new DatabaseException($"Failed to save live capture record: {saveResult.ErrorMessage}");
                        }

                        var response = new LiveCaptureResponse
                        {
                            CaptureId = captureId,
                            Status = "PROCESSED",
                            ProcessedAt = DateTime.UtcNow
                        };

                        return response;
                    }
                    catch (Exception)
                    {
                        // Clean up files if database save fails
                        try
                        {
                            if (File.Exists(frontFilePath))
                                File.Delete(frontFilePath);
                            if (backFilePath != null && File.Exists(backFilePath))
                                File.Delete(backFilePath);
                        }
                        catch (Exception cleanupEx)
                        {
                            await _loggingService.LogTraceAsync(
                                $"Failed to cleanup live capture files: {cleanupEx.Message}",
                                level: LogLevel.Warning);
                        }
                        throw; // Re-throw the original exception
                    }
                })
                .WithMongoDbWriteResilience()
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1))
                .WithContext("CaptureType", "Live Document")
                .OnSuccess(async (response) =>
                {
                    await _loggingService.LogTraceAsync(
                        $"Live document capture processed successfully: {response.CaptureId}",
                        "ProcessLiveDocumentCapture",
                        LogLevel.Information);
                })
                .OnError(async (ex) =>
                {
                    await _loggingService.LogTraceAsync(
                        $"Live document capture failed for user {userId}: {ex.Message}",
                        "ProcessLiveDocumentCapture",
                        LogLevel.Error,
                        requiresResolution: true);
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<LiveCaptureResponse>> ProcessLiveSelfieCaptureAsync(
            Guid userId,
            LiveSelfieCaptureRequest request)
        {
            return await _resilienceService.CreateBuilder<LiveCaptureResponse>(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.KYC",
                    FileName = "LiveCaptureService",
                    OperationName = "ProcessLiveSelfieCaptureAsync(Guid userId, LiveSelfieCaptureRequest request)",
                    State = new()
                    {
                        ["UserId"] = userId,
                        ["SessionId"] = request.SessionId,
                        ["DocumentType"] = "selfie",
                        ["IsLive"] = request.IsLive,
                        ["CaptureTimestamp"] = request.CaptureMetadata.Timestamp,
                    },
                    LogLevel = LogLevel.Error,
                },
                async () =>
                {
                    // Validate live capture data first
                    var validationResult = ValidateLiveSelfieCaptureAsync(request, userId);
                    if (!validationResult.IsSuccess || !validationResult.Data.IsValid)
                    {
                        throw new ValidationException(
                            validationResult.Data?.ErrorMessage ?? "Live selfie capture validation failed",
                            new Dictionary<string, string[]>
                            {
                                ["LiveSelfieCapture"] = new[] { validationResult.Data?.ErrorMessage ?? "Invalid live selfie capture data" }
                            });
                    }

                    var captureId = Guid.NewGuid();
                    var userDirectory = Path.Combine(_liveCaptureBasePath, request.SessionId);
                    Directory.CreateDirectory(userDirectory);

                    // Process selfie image
                    var filePath = Path.Combine(userDirectory, $"{captureId}_selfie.jpg");
                    var imageBytes = Convert.FromBase64String(request.ImageData.ImageData.Split(',')[1]);

                    // Read file into memory
                    byte[] fileData;
                    using (var ms = new MemoryStream())
                    {
                        fileData = imageBytes.ToArray();
                    }

                    // Calculate original hash before encryption for integrity verification
                    string originalHash = CalculateFileHash(fileData);

                    // Encrypt the document data
                    byte[] encryptedData = await EncryptAsync(fileData);

                    await File.WriteAllBytesAsync(filePath, encryptedData);

                    string? savedFilePath = filePath; // Track for cleanup

                    try
                    {
                        // Create live capture record
                        var liveCaptureRecord = new LiveCaptureData
                        {
                            Id = captureId,
                            UserId = userId,
                            SessionId = Guid.Parse(request.SessionId),
                            DocumentType = "selfie",
                            SecureFilePath = filePath,
                            FileHash = originalHash,
                            IsDuplex = false,
                            DeviceFingerprint = request.CaptureMetadata.DeviceFingerprint,
                            CaptureTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(request.CaptureMetadata.Timestamp),
                            ProcessedAt = DateTime.UtcNow,
                            Status = "CAPTURED",
                            IsEncrypted = true,
                            EncryptionMethod = "DataProtection"
                        };

                        // Save to database (this has its own resilience)
                        var saveResult = await SaveLiveCaptureRecordAsync(liveCaptureRecord);
                        if (!saveResult.IsSuccess)
                        {
                            throw new DatabaseException($"Failed to save live selfie capture record: {saveResult.ErrorMessage}");
                        }

                        var response = new LiveCaptureResponse
                        {
                            CaptureId = captureId,
                            Status = "PROCESSED",
                            ProcessedAt = DateTime.UtcNow
                        };

                        return response;
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
                                await _loggingService.LogTraceAsync(
                                    $"Failed to cleanup selfie capture file {savedFilePath}: {cleanupEx.Message}",
                                    level: LogLevel.Warning);
                            }
                        }
                        throw; // Re-throw the original exception
                    }
                })
                .WithMongoDbWriteResilience()
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30))
                .WithContext("CaptureType", "Live Selfie")
                .OnSuccess(async (response) =>
                {
                    await _loggingService.LogTraceAsync(
                        $"Live selfie capture processed successfully: {response.CaptureId}",
                        "ProcessLiveSelfieCapture",
                        LogLevel.Information);
                })
                .OnError(async (ex) =>
                {
                    await _loggingService.LogTraceAsync(
                        $"Live selfie capture failed for user {userId}: {ex.Message}",
                        "ProcessLiveSelfieCapture",
                        LogLevel.Error,
                        requiresResolution: true);
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<LiveCaptureData>> GetLiveCaptureAsync(Guid captureId, Guid? userId = null)
        {
            return await _resilienceService.CreateBuilder(
                 new Scope
                 {
                     NameSpace = "Infrastructure.Services.KYC",
                     FileName = "LiveCaptureService",
                     OperationName = "GetLiveCaptureAsync(Guid captureId, Guid? userId = null)",
                     State = new()
                     {
                         ["CaptureId"] = captureId,
                         ["UserId"] = userId,
                     }
                 },
                 async () =>
                 {
                     var filter = Builders<LiveCaptureData>.Filter.Eq(d => d.Id, captureId);
                
                    if (userId.HasValue)
                    {
                        filter = Builders<LiveCaptureData>.Filter.And(filter,
                            Builders<LiveCaptureData>.Filter.Eq(d => d.UserId, userId.Value));
                    }

                    var capture = await GetOneAsync(filter);
                    if (capture == null)
                    {
                         throw new ResourceNotFoundException($"Failed to get live capture", captureId.ToString());
                    }

                    return capture.Data;
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<List<LiveCaptureData>>> GetSessionLiveCapturesAsync(Guid sessionId, Guid userId)
        {
            return await _resilienceService.CreateBuilder(
                 new Scope
                 {
                     NameSpace = "Infrastructure.Services.KYC",
                     FileName = "LiveCaptureService",
                     OperationName = "GetSessionLiveCapturesAsync(Guid sessionId, Guid userId)",
                     State = new()
                     {
                         ["SessionId"] = sessionId,
                         ["UserId"] = userId,
                     }
                 },
                 async () =>
                 {
                     var filter = Builders<LiveCaptureData>.Filter.And(
                    Builders<LiveCaptureData>.Filter.Eq(d => d.SessionId, sessionId),
                    Builders<LiveCaptureData>.Filter.Eq(d => d.UserId, userId));

                    var captures = await GetManyAsync(filter);
                    return captures?.Data ?? new List<LiveCaptureData>();
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<DownloadLiveCaptureDto>> DownloadLiveCaptureAsync(
            Guid captureId, 
            Guid requestingUserId, 
            bool isAdminRequest = false)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.KYC",
                    FileName = "LiveCaptureService",
                    OperationName = "DownloadDocumentAsync(Guid documentId, Guid requestingUserId, bool isAdminRequest = false)",
                    State = new()
                    {
                        ["CaptureId"] = captureId,
                        ["UserId"] = requestingUserId,
                        ["IsAdminRequest"] = isAdminRequest
                    }
                },
                async () =>
                {
                    var captureResult = await GetLiveCaptureAsync(captureId, isAdminRequest ? null : requestingUserId);
                    if (!captureResult.IsSuccess)
                    {
                        throw new ResourceNotFoundException($"Failed to get live capture", captureId.ToString());
                    }

                    var capture = captureResult.Data;
                
                    if (!File.Exists(capture.SecureFilePath))
                    {
                        throw new FileNotFoundException("Live capture physical file not found", capture.SecureFilePath);
                    }

                    List<byte[]> fileBytes = new();
                    fileBytes.Add(await File.ReadAllBytesAsync(capture.SecureFilePath));

                    List<string> fileNames = new List<string>();
                    fileNames.Add($"live-capture-{captureId}_default.jpg");

                    if (capture.IsDuplex)
                    {
                        fileBytes.Add(await File.ReadAllBytesAsync(capture.BackSideFilePath!));
                        fileNames.Add($"live-capture-{captureId}_back.jpg");
                    }

                    if (!capture.IsEncrypted)
                    {
                        await _loggingService.LogTraceAsync($"Live capture downloaded: {captureId} by user: {requestingUserId}", "DownloadLiveCapture");
                        return new DownloadLiveCaptureDto()
                        {
                            FileDatas = fileBytes,
                            ContentType = "image/jpeg",
                            FileNames = fileNames
                        };
                    }

                    // Decrypt file if it's encrypted

                    List<byte[]> decryptedFileBytes = new();
                    decryptedFileBytes.Add(await DecryptAsync(fileBytes[0]));
                    // Verify hash for document integrity
                    string currentFrontHash = CalculateFileHash(decryptedFileBytes[0]);
                    if (currentFrontHash != capture.FileHash)
                    {
                        _loggingService.LogWarning($"Live-capture integrity check failed for {capture.Id}. Hash mismatch.");
                        // We still return the document but log the integrity issue
                    }

                    if (capture.IsDuplex)
                    {
                        decryptedFileBytes.Add(await DecryptAsync(fileBytes[1]));
                        // Verify hash for document integrity
                        string currentBackHash = CalculateFileHash(decryptedFileBytes[1]);
                        if (currentBackHash != capture.BackSideFileHash)
                        {
                            _loggingService.LogWarning($"Live-capture integrity check failed for {capture.Id}. Hash mismatch.");
                            // We still return the document but log the integrity issue
                        }
                    }

                    return new DownloadLiveCaptureDto()
                    {
                        FileDatas = decryptedFileBytes,
                        ContentType = "image/jpeg",
                        FileNames = fileNames
                    };               
                })
                .WithMongoDbReadResilience()
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10))
                .WithContext("FileUpload", "KYC Document")
                .OnSuccess(async (response) =>
                {
                    await _loggingService.LogTraceAsync(
                        $"Live-capture ID {captureId} downloaded successfully: {response.FileNames}",
                        "DownloadDocument",
                        LogLevel.Information);
                })
                .OnError(async (ex) =>
                {
                    await _loggingService.LogTraceAsync(
                        $"Live-capture ID {captureId} download failed for user {requestingUserId}: {ex.Message}",
                        "DownloadDocument",
                        LogLevel.Error,
                        requiresResolution: true);
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<CrudResult<LiveCaptureData>>> DeleteLiveCaptureAsync(Guid captureId, Guid userId, string reason = "User requested deletion")
        {
            return await _resilienceService.CreateBuilder(
                 new Scope
                 {
                     NameSpace = "Infrastructure.Services.KYC",
                     FileName = "LiveCaptureService",
                     OperationName = "DeleteLiveCaptureAsync(Guid captureId, Guid userId, string reason = \"User requested deletion\")",
                     State = new()
                     {
                         ["CaptureId"] = captureId,
                         ["UserId"] = userId,
                         ["Reason"] = reason,
                     },
                     LogLevel = LogLevel.Error,
                 },
                 async () =>
                 {
                     var captureResult = await GetLiveCaptureAsync(captureId, userId);
                    if (!captureResult.IsSuccess)
                    {
                         throw new ResourceNotFoundException($"Failed to get live capture", captureId.ToString());
                    }

                    // Soft delete - update status instead of physical deletion
                    var updateFields = new Dictionary<string, object>
                    {
                        ["Status"] = "DELETED",
                        ["UpdatedAt"] = DateTime.UtcNow,
                        ["DeletionReason"] = reason
                    };

                    var updateResult = await UpdateAsync(captureId, updateFields);
                    if (updateResult == null || !updateResult.IsSuccess)
                    {
                         throw new DatabaseException($"Failed to delete live capture record: {updateResult?.ErrorMessage ?? "Update live-capture data returned null"}");
                    }

                    await _loggingService.LogTraceAsync($"Live capture soft deleted: {captureId}, Reason: {reason}", "DeleteLiveCapture");
                    return updateResult.Data;
                })
                .ExecuteAsync();
        }
        public async Task<ResultWrapper<bool>> CleanupExpiredFilesAsync()
        {
            return await _resilienceService.CreateBuilder(
                 new Scope
                 {
                     NameSpace = "Infrastructure.Services.KYC",
                     FileName = "LiveCaptureService",
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
                     var deletedDocumentsFilter = Builders<LiveCaptureData>.Filter.And(
                         Builders<LiveCaptureData>.Filter.Eq(d => d.Status, "DELETED"),
                         Builders<LiveCaptureData>.Filter.Lt(d => d.CreatedAt, cutoffDate));

                     var deletedDocuments = await GetManyAsync(deletedDocumentsFilter);

                     foreach (var doc in deletedDocuments?.Data ?? new List<LiveCaptureData>())
                     {
                         var filePath = doc.SecureFilePath;
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

                     await Task.WhenAll(cleanupTasks);

                     await _loggingService.LogTraceAsync($"File cleanup completed. Deleted {deletedCount} files.", "CleanupExpiredFiles");
                     return true;
                 })
                .ExecuteAsync();
        }
        public async Task<ResultWrapper<LiveCaptureStatistics>> GetDocumentStatisticsAsync()
        {
            return await _resilienceService.CreateBuilder(
                 new Scope
                 {
                     NameSpace = "Infrastructure.Services.KYC",
                     FileName = "LiveCaptureService",
                     OperationName = "GetDocumentStatisticsAsync()",
                     State = []
                 },
                 async () =>
                 {
                     var today = DateTime.UtcNow.Date;
                    var todayFilter = Builders<DocumentData>.Filter.Gte(d => d.CreatedAt, today);

                    var totalLiveCaptures = (int)await _repository.CountAsync(Builders<LiveCaptureData>.Filter.Empty);
                    var liveCapturesFilter = Builders<LiveCaptureData>.Filter.Gte(d => d.ProcessedAt, today);
                    var liveCapturesToday = (int)await _repository.CountAsync(liveCapturesFilter);

                    // Calculate storage usage (simplified)
                    var allDocuments = await _repository.GetAllAsync();
                    var totalStorageUsed = allDocuments?.Sum(d => d.FileSize) ?? 0;

                    var statistics = new LiveCaptureStatistics
                    {
                        TotalLiveCaptures = totalLiveCaptures,
                        LiveCapturesToday = liveCapturesToday,
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
        private ResultWrapper<ValidationResult> ValidateLiveSelfieCaptureAsync(LiveSelfieCaptureRequest request, Guid userId)
        {
            var errors = new List<string>();

            // Validate session
            if (!Guid.TryParse(request.SessionId, out _))
            {
                errors.Add("Invalid session ID");
            }

            // Validate live detection
            if (!request.IsLive)
            {
                errors.Add("Selfie does not appear to be captured live");
            }

            // Validate device fingerprint
            if (string.IsNullOrEmpty(request.CaptureMetadata.DeviceFingerprint))
            {
                errors.Add("Missing device fingerprint");
            }

            // Validate timestamp
            var captureTime = DateTimeOffset.FromUnixTimeMilliseconds(request.CaptureMetadata.Timestamp);
            if (DateTime.UtcNow - captureTime > TimeSpan.FromMinutes(5))
            {
                errors.Add("Capture timestamp too old");
            }

            // Validate image data
            try
            {
                var fileLength = Convert.FromBase64String(request.ImageData.ImageData.Split(',')[1]).Length;
                var above1MB = fileLength >= 1024;
                var below10MB = fileLength <= 10 * 1024; // 10 MB limit
                if (!above1MB || !below10MB) // Minimum and maximum reasonable size
                {
                    errors.Add("Selfie image too small");
                }
            }
            catch
            {
                errors.Add("Invalid selfie image format");
            }

            var result = new ValidationResult
            {
                IsValid = !errors.Any(),
                ErrorMessage = string.Join("; ", errors),
                Issues = errors
            };

            return ResultWrapper<ValidationResult>.Success(result);
        }
        
        private ResultWrapper<ValidationResult> ValidateLiveDocumentCapture(LiveDocumentCaptureRequest request, Guid userId)
        {
            var errors = new List<string>();

            // Validate session
            if (!Guid.TryParse(request.SessionId, out _))
            {
                errors.Add("Invalid session ID");
            }

            // Validate live detection
            if (!request.IsLive && DocumentType.LiveCaptureRequired.Contains(request.DocumentType))
            {
                errors.Add("Document does not appear to be captured live");
            }

            // Validate device fingerprint
            if (string.IsNullOrEmpty(request.CaptureMetadata.DeviceFingerprint))
            {
                errors.Add("Missing device fingerprint");
            }

            // Validate timestamp
            var captureTime = DateTimeOffset.FromUnixTimeMilliseconds(request.CaptureMetadata.Timestamp);
            if (DateTime.UtcNow - captureTime > TimeSpan.FromMinutes(5))
            {
                errors.Add("Capture timestamp too old");
            }

            // Validate image data
            try
            {
                var allAbove1000KB = request.ImageData.All(i => Convert.FromBase64String(i.ImageData.Split(',')[1]).Length >= 1000);
                if (!allAbove1000KB) // Minimum reasonable size
                {
                    errors.Add("Image data too small");
                }
            }
            catch
            {
                errors.Add("Invalid image data format");
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
                await _loggingService.LogTraceAsync("Error encrypting document data", level: Domain.Constants.Logging.LogLevel.Error);
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

    }
}