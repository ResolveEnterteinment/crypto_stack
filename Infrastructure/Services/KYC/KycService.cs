using Application.Interfaces.Base;
using Application.Interfaces.KYC;
using Application.Interfaces.Logging;
using Domain.Constants;
using Domain.Constants.KYC;
using Domain.DTOs;
using Domain.DTOs.KYC;
using Domain.Exceptions;
using Domain.Models.KYC;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Infrastructure.Services.KYC
{
    public class KycService : IKycService
    {
        private readonly IDocumentService _documentService;
        private readonly ILoggingService _logger;
        private readonly ICrudRepository<KycData> _repository;
        private readonly ICrudRepository<KycSessionData> _sessionRepository;
        private readonly ICrudRepository<KycAuditLog> _auditRepository;
        private readonly INotificationService _notificationService;
        private readonly IDataProtector _dataProtector;
        private readonly IConfiguration _configuration;
        private readonly string _encryptionKey;
        private readonly TimeSpan _sessionTimeout;

        public KycService(
            IDocumentService documentService,
            ICrudRepository<KycData> repository,
            ICrudRepository<KycSessionData> sessionRepository,
            ICrudRepository<KycAuditLog> auditRepository,
            ILoggingService logger,
            INotificationService notificationService,
            IDataProtectionProvider dataProtectionProvider,
            IConfiguration configuration)
        {
            _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
            _auditRepository = auditRepository ?? throw new ArgumentNullException(nameof(auditRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _dataProtector = dataProtectionProvider.CreateProtector("KYC.PersonalData");
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            _encryptionKey = _configuration["KYC:EncryptionKey"] ?? throw new InvalidOperationException("KYC encryption key not configured");
            _sessionTimeout = TimeSpan.FromHours(_configuration.GetValue("KYC:SessionTimeoutHours", 24));
        }


        // TO-DO: This function will only return KycData with sensible user information Decrypted!!! For other KycStatusResponse requirements make a new function that returns limited public information
        public async Task<ResultWrapper<KycData>> GetUserKycDataDecryptedAsync(Guid userId, string? statusFilter = null)
        {
            try
            {
                await LogAuditEvent(userId, "GetKycStatus", "User KYC status requested");

                var filters = new List<FilterDefinition<KycData>>
                {
                    Builders<KycData>.Filter.Eq(k => k.UserId, userId)
                };

                if (!string.IsNullOrWhiteSpace(statusFilter) && IsValidKycStatus(statusFilter))
                {
                    filters.Add(Builders<KycData>.Filter.Eq(k => k.Status, statusFilter.ToUpperInvariant()));
                }

                var kycData = await _repository.GetAllAsync(Builders<KycData>.Filter.And(filters));

                if (kycData.Count == 0)
                {
                    // Create new KYC entry if not found
                    var newKycData = new KycData
                    {
                        UserId = userId,
                        Status = KycStatus.NotStarted,
                        VerificationLevel = KycLevel.None,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        History = new List<KycHistoryEntry>(),
                        SecurityFlags = new KycSecurityFlags { RequiresReview = false }
                    };

                    var createResult = await _repository.InsertAsync(newKycData);
                    if (!createResult.IsSuccess)
                    {
                        throw new DatabaseException("Failed to create KYC record");
                    }

                    return ResultWrapper<KycData>.Success(newKycData);
                }

                // Return the most recent KYC data
                var latestKyc = kycData.OrderByDescending(k => k.UpdatedAt).First();

                // Decrypt sensitive data if needed
                if (latestKyc.EncryptedPersonalData != null)
                {
                    latestKyc.PersonalData = DecryptPersonalData(latestKyc.EncryptedPersonalData);
                }

                return ResultWrapper<KycData>.Success(latestKyc);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting KYC status for user {userId}: {ex.Message}");
                await LogAuditEvent(userId, "GetKycStatus", $"Error: {ex.Message}");
                return ResultWrapper<KycData>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<KycStatusDto>> GetUserKycStatusAsync(Guid userId)
        {
            try
            {
                await LogAuditEvent(userId, "GetKycStatus", "User KYC status requested");

                var filters = new List<FilterDefinition<KycData>>
                {
                    Builders<KycData>.Filter.Eq(k => k.UserId, userId),
                    Builders<KycData>.Filter.Eq(k => k.Status, KycStatus.Approved) // Only fetch approved status for this method
                };

                var kycData = await _repository.GetAllAsync(Builders<KycData>.Filter.And(filters));

                if (kycData.Count == 0)
                {
                    /*
                    // Create new KYC entry if not found
                    var newKycData = new KycData
                    {
                        UserId = userId,
                        Status = KycStatus.NotStarted,
                        VerificationLevel = KycLevel.None,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        History = new List<KycHistoryEntry>(),
                        SecurityFlags = new KycSecurityFlags { RequiresReview = false }
                    };

                    var createResult = await _repository.InsertAsync(newKycData);
                    if (!createResult.IsSuccess)
                    {
                        throw new DatabaseException("Failed to create KYC record");
                    }
                    */

                    return ResultWrapper<KycStatusDto>.Success(new KycStatusDto { 
                        Status = KycStatus.NotStarted,
                        VerificationLevel = KycLevel.None
                    });
                }

                // Return the most recent KYC data
                var latestKyc = kycData.OrderByDescending(k => VerificationLevel.GetIndex(k.VerificationLevel)).First();

                return ResultWrapper<KycStatusDto>.Success(KycStatusDto.FromKycData(latestKyc));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting KYC status for user {userId}: {ex.Message}");
                await LogAuditEvent(userId, "GetKycStatus", $"Error: {ex.Message}");
                return ResultWrapper<KycStatusDto>.FromException(ex);
            }
        }
        public async Task<ResultWrapper<KycSessionData>> GetOrCreateUserSessionAsync(Guid userId, string verificationLevel)
        {
            try
            {
                // Input validation
                if (userId == Guid.Empty)
                {
                    throw new ArgumentException("Invalid user ID", nameof(userId));
                }

                if (!IsValidKycLevel(verificationLevel))
                {
                    throw new ArgumentException("Invalid verification level", nameof(verificationLevel));
                }

                await LogAuditEvent(userId, "CreateSession", $"Session requested for level: {verificationLevel}");

                // Check for existing active session
                var existingSession = await GetActiveSession(userId);
                if (existingSession != null)
                {
                    // Extend session if within timeout
                    if (existingSession.ExpiresAt > DateTime.UtcNow)
                    {
                        existingSession.ExpiresAt = DateTime.UtcNow.Add(_sessionTimeout);
                        await _sessionRepository.UpdateAsync(existingSession.Id, new Dictionary<string, object>
                        {
                            ["ExpiresAt"] = existingSession.ExpiresAt,
                            ["UpdatedAt"] = DateTime.UtcNow
                        });

                        return ResultWrapper<KycSessionData>.Success(existingSession);
                    }
                    else
                    {
                        // Mark expired session as expired
                        await _sessionRepository.UpdateAsync(existingSession.Id, new Dictionary<string, object>
                        {
                            ["Status"] = "EXPIRED",
                            ["UpdatedAt"] = DateTime.UtcNow
                        });
                    }
                }

                // Create new session
                var sessionData = new KycSessionData
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    SessionId = GenerateSecureSessionId(),
                    Status = "ACTIVE",
                    VerificationLevel = verificationLevel,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.Add(_sessionTimeout),
                    SecurityContext = new SessionSecurityContext
                    {
                        IpAddress = GetClientIpAddress(),
                        UserAgent = GetUserAgent(),
                        CreatedAt = DateTime.UtcNow
                    }
                };

                var createResult = await _sessionRepository.InsertAsync(sessionData);
                if (!createResult.IsSuccess)
                {
                    throw new DatabaseException("Failed to create KYC session");
                }

                await LogAuditEvent(userId, "SessionCreated", $"Session {sessionData.SessionId} created");

                return ResultWrapper<KycSessionData>.Success(sessionData);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error creating KYC session for user {userId}: {ex.Message}");
                await LogAuditEvent(userId, "CreateSession", $"Error: {ex.Message}");
                return ResultWrapper<KycSessionData>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<KycData>> VerifyAsync(KycVerificationRequest request)
        {
            try
            {
                // Enhanced input validation
                var validationResult = ValidateVerificationRequest(request);
                if (!validationResult.IsValid)
                {
                    return ResultWrapper<KycData>.Failure(FailureReason.ValidationError, validationResult.ErrorMessage);
                }

                await LogAuditEvent(request.UserId, "VerificationStarted", "KYC verification process initiated");

                // Verify session
                var sessionResult = await ValidateSessionAsync(request.SessionId, request.UserId);
                if (!sessionResult.IsSuccess)
                {
                    return ResultWrapper<KycData>.Failure(FailureReason.ValidationError, sessionResult.ErrorMessage);
                }

                // Get or create KYC record
                var kycResult = await GetUserKycDataDecryptedAsync(request.UserId);
                if (!kycResult.IsSuccess)
                {
                    return ResultWrapper<KycData>.Failure(FailureReason.KYCFetchError, kycResult.ErrorMessage);
                }

                var kycData = kycResult.Data;

                // Process verification based on level
                var verificationResult = await ProcessVerification(request);
                if (!verificationResult.IsSuccess)
                {
                    await LogAuditEvent(request.UserId, "VerificationFailed", verificationResult.ErrorMessage);
                    return ResultWrapper<KycData>.Failure(FailureReason.KYCFetchError, verificationResult.ErrorMessage);
                }

                // Update KYC record with verification results
                await UpdateKycWithVerificationResults(kycData, request, verificationResult.Data);

                await LogAuditEvent(request.UserId, "VerificationCompleted",
                    $"KYC verification completed with status: {kycData.Status}");

                // Send notification
                await SendVerificationNotification(kycData);

                return ResultWrapper<KycData>.Success(kycData);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in KYC verification: {ex.Message}");
                await LogAuditEvent(request.UserId, "VerificationError", $"Error: {ex.Message}");
                return ResultWrapper<KycData>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<bool>> IsUserVerifiedAsync(Guid userId, string requiredLevel = KycLevel.Standard)
        {
            try
            {
                var kycResult = await GetUserKycDataDecryptedAsync(userId);
                if (!kycResult.IsSuccess)
                {
                    return ResultWrapper<bool>.Failure(kycResult.Reason, kycResult.ErrorMessage);
                }

                var kycData = kycResult.Data;

                // Check verification status and level
                var isVerified = kycData.Status == KycStatus.Approved &&
                                GetKycLevelValue(kycData.VerificationLevel) >= GetKycLevelValue(requiredLevel) &&
                                !IsVerificationExpired(kycData);

                return ResultWrapper<bool>.Success(isVerified);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error checking user verification: {ex.Message}");
                return ResultWrapper<bool>.FromException(ex);
            }
        }

        public async Task<ResultWrapper> UpdateKycStatusAsync(Guid userId, string status, string? adminUserId = null, string? reason = null)
        {
            try
            {
                if (!IsValidKycStatus(status))
                {
                    return ResultWrapper.Failure(FailureReason.ValidationError, "Invalid KYC status provided");
                }

                var kycResult = await GetUserKycDataDecryptedAsync(userId);
                if (!kycResult.IsSuccess)
                {
                    return ResultWrapper.Failure(FailureReason.NotFound, "KYC record not found");
                }

                var kycData = kycResult.Data;
                var previousStatus = kycData.Status;

                // Update status
                kycData.Status = status;
                kycData.UpdatedAt = DateTime.UtcNow;

                if (status == KycStatus.Approved)
                {
                    kycData.VerifiedAt = DateTime.UtcNow;
                    kycData.ExpiresAt = DateTime.UtcNow.AddYears(1); // Verification expires after 1 year
                }

                // Add history entry
                var historyEntry = new KycHistoryEntry
                {
                    Timestamp = DateTime.UtcNow,
                    Action = "StatusUpdate",
                    PreviousStatus = previousStatus,
                    NewStatus = status,
                    PerformedBy = adminUserId ?? "SYSTEM",
                    Reason = reason ?? "Status updated",
                    Details = new Dictionary<string, object>
                    {
                        ["UpdatedBy"] = adminUserId ?? "SYSTEM",
                        ["UpdatedAt"] = DateTime.UtcNow
                    }
                };

                kycData.History.Add(historyEntry);

                // Update in database
                var updateFields = new Dictionary<string, object>
                {
                    ["Status"] = kycData.Status,
                    ["UpdatedAt"] = kycData.UpdatedAt,
                    ["History"] = kycData.History
                };

                if (kycData.VerifiedAt.HasValue)
                {
                    updateFields["VerifiedAt"] = kycData.VerifiedAt;
                }

                if (kycData.ExpiresAt.HasValue)
                {
                    updateFields["ExpiresAt"] = kycData.ExpiresAt;
                }

                var updateResult = await _repository.UpdateAsync(kycData.Id, updateFields);
                if (!updateResult.IsSuccess)
                {
                    throw new DatabaseException("Failed to update KYC status");
                }

                await LogAuditEvent(userId, "StatusUpdated",
                    $"Status changed from {previousStatus} to {status} by {adminUserId ?? "SYSTEM"}");

                // Send notification
                await SendStatusUpdateNotification(kycData, previousStatus);

                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating KYC status: {ex.Message}");
                await LogAuditEvent(userId, "StatusUpdateError", $"Error: {ex.Message}");
                return ResultWrapper.FromException(ex);
            }
        }

        public async Task<ResultWrapper<PaginatedResult<KycData>>> GetPendingVerificationsAsync(int page = 1, int pageSize = 20)
        {
            try
            {
                var statusFilter = Builders<KycData>.Filter.Or(
                    Builders<KycData>.Filter.Eq(k => k.Status, KycStatus.Pending),
                    Builders<KycData>.Filter.Eq(k => k.Status, KycStatus.NeedsReview)
                );

                var sort = Builders<KycData>.Sort.Descending(k => k.UpdatedAt);

                int totalCount = (int) await _repository.CountAsync(statusFilter);
                var paginatedResult = await _repository.GetPaginatedAsync(statusFilter, sort, page, pageSize);

                // Decrypt personal data for admin view
                foreach (var item in paginatedResult.Items)
                {
                    if (item.EncryptedPersonalData != null)
                    {
                        item.PersonalData = DecryptPersonalData(item.EncryptedPersonalData);
                    }
                }

                var result = new PaginatedResult<KycData>
                {
                    Items = paginatedResult.Items,
                    Page = page,
                    PageSize = pageSize,
                    TotalCount = totalCount
                };

                return ResultWrapper<PaginatedResult<KycData>>.Success(result);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting pending verifications: {ex.Message}");
                return ResultWrapper<PaginatedResult<KycData>>.FromException(ex);
            }
        }

        public async Task<ResultWrapper> PerformAmlCheckAsync(Guid userId)
        {
            try
            {
                await LogAuditEvent(userId, "AmlCheckStarted", "AML check initiated");

                var kycResult = await GetUserKycDataDecryptedAsync(userId);
                if (!kycResult.IsSuccess)
                {
                    return ResultWrapper.Failure(FailureReason.NotFound, "KYC record not found");
                }

                var kycData = kycResult.Data;

                // Perform internal AML checks
                var amlResult = await PerformInternalAmlChecks(kycData);

                // Update KYC record with AML results
                var updateFields = new Dictionary<string, object>
                {
                    ["AmlCheckDate"] = DateTime.UtcNow,
                    ["AmlStatus"] = amlResult.Status,
                    ["AmlRiskScore"] = amlResult.RiskScore,
                    ["UpdatedAt"] = DateTime.UtcNow
                };

                if (amlResult.RiskLevel == "HIGH")
                {
                    updateFields["SecurityFlags.RequiresReview"] = true;
                    updateFields["SecurityFlags.HighRiskIndicators"] = amlResult.RiskIndicators;
                }

                await _repository.UpdateAsync(kycData.Id, updateFields);

                await LogAuditEvent(userId, "AmlCheckCompleted",
                    $"AML check completed with status: {amlResult.Status}, Risk: {amlResult.RiskLevel}");

                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error performing AML check: {ex.Message}");
                await LogAuditEvent(userId, "AmlCheckError", $"Error: {ex.Message}");
                return ResultWrapper.FromException(ex);
            }
        }

        public async Task<ResultWrapper<bool>> IsUserEligibleForTrading(Guid userId)
        {
            try
            {
                var verificationResult = await IsUserVerifiedAsync(userId, KycLevel.Standard);
                if (!verificationResult.IsSuccess)
                {
                    return verificationResult;
                }

                if (!verificationResult.Data)
                {
                    return ResultWrapper<bool>.Success(false);
                }

                // Additional checks for trading eligibility
                var kycResult = await GetUserKycDataDecryptedAsync(userId);
                if (!kycResult.IsSuccess)
                {
                    return ResultWrapper<bool>.Success(false);
                }

                var kycData = kycResult.Data;

                // Check if user has any security flags that prevent trading
                var isEligible = kycData.SecurityFlags?.RequiresReview != true &&
                                kycData.AmlStatus != "BLOCKED" &&
                                !IsVerificationExpired(kycData);

                return ResultWrapper<bool>.Success(isEligible);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error checking trading eligibility: {ex.Message}");
                return ResultWrapper<bool>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<KycSessionData>> ValidateSessionAsync(Guid sessionId, Guid userId)
        {
            try
            {
                var filter = Builders<KycSessionData>.Filter.And(
                    Builders<KycSessionData>.Filter.Eq(s => s.Id, sessionId),
                    Builders<KycSessionData>.Filter.Eq(s => s.UserId, userId),
                    Builders<KycSessionData>.Filter.Eq(s => s.Status, "ACTIVE"),
                    Builders<KycSessionData>.Filter.Gt(s => s.ExpiresAt, DateTime.UtcNow)
                );

                var sessions = await _sessionRepository.GetAllAsync(filter);
                var session = sessions?.FirstOrDefault();

                if (session == null)
                {
                    return ResultWrapper<KycSessionData>.Failure(FailureReason.NotFound, "Session not found or expired");
                }

                // Update last accessed time and security context
                var updateFields = new Dictionary<string, object>
                {
                    ["UpdatedAt"] = DateTime.UtcNow,
                    ["SecurityContext.LastAccessedAt"] = DateTime.UtcNow,
                    ["SecurityContext.IpAddress"] = GetClientIpAddress(),
                    ["SecurityContext.UserAgent"] = GetUserAgent()
                };

                await _sessionRepository.UpdateAsync(session.Id, updateFields);

                await LogAuditEvent(userId, "SessionValidated", $"Session {sessionId} validated successfully");

                return ResultWrapper<KycSessionData>.Success(session);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error validating KYC session {sessionId} for user {userId}: {ex.Message}");
                await LogAuditEvent(userId, "SessionValidationError", $"Error: {ex.Message}");
                return ResultWrapper<KycSessionData>.FromException(ex);
            }
        }

        public async Task<ResultWrapper> InvalidateSessionAsync(Guid sessionId, Guid userId, string reason = "Manual invalidation")
        {
            try
            {
                var updateFields = new Dictionary<string, object>
                {
                    ["Status"] = "EXPIRED",
                    ["UpdatedAt"] = DateTime.UtcNow,
                    ["CompletedAt"] = DateTime.UtcNow
                };

                var updateResult = await _sessionRepository.UpdateAsync(sessionId, updateFields);
                if (!updateResult.IsSuccess)
                {
                    return ResultWrapper.Failure(FailureReason.DatabaseError, "Failed to invalidate session");
                }

                await LogAuditEvent(userId, "SessionInvalidated", $"Session {sessionId} invalidated: {reason}");
                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error invalidating session {sessionId}: {ex.Message}");
                return ResultWrapper.FromException(ex);
            }
        }

        public async Task<ResultWrapper> UpdateSessionProgressAsync(Guid sessionId, SessionProgress progress)
        {
            try
            {
                var updateFields = new Dictionary<string, object>
                {
                    ["Progress"] = progress,
                    ["UpdatedAt"] = DateTime.UtcNow
                };

                var updateResult = await _sessionRepository.UpdateAsync(sessionId, updateFields);
                return updateResult.IsSuccess
                    ? ResultWrapper.Success()
                    : ResultWrapper.Failure(FailureReason.DatabaseError, "Failed to update session progress");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating session progress: {ex.Message}");
                return ResultWrapper.FromException(ex);
            }
        }

        // Private helper methods

        private async Task<KycSessionData?> GetActiveSession(Guid userId)
        {
            var filter = Builders<KycSessionData>.Filter.And(
                Builders<KycSessionData>.Filter.Eq(s => s.UserId, userId),
                Builders<KycSessionData>.Filter.Eq(s => s.Status, "ACTIVE"),
                Builders<KycSessionData>.Filter.Gt(s => s.ExpiresAt, DateTime.UtcNow)
            );

            var sessions = await _sessionRepository.GetAllAsync(filter);
            return sessions?.FirstOrDefault();
        }

        private ValidationResult ValidateVerificationRequest(KycVerificationRequest request)
        {
            var errors = new List<string>();

            if (request.UserId == Guid.Empty)
                errors.Add("Invalid user ID");

            if (request.SessionId == Guid.Empty)
                errors.Add("Invalid session ID");

            if (!IsValidKycLevel(request.VerificationLevel))
                errors.Add("Invalid verification level");

            if (request.Data == null)
                errors.Add("Verification data is required");

            // Validate personal data
            if (request.Data.TryGetValue("PersonalInfo", out var personalInfo))
            {
                var personadlInfoDict = personalInfo as Dictionary<string, object>;
                if (personadlInfoDict != null)
                {
                    var personalDataErrors = ValidatePersonalData(personadlInfoDict);
                    errors.AddRange(personalDataErrors);
                }
            }

            return new ValidationResult
            {
                IsValid = !errors.Any(),
                ErrorMessage = string.Join("; ", errors)
            };
        }

        private List<string> ValidatePersonalData(Dictionary<string, object> personalInfo)
        {
            var errors = new List<string>();

            // Name validation
            if (personalInfo.TryGetValue("firstName", out var firstName))
            {
                if (!IsValidName(firstName?.ToString()))
                    errors.Add("Invalid first name");
            }

            if (personalInfo.TryGetValue("lastName", out var lastName))
            {
                if (!IsValidName(lastName?.ToString()))
                    errors.Add("Invalid last name");
            }

            // Date of birth validation
            if (personalInfo.TryGetValue("dateOfBirth", out var dobObj))
            {
                if (!DateTime.TryParse(dobObj?.ToString(), out var dob) || !IsValidDateOfBirth(dob))
                    errors.Add("Invalid date of birth");
            }

            // Document number validation
            if (personalInfo.TryGetValue("documentNumber", out var docNum))
            {
                if (!IsValidDocumentNumber(docNum?.ToString()))
                    errors.Add("Invalid document number");
            }

            return errors;
        }

        private bool IsValidName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            // Only allow letters, spaces, hyphens, and apostrophes
            return Regex.IsMatch(name, @"^[a-zA-Z\s\-']+$") && name.Length >= 2 && name.Length <= 50;
        }

        private bool IsValidDateOfBirth(DateTime dateOfBirth)
        {
            var age = DateTime.UtcNow.Year - dateOfBirth.Year;
            if (dateOfBirth > DateTime.UtcNow.AddYears(-age)) age--;

            return age >= 18 && age <= 120;
        }

        private bool IsValidDocumentNumber(string? docNumber)
        {
            if (string.IsNullOrWhiteSpace(docNumber))
                return false;

            // Only allow alphanumeric characters
            return Regex.IsMatch(docNumber, @"^[a-zA-Z0-9]+$") && docNumber.Length >= 5 && docNumber.Length <= 20;
        }

        private async Task<ResultWrapper<VerificationResult>> ProcessVerification(KycVerificationRequest request)
        {
            var verificationResult = new VerificationResult
            {
                UserId = request.UserId,
                VerificationLevel = request.VerificationLevel,
                ProcessedAt = DateTime.UtcNow,
                Checks = new List<VerificationCheck>()
            };

            // Document verification

            if (request.VerificationLevel == VerificationLevel.Basic)
            {
                var data = BasicKycDataRequest.FromDictionary(request.Data);
                // With the following code to properly map the dictionary to a BasicKycDataRequest object:
                if (data == null || data.PersonalInfo == null)
                {
                    verificationResult.Checks.Add(new VerificationCheck
                    {
                        CheckType = "PERSONAL_INFO",
                        CheckName = "Personal Info Verification",
                        ProcessedAt = DateTime.UtcNow,
                        Passed = false,
                        FailureReason = "Invalid or incomplete verificaiton data",
                        Details = new Dictionary<string, object>()
                    });
                }
                else
                {
                    // Add successful validation check
                    verificationResult.Checks.Add(new VerificationCheck
                    {
                        CheckType = "PERSONAL_INFO",
                        CheckName = "Personal Info Verification",
                        ProcessedAt = DateTime.UtcNow,
                        Passed = true,
                        Details = new Dictionary<string, object>()
                    });
                }
            }

            if (request.VerificationLevel == VerificationLevel.Standard)
            {
                var data = StandardKycDataRequest.FromDictionary(request.Data);
                // With the following code to properly map the dictionary to a BasicKycDataRequest object:
                if (data == null || data.PersonalInfo == null || !data.Documents.Any() || string.IsNullOrEmpty(data.SelfieHash))
                {
                    verificationResult.Checks.Add(new VerificationCheck
                    {
                        CheckType = "PERSONAL_INFO",
                        CheckName = "Personal Info Verification",
                        ProcessedAt = DateTime.UtcNow,
                        Passed = false,
                        FailureReason = "Invalid or incomplete verificaiton data",
                        Details = new Dictionary<string, object>()
                    });
                }
                else
                {
                    // Add successful validation check
                    verificationResult.Checks.Add(new VerificationCheck
                    {
                        CheckType = "PERSONAL_INFO",
                        CheckName = "Personal Info Verification",
                        ProcessedAt = DateTime.UtcNow,
                        Passed = true,
                        Details = new Dictionary<string, object>()
                    });
                }

                if (data.Documents.Any())
                {
                    var documentCheck = await VerifyDocuments(data.Documents);
                    verificationResult.Checks.Add(documentCheck);
                }
            }
            

            // Determine overall status
            var failedChecks = verificationResult.Checks.Where(c => !c.Passed).ToList();
            if (!failedChecks.Any())
            {
                verificationResult.Status = "PASSED";
            }
            else
            {
                verificationResult.Status = "FAILED";
                verificationResult.FailureReasons = failedChecks.Select(c => c.FailureReason).ToList();
            }

            return ResultWrapper<VerificationResult>.Success(verificationResult);
        }

        private async Task<VerificationCheck> VerifyDocuments(IEnumerable<KycDocument> documents)
        {
            var documentCount = 0;
            var issues = new List<string>();

            if (documents == null || !documents.Any())
            {
                issues.Add("No documents provided for verification");
                return new VerificationCheck
                {
                    CheckType = "DOCUMENT",
                    CheckName = "Document Verification",
                    ProcessedAt = DateTime.UtcNow,
                    Score = 0.0,
                    Passed = false,
                    FailureReason = "No documents provided for verification",
                    Details = new Dictionary<string, object>
                    {
                        ["DocumentCount"] = 0,
                        ["Issues"] = issues
                    }
                };
            }

            foreach (var doc in documents)
            {
                documentCount++;

                // Check if document was live captured
                var isLiveCapture = doc.IsLiveCapture;

                if (!isLiveCapture && DocumentType.LiveCaptureRequired.Contains(doc.Type))
                {
                    issues.Add("Non-live captured document detected");
                }
            }

            var check = new VerificationCheck
            {
                CheckType = "DOCUMENT",
                CheckName = "Document Verification",
                ProcessedAt = DateTime.UtcNow,
                Passed = issues.Count() == 0,
                Details = new Dictionary<string, object>
                {
                    ["DocumentCount"] = documentCount,
                    ["Issues"] = issues
                }
            };

            if (!check.Passed)
            {
                check.FailureReason = string.Join("; ", issues);
            }

            return check;
        }

        private async Task UpdateKycWithVerificationResults(KycData kycData, KycVerificationRequest request, VerificationResult verificationResult)
        {
            // Update KYC status based on verification results
            if (verificationResult.Status == "PASSED")
            {
                kycData.Status = KycStatus.Pending;
                kycData.VerificationLevel = request.VerificationLevel;
            }
            else
            {
                kycData.Status = KycStatus.NeedsReview;
                kycData.SecurityFlags ??= new KycSecurityFlags();
                kycData.SecurityFlags.RequiresReview = true;
                kycData.SecurityFlags.FailureReasons = verificationResult.FailureReasons;
            }

            var previousData = DecryptPersonalData(kycData.EncryptedPersonalData) ?? new();

            // Encrypt and store personal data
            if (request.Data.TryGetValue("personalInfo", out object? personalInfo))
            {
                using var newDocument = JsonDocument.Parse(JsonConvert.SerializeObject(personalInfo));

                foreach (var property in newDocument.RootElement.EnumerateObject())
                {
                    previousData[property.Name] = ConvertJsonElement(property.Value);
                }

                kycData.EncryptedPersonalData = EncryptPersonalData(previousData);
            }

            // Store verification results
            kycData.VerificationResults = verificationResult;
            kycData.UpdatedAt = DateTime.UtcNow;

            // Add history entry
            var historyEntry = new KycHistoryEntry
            {
                Timestamp = DateTime.UtcNow,
                Action = "VerificationProcessed",
                NewStatus = kycData.Status,
                Session = request.SessionId,
                PerformedBy = "SYSTEM",
                Details = new Dictionary<string, object>
                {
                    ["VerificationLevel"] = request.VerificationLevel,
                    ["ChecksPassed"] = verificationResult.Checks.Count(c => c.Passed),
                    ["TotalChecks"] = verificationResult.Checks.Count,
                }
            };

            kycData.History.Add(historyEntry);

            var documents = await _documentService.GetSessionDocumentsAsync(request.SessionId, request.UserId);
            var liveCaptures = await _documentService.GetSessionLiveCapturesAsync(request.SessionId, request.UserId);

            // Update in database
            var updateFields = new Dictionary<string, object>
            {
                ["Status"] = kycData.Status,
                ["VerificationLevel"] = kycData.VerificationLevel,
                ["EncryptedPersonalData"] = kycData.EncryptedPersonalData,
                ["VerificationResults"] = kycData.VerificationResults,
                ["Documents"] = documents.Data?.Select(d => d.Id).ToList() ?? [],
                ["LiveCaptures"] = liveCaptures.Data?.Select(d => d.Id).ToList() ?? [],
                ["UpdatedAt"] = kycData.UpdatedAt,
                ["History"] = kycData.History,
                ["SecurityFlags"] = kycData.SecurityFlags
            };

            if (kycData.VerifiedAt.HasValue)
            {
                updateFields["VerifiedAt"] = kycData.VerifiedAt;
            }

            if (kycData.ExpiresAt.HasValue)
            {
                updateFields["ExpiresAt"] = kycData.ExpiresAt;
            }

            var updateResult = await _repository.UpdateAsync(kycData.Id, updateFields);
            if (updateResult == null || !updateResult.IsSuccess || updateResult.ModifiedCount == 0)
            {
                throw new DatabaseException("Failed to update KYC record with verification results");
            }
        }

        private async Task<AmlResult> PerformInternalAmlChecks(KycData kycData)
        {
            await Task.Delay(200); // Simulate processing time

            var amlResult = new AmlResult
            {
                Status = "CLEARED",
                RiskLevel = "LOW",
                RiskScore = 15.0,
                CheckedAt = DateTime.UtcNow,
                RiskIndicators = new List<string>()
            };

            // Simple risk assessment based on internal rules
            if (kycData.PersonalData != null)
            {
                // Check for high-risk patterns (simplified)
                var riskFactors = 0;

                // Add risk factors based on various criteria
                if (kycData.History.Count(h => h.Action == "VerificationProcessed") > 3)
                {
                    riskFactors++;
                    amlResult.RiskIndicators.Add("Multiple verification attempts");
                }

                if (riskFactors > 2)
                {
                    amlResult.RiskLevel = "HIGH";
                    amlResult.RiskScore = 75.0;
                    amlResult.Status = "REVIEW_REQUIRED";
                }
                else if (riskFactors > 0)
                {
                    amlResult.RiskLevel = "MEDIUM";
                    amlResult.RiskScore = 35.0;
                }
            }

            return amlResult;
        }

        private string EncryptPersonalData(object personalData)
        {
            return _dataProtector.Protect(JsonConvert.SerializeObject(personalData));
        }

        private Dictionary<string, object>? DecryptPersonalData(string encryptedData)
        {
            try
            {
                var json = _dataProtector.Unprotect(encryptedData);
                using var document = JsonDocument.Parse(json);
                var result = new Dictionary<string, object>();

                foreach (var property in document.RootElement.EnumerateObject())
                {
                    result[property.Name] = ConvertJsonElement(property.Value);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error decrypting personal data: {ex.Message}");
                return null;
            }
        }

        private object ConvertJsonElement(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return element.GetString() ?? string.Empty;
                case JsonValueKind.Number:
                    if (element.TryGetInt32(out int intValue))
                        return intValue;
                    if (element.TryGetInt64(out long longValue))
                        return longValue;
                    return element.GetDouble();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Null:
                    return null;
                case JsonValueKind.Object:
                    var obj = new Dictionary<string, object>();
                    foreach (var property in element.EnumerateObject())
                    {
                        obj[property.Name] = ConvertJsonElement(property.Value);
                    }
                    return obj;
                case JsonValueKind.Array:
                    var array = new List<object>();
                    foreach (var item in element.EnumerateArray())
                    {
                        array.Add(ConvertJsonElement(item));
                    }
                    return array;
                default:
                    return null;
            }
        }

        private string GenerateSecureSessionId()
        {
            using var rng = RandomNumberGenerator.Create();
            var bytes = new byte[32];
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        private bool IsValidKycLevel(string level)
        {
            return !string.IsNullOrWhiteSpace(level) &&
                   new[] { KycLevel.Basic, KycLevel.Standard, KycLevel.Advanced }.Contains(level);
        }

        private bool IsValidKycStatus(string status)
        {
            return !string.IsNullOrWhiteSpace(status) &&
                   KycStatus.AllValues.Contains(status.ToUpperInvariant());
        }

        private int GetKycLevelValue(string level)
        {
            return level switch
            {
                KycLevel.Basic => 1,
                KycLevel.Standard => 2,
                KycLevel.Advanced => 3,
                _ => 0
            };
        }

        private bool IsVerificationExpired(KycData kycData)
        {
            return kycData.ExpiresAt.HasValue && kycData.ExpiresAt.Value <= DateTime.UtcNow;
        }

        private async Task LogAuditEvent(Guid userId, string action, string details)
        {
            var auditLog = new KycAuditLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Action = action,
                Details = details,
                Timestamp = DateTime.UtcNow,
                IpAddress = GetClientIpAddress(),
                UserAgent = GetUserAgent()
            };

            await _auditRepository.InsertAsync(auditLog);
        }

        private string GetClientIpAddress()
        {
            // This would be injected from HttpContext in a real implementation
            return "127.0.0.1";
        }

        private string GetUserAgent()
        {
            // This would be injected from HttpContext in a real implementation
            return "KYC-Service/1.0";
        }

        private async Task SendVerificationNotification(KycData kycData)
        {
            var notificationMessage = kycData.Status switch
            {
                KycStatus.Approved => "Your identity verification has been approved.",
                KycStatus.Rejected => "Your identity verification was not approved. Please contact support.",
                KycStatus.NeedsReview => "Your identity verification is under review. We'll notify you once completed.",
                _ => "Your identity verification status has been updated."
            };

            await _notificationService.CreateAndSendNotificationAsync(new NotificationData
            {
                UserId = kycData.UserId.ToString(),
                Message = notificationMessage
            });
        }

        private async Task SendStatusUpdateNotification(KycData kycData, string previousStatus)
        {
            await _notificationService.CreateAndSendNotificationAsync(new NotificationData
            {
                UserId = kycData.UserId.ToString(),
                Message = $"Your verification status has been updated from {previousStatus} to {kycData.Status}."
            });
        }
    }

}