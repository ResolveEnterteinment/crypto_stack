using Application.Contracts.Requests.KYC;
using Application.Contracts.Responses.KYC;
using Application.Interfaces.Base;
using Application.Interfaces.KYC;
using Application.Interfaces.Logging;
using Domain.Constants;
using Domain.Constants.KYC;
using Domain.Constants.Logging;
using Domain.DTOs;
using Domain.DTOs.KYC;
using Domain.Models.KYC;
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
    public class DocumentService : IDocumentService
    {
        private readonly ICrudRepository<DocumentRecord> _documentRepository;
        private readonly ICrudRepository<LiveCaptureRecord> _liveCaptureRepository;
        private readonly ILoggingService _logger;
        private readonly IConfiguration _configuration;
        private readonly IDataProtector _dataProtector;
        private readonly string _uploadBasePath;
        private readonly string _liveCaptureBasePath;
        private readonly string[] _allowedFileTypes;
        private readonly long _maxFileSize;
        private readonly string[] _allowedImageTypes = { ".jpg", ".jpeg", ".png", ".webp" };
        private readonly string[] _allowedDocumentTypes = { ".pdf" };

        public DocumentService(
            ICrudRepository<DocumentRecord> documentRepository,
            ICrudRepository<LiveCaptureRecord> liveCaptureRepository,
            ILoggingService logger,
            IConfiguration configuration,
            IDataProtectionProvider dataProtectionProvider)
        {
            _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
            _liveCaptureRepository = liveCaptureRepository ?? throw new ArgumentNullException(nameof(liveCaptureRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _dataProtector = dataProtectionProvider.CreateProtector("KYC.Documents");

            _uploadBasePath = _configuration["FileUpload:KycDocuments"] ?? 
                Path.Combine(Directory.GetCurrentDirectory(), "secure", "uploads", "kyc");
            _liveCaptureBasePath = _configuration["FileUpload:LiveCaptures"] ?? 
                Path.Combine(Directory.GetCurrentDirectory(), "secure", "live-captures");
            
            _allowedFileTypes = _configuration.GetSection("FileUpload:AllowedTypes").Get<string[]>() ?? 
                new[] { ".jpg", ".jpeg", ".png", ".pdf", ".webp" };
            _maxFileSize = _configuration.GetValue("FileUpload:MaxFileSizeBytes", 10 * 1024 * 1024); // 10MB default

            // Ensure directories exist
            Directory.CreateDirectory(_uploadBasePath);
            Directory.CreateDirectory(_liveCaptureBasePath);
        }

        public async Task<ResultWrapper<DocumentRecord>> SaveDocumentRecordAsync(DocumentRecord documentRecord)
        {
            try
            {
                if (documentRecord == null)
                {
                    return ResultWrapper<DocumentRecord>.Failure(FailureReason.ValidationError, "Document record cannot be null");
                }

                // Validate required fields
                if (documentRecord.UserId == Guid.Empty || documentRecord.SessionId == Guid.Empty)
                {
                    return ResultWrapper<DocumentRecord>.Failure(FailureReason.ValidationError, "UserId and SessionId are required");
                }

                var result = await _documentRepository.InsertAsync(documentRecord);
                if (!result.IsSuccess)
                {
                    return ResultWrapper<DocumentRecord>.Failure(FailureReason.DatabaseError, "Failed to save document record");
                }

                await _logger.LogTraceAsync($"Document record saved: {documentRecord.Id}", "SaveDocumentRecord");
                return ResultWrapper<DocumentRecord>.Success(documentRecord, "Document record saved successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error saving document record: {ex.Message}");
                return ResultWrapper<DocumentRecord>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<LiveCaptureRecord>> SaveLiveCaptureRecordAsync(LiveCaptureRecord liveCaptureRecord)
        {
            try
            {
                if (liveCaptureRecord == null)
                {
                    return ResultWrapper<LiveCaptureRecord>.Failure(FailureReason.ValidationError, "Live capture record cannot be null");
                }

                // Validate required fields
                if (liveCaptureRecord.UserId == Guid.Empty || liveCaptureRecord.SessionId == Guid.Empty)
                {
                    return ResultWrapper<LiveCaptureRecord>.Failure(FailureReason.ValidationError, "UserId and SessionId are required");
                }

                var result = await _liveCaptureRepository.InsertAsync(liveCaptureRecord);
                if (!result.IsSuccess)
                {
                    return ResultWrapper<LiveCaptureRecord>.Failure(FailureReason.DatabaseError, "Failed to save live capture record");
                }

                await _logger.LogTraceAsync($"Live capture record saved: {liveCaptureRecord.Id}", "SaveLiveCaptureRecord");
                return ResultWrapper<LiveCaptureRecord>.Success(liveCaptureRecord, "Live capture record saved successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error saving live capture record: {ex.Message}");
                return ResultWrapper<LiveCaptureRecord>.FromException(ex);
            }
        }

        // Modify your existing UploadDocumentAsync method
        public async Task<ResultWrapper<DocumentUploadResponse>> UploadDocumentAsync(
            Guid userId,
            Guid sessionId,
            IFormFile file,
            string documentType)
        {
            // Keep your existing validation code

            try
            {
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
                byte[] encryptedData = await EncryptDocumentAsync(fileData);

                // Create secure filename
                string secureFileName = $"{Guid.NewGuid()}_{sessionId}";

                // Save encrypted file data
                string filePath = Path.Combine(_uploadBasePath, secureFileName);
                await File.WriteAllBytesAsync(filePath, encryptedData);

                // Create document record with encryption info
                var documentRecord = new DocumentRecord
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

                // Save record to database
                var saveResult = await SaveDocumentRecordAsync(documentRecord);
                if (!saveResult.IsSuccess)
                {
                    // Clean up if record save fails
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }

                    return ResultWrapper<DocumentUploadResponse>.Failure(
                        saveResult.Reason,
                        "Failed to save document record");
                }

                return ResultWrapper<DocumentUploadResponse>.Success(new DocumentUploadResponse
                {
                    DocumentId = documentRecord.Id,
                    Status = documentRecord.Status,
                    UploadedAt = documentRecord.CreatedAt
                });
            }
            catch (Exception ex)
            {
                await _logger.LogTraceAsync($"Error uploading document for user {userId}", level: LogLevel.Error);
                return ResultWrapper<DocumentUploadResponse>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<LiveCaptureResponse>> ProcessLiveDocumentCaptureAsync(
            Guid userId,
            LiveDocumentCaptureRequest request)
        {
            try
            {
                // Validate live capture data
                var validationResult = ValidateLiveDocumentCapture(request, userId);
                if (!validationResult.IsSuccess || !validationResult.Data.IsValid)
                {
                    return ResultWrapper<LiveCaptureResponse>.Failure(FailureReason.ValidationError,
                        validationResult.Data?.ErrorMessage ?? "Live capture validation failed");
                }

                // Check if document type requires duplex
                var requiresDuplex = DocumentType.RequiresDuplexCapture(request.DocumentType);

                if (requiresDuplex && !request.IsDuplex)
                {
                    return ResultWrapper<LiveCaptureResponse>.Failure(FailureReason.ValidationError,
                        "This document type requires duplex capture (front and back sides)");
                }

                var captureId = Guid.NewGuid();
                var userDirectory = Path.Combine(_liveCaptureBasePath, userId.ToString());
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
                byte[] frontEncryptedData = await EncryptDocumentAsync(frontFileData);

                // Create secure filename
                string frontSecureFileName = $"{Guid.NewGuid()}_{request.SessionId}";

                // Save encrypted file data
                var frontFilePath = Path.Combine(userDirectory, $"_{captureId}{sideName}.jpg");
                string? backFilePath = null; // Initialize back file path;
                await File.WriteAllBytesAsync(frontFilePath, frontEncryptedData);

                // Create live capture record
                var liveCaptureRecord = new LiveCaptureRecord
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
                    byte[] backEncryptedData = await EncryptDocumentAsync(backFileData);


                    // Save encrypted file data
                    backFilePath = Path.Combine(userDirectory, $"_{captureId}_back.jpg");
                    await File.WriteAllBytesAsync(backFilePath, backEncryptedData);

                    liveCaptureRecord.BackSideFilePath = backFilePath;
                    liveCaptureRecord.BackSideFileHash = backOriginalHash;
                }                


                // Save to database
                var saveResult = await SaveLiveCaptureRecordAsync(liveCaptureRecord);
                if (!saveResult.IsSuccess)
                {
                    // Clean up files if database save failed
                    try
                    {
                        File.Delete(frontFilePath);
                        if (backFilePath != null) File.Delete(backFilePath);
                    }
                    catch { }
                    return ResultWrapper<LiveCaptureResponse>.Failure(saveResult.Reason, saveResult.ErrorMessage);
                }

                await _logger.LogTraceAsync($"Live document capture processed successfully: {captureId}", "ProcessLiveDocumentCapture");

                var response = new LiveCaptureResponse
                {
                    CaptureId = captureId,
                    Status = "PROCESSED",
                    ProcessedAt = DateTime.UtcNow
                };

                return ResultWrapper<LiveCaptureResponse>.Success(response, "Live document capture processed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing live document capture: {ex.Message}");
                return ResultWrapper<LiveCaptureResponse>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<LiveCaptureResponse>> ProcessLiveSelfieCaptureAsync(
            Guid userId,
            LiveSelfieCaptureRequest request)
        {
            try
            {
                // Validate live capture data
                var validationResult = ValidateLiveSelfieCaptureAsync(request, userId);
                if (!validationResult.IsSuccess || !validationResult.Data.IsValid)
                {
                    return ResultWrapper<LiveCaptureResponse>.Failure(FailureReason.ValidationError,
                        validationResult.Data?.ErrorMessage ?? "Live selfie capture validation failed");
                }

                var captureId = Guid.NewGuid();
                var userDirectory = Path.Combine(_liveCaptureBasePath, userId.ToString());
                Directory.CreateDirectory(userDirectory);

                // Process front side
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
                byte[] encryptedData = await EncryptDocumentAsync(fileData);

                await File.WriteAllBytesAsync(filePath, encryptedData);

                // Create live capture record
                var liveCaptureRecord = new LiveCaptureRecord
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

                // Save to database
                var saveResult = await SaveLiveCaptureRecordAsync(liveCaptureRecord);
                if (!saveResult.IsSuccess)
                {
                    // Clean up files if database save failed
                    try
                    {
                        File.Delete(filePath);
                    }
                    catch { }
                    return ResultWrapper<LiveCaptureResponse>.Failure(saveResult.Reason, saveResult.ErrorMessage);
                }

                await _logger.LogTraceAsync($"Live selfie capture processed successfully: {captureId}", "ProcessLiveSelfieCapture");

                var response = new LiveCaptureResponse
                {
                    CaptureId = captureId,
                    Status = "PROCESSED",
                    ProcessedAt = DateTime.UtcNow
                };

                return ResultWrapper<LiveCaptureResponse>.Success(response, "Live selfie capture processed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing live capture: {ex.Message}");
                return ResultWrapper<LiveCaptureResponse>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<DocumentRecord>> GetDocumentAsync(Guid documentId, Guid? userId = null)
        {
            try
            {
                var filter = Builders<DocumentRecord>.Filter.Eq(d => d.Id, documentId);
                
                if (userId.HasValue)
                {
                    filter = Builders<DocumentRecord>.Filter.And(filter,
                        Builders<DocumentRecord>.Filter.Eq(d => d.UserId, userId.Value));
                }

                var document = await _documentRepository.GetOneAsync(filter);
                if (document == null)
                {
                    return ResultWrapper<DocumentRecord>.Failure(FailureReason.NotFound, "Document not found");
                }

                return ResultWrapper<DocumentRecord>.Success(document);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrieving document: {ex.Message}");
                return ResultWrapper<DocumentRecord>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<LiveCaptureRecord>> GetLiveCaptureAsync(Guid captureId, Guid? userId = null)
        {
            try
            {
                var filter = Builders<LiveCaptureRecord>.Filter.Eq(d => d.Id, captureId);
                
                if (userId.HasValue)
                {
                    filter = Builders<LiveCaptureRecord>.Filter.And(filter,
                        Builders<LiveCaptureRecord>.Filter.Eq(d => d.UserId, userId.Value));
                }

                var capture = await _liveCaptureRepository.GetOneAsync(filter);
                if (capture == null)
                {
                    return ResultWrapper<LiveCaptureRecord>.Failure(FailureReason.NotFound, "Live capture not found");
                }

                return ResultWrapper<LiveCaptureRecord>.Success(capture);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrieving live capture: {ex.Message}");
                return ResultWrapper<LiveCaptureRecord>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<List<DocumentRecord>>> GetSessionDocumentsAsync(Guid sessionId, Guid userId)
        {
            try
            {
                var filter = Builders<DocumentRecord>.Filter.And(
                    Builders<DocumentRecord>.Filter.Eq(d => d.SessionId, sessionId),
                    Builders<DocumentRecord>.Filter.Eq(d => d.UserId, userId));

                var documents = await _documentRepository.GetAllAsync(filter);
                return ResultWrapper<List<DocumentRecord>>.Success(documents ?? new List<DocumentRecord>());
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrieving session documents: {ex.Message}");
                return ResultWrapper<List<DocumentRecord>>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<List<LiveCaptureRecord>>> GetSessionLiveCapturesAsync(Guid sessionId, Guid userId)
        {
            try
            {
                var filter = Builders<LiveCaptureRecord>.Filter.And(
                    Builders<LiveCaptureRecord>.Filter.Eq(d => d.SessionId, sessionId),
                    Builders<LiveCaptureRecord>.Filter.Eq(d => d.UserId, userId));

                var captures = await _liveCaptureRepository.GetAllAsync(filter);
                return ResultWrapper<List<LiveCaptureRecord>>.Success(captures ?? new List<LiveCaptureRecord>());
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrieving session live captures: {ex.Message}");
                return ResultWrapper<List<LiveCaptureRecord>>.FromException(ex);
            }
        }

        // Modify your existing DownloadDocumentAsync method
        public async Task<ResultWrapper<(byte[] FileData, string ContentType, string FileName)>> DownloadDocumentAsync(
            Guid documentId,
            Guid requestingUserId,
            bool isAdminRequest = false)
        {
            try
            {
                // Get document record (keep your existing retrieval code)
                var documentRecord = await GetDocumentAsync(documentId);
                if (!documentRecord.IsSuccess || documentRecord.Data == null)
                {
                    return ResultWrapper<(byte[] FileData, string ContentType, string FileName)>.Failure(
                        FailureReason.NotFound, "Document not found");
                }

                var record = documentRecord.Data;

                // Security check (keep your existing security validation)
                if (!isAdminRequest && record.UserId != requestingUserId)
                {
                    return ResultWrapper<(byte[] FileData, string ContentType, string FileName)>.Failure(
                        FailureReason.SecurityError, "Unauthorized document access");
                }

                // Read file data from storage
                string filePath = Path.Combine(_uploadBasePath, record.SecureFileName);
                if (!File.Exists(filePath))
                {
                    return ResultWrapper<(byte[] FileData, string ContentType, string FileName)>.Failure(
                        FailureReason.NotFound, "Document file not found");
                }

                byte[] fileData = await File.ReadAllBytesAsync(filePath);

                // Decrypt file if it's encrypted
                if (record.IsEncrypted)
                {
                    try
                    {
                        fileData = await DecryptDocumentAsync(fileData);

                        // Verify hash for document integrity
                        string currentHash = CalculateFileHash(fileData);
                        if (currentHash != record.FileHash)
                        {
                            _logger.LogWarning($"Document integrity check failed for {documentId}. Hash mismatch.");
                            // We still return the document but log the integrity issue
                        }
                    }
                    catch (Exception ex)
                    {
                        await _logger.LogTraceAsync($"Failed to decrypt document {documentId}", level: LogLevel.Error);
                        return ResultWrapper<(byte[] FileData, string ContentType, string FileName)>.Failure(
                            FailureReason.SecurityError, "Failed to decrypt document");
                    }
                }
                return ResultWrapper<(byte[] FileData, string ContentType, string FileName)>.Success(
                    (fileData, record.ContentType, record.OriginalFileName));
            }
            catch (Exception ex)
            {
                await _logger.LogTraceAsync($"Error downloading document {documentId}", level: LogLevel.Error);
                return ResultWrapper<(byte[] FileData, string ContentType, string FileName)>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<(List<byte[]> FileDatas, string ContentType, List<string> FileNames)>> DownloadLiveCaptureAsync(
            Guid captureId, 
            Guid requestingUserId, 
            bool isAdminRequest = false)
        {
            try
            {
                var captureResult = await GetLiveCaptureAsync(captureId, isAdminRequest ? null : requestingUserId);
                if (!captureResult.IsSuccess)
                {
                    return ResultWrapper<(List<byte[]>, string, List<string>)>.Failure(captureResult.Reason, captureResult.ErrorMessage);
                }

                var capture = captureResult.Data;
                
                if (!File.Exists(capture.SecureFilePath))
                {
                    return ResultWrapper<(List<byte[]>, string, List<string>)>.Failure(FailureReason.NotFound, "Physical file not found");
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
                    await _logger.LogTraceAsync($"Live capture downloaded: {captureId} by user: {requestingUserId}", "DownloadLiveCapture");
                    return ResultWrapper<(List<byte[]> FileDatas, string ContentType, List<string> FileNames)>.Success(
                    (fileBytes, "image / jpeg", fileNames));
                }

                // Decrypt file if it's encrypted

                try
                {
                    List<byte[]> decryptedFileBytes = new();
                    decryptedFileBytes.Add(await DecryptDocumentAsync(fileBytes[0]));
                    // Verify hash for document integrity
                    string currentFrontHash = CalculateFileHash(decryptedFileBytes[0]);
                    if (currentFrontHash != capture.FileHash)
                    {
                        _logger.LogWarning($"Live-capture integrity check failed for {capture.Id}. Hash mismatch.");
                        // We still return the document but log the integrity issue
                    }

                    if (capture.IsDuplex)
                    {
                        decryptedFileBytes.Add(await DecryptDocumentAsync(fileBytes[1]));
                        // Verify hash for document integrity
                        string currentBackHash = CalculateFileHash(decryptedFileBytes[1]);
                        if (currentBackHash != capture.BackSideFileHash)
                        {
                            _logger.LogWarning($"Live-capture integrity check failed for {capture.Id}. Hash mismatch.");
                            // We still return the document but log the integrity issue
                        }
                    }

                    return ResultWrapper<(List<byte[]> FileDatas, string ContentType, List<string> FileNames)>.Success(
                    (decryptedFileBytes, "image / jpeg", fileNames),
                    "Live capture downloaded successfully");
                }
                catch (Exception ex)
                {
                    await _logger.LogTraceAsync($"Failed to decrypt live-capture {capture.Id}: {ex.Message}", level: LogLevel.Error);
                    return ResultWrapper<(List<byte[]> FileDatas, string ContentType, List<string> FileNames)>.Failure(
                        FailureReason.SecurityError, "Failed to decrypt document");
                }                
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error downloading live capture: {ex.Message}");
                return ResultWrapper<(List<byte[]> FileDatas, string ContentType, List<string> FileNames)>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<ValidationResult>> ValidateFileAsync(IFormFile file, string documentType)
        {
            await Task.Delay(1); // Make async

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

        public ResultWrapper<ValidationResult> ValidateLiveDocumentCapture(LiveDocumentCaptureRequest request, Guid userId)
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

        public ResultWrapper<ValidationResult> ValidateLiveSelfieCaptureAsync(LiveSelfieCaptureRequest request, Guid userId)
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
                var above1000KB = Convert.FromBase64String(request.ImageData.ImageData.Split(',')[1]).Length >= 1000;
                if (!above1000KB) // Minimum reasonable size
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
        public async Task<ResultWrapper> DeleteDocumentAsync(Guid documentId, Guid userId, string reason = "User requested deletion")
        {
            try
            {
                var documentResult = await GetDocumentAsync(documentId, userId);
                if (!documentResult.IsSuccess)
                {
                    return ResultWrapper.Failure(documentResult.Reason, documentResult.ErrorMessage);
                }

                var document = documentResult.Data;

                // Soft delete - update status instead of physical deletion
                var updateFields = new Dictionary<string, object>
                {
                    ["Status"] = "DELETED",
                    ["UpdatedAt"] = DateTime.UtcNow,
                    ["DeletionReason"] = reason
                };

                var updateResult = await _documentRepository.UpdateAsync(documentId, updateFields);
                if (!updateResult.IsSuccess)
                {
                    return ResultWrapper.Failure(FailureReason.DatabaseError, "Failed to delete document record");
                }

                await _logger.LogTraceAsync($"Document soft deleted: {documentId}, Reason: {reason}", "DeleteDocument");
                return ResultWrapper.Success("Document deleted successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error deleting document: {ex.Message}");
                return ResultWrapper.FromException(ex);
            }
        }

        public async Task<ResultWrapper> DeleteLiveCaptureAsync(Guid captureId, Guid userId, string reason = "User requested deletion")
        {
            try
            {
                var captureResult = await GetLiveCaptureAsync(captureId, userId);
                if (!captureResult.IsSuccess)
                {
                    return ResultWrapper.Failure(captureResult.Reason, captureResult.ErrorMessage);
                }

                // Soft delete - update status instead of physical deletion
                var updateFields = new Dictionary<string, object>
                {
                    ["Status"] = "DELETED",
                    ["UpdatedAt"] = DateTime.UtcNow,
                    ["DeletionReason"] = reason
                };

                var updateResult = await _liveCaptureRepository.UpdateAsync(captureId, updateFields);
                if (!updateResult.IsSuccess)
                {
                    return ResultWrapper.Failure(FailureReason.DatabaseError, "Failed to delete live capture record");
                }

                await _logger.LogTraceAsync($"Live capture soft deleted: {captureId}, Reason: {reason}", "DeleteLiveCapture");
                return ResultWrapper.Success("Live capture deleted successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error deleting live capture: {ex.Message}");
                return ResultWrapper.FromException(ex);
            }
        }

        public async Task<string> GenerateFileHashAsync(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            using var sha256 = SHA256.Create();
            var hashBytes = await sha256.ComputeHashAsync(stream);
            return Convert.ToBase64String(hashBytes);
        }

        public async Task<bool> VerifyFileIntegrityAsync(string filePath, string expectedHash)
        {
            try
            {
                var actualHash = await GenerateFileHashAsync(filePath);
                return actualHash == expectedHash;
            }
            catch
            {
                return false;
            }
        }
        private async Task<byte[]> EncryptDocumentAsync(byte[] documentData)
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
                await _logger.LogTraceAsync("Error encrypting document data", level: Domain.Constants.Logging.LogLevel.Error);
                throw;
            }
        }

        private async Task<byte[]> DecryptDocumentAsync(byte[] encryptedData)
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
                await _logger.LogTraceAsync("Error decrypting document data", level: Domain.Constants.Logging.LogLevel.Error);
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

        public async Task<ResultWrapper> CleanupExpiredFilesAsync()
        {
            try
            {
                var cleanupTasks = new List<Task>();
                var deletedCount = 0;

                // Clean up old temporary files (older than 30 days)
                var cutoffDate = DateTime.UtcNow.AddDays(-30);

                // Clean up documents marked as deleted
                var deletedDocumentsFilter = Builders<DocumentRecord>.Filter.And(
                    Builders<DocumentRecord>.Filter.Eq(d => d.Status, "DELETED"),
                    Builders<DocumentRecord>.Filter.Lt(d => d.CreatedAt, cutoffDate));

                var deletedDocuments = await _documentRepository.GetAllAsync(deletedDocumentsFilter);
                
                foreach (var doc in deletedDocuments ?? new List<DocumentRecord>())
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
                                _logger.LogError($"Failed to delete file {filePath}: {ex.Message}");
                            }
                        }));
                    }
                }

                await Task.WhenAll(cleanupTasks);

                await _logger.LogTraceAsync($"File cleanup completed. Deleted {deletedCount} files.", "CleanupExpiredFiles");
                return ResultWrapper.Success($"Cleanup completed. Deleted {deletedCount} files.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error during file cleanup: {ex.Message}");
                return ResultWrapper.FromException(ex);
            }
        }

        public async Task<ResultWrapper<DocumentStatistics>> GetDocumentStatisticsAsync()
        {
            try
            {
                var today = DateTime.UtcNow.Date;
                var todayFilter = Builders<DocumentRecord>.Filter.Gte(d => d.CreatedAt, today);

                var totalDocuments = (int)await _documentRepository.CountAsync(Builders<DocumentRecord>.Filter.Empty);
                var totalLiveCaptures = (int)await _liveCaptureRepository.CountAsync(Builders<LiveCaptureRecord>.Filter.Empty);
                var documentsToday = (int)await _documentRepository.CountAsync(todayFilter);
                var liveCapturesFilter = Builders<LiveCaptureRecord>.Filter.Gte(d => d.ProcessedAt, today);
                var liveCapturesToday = (int)await _liveCaptureRepository.CountAsync(liveCapturesFilter);

                // Calculate storage usage (simplified)
                var allDocuments = await _documentRepository.GetAllAsync();
                var totalStorageUsed = allDocuments?.Sum(d => d.FileSize) ?? 0;

                var statistics = new DocumentStatistics
                {
                    TotalDocuments = totalDocuments,
                    TotalLiveCaptures = totalLiveCaptures,
                    DocumentsToday = documentsToday,
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

                return ResultWrapper<DocumentStatistics>.Success(statistics);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting document statistics: {ex.Message}");
                return ResultWrapper<DocumentStatistics>.FromException(ex);
            }
        }

        // Private helper methods

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