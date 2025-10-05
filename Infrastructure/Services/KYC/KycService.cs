using Application.Interfaces.KYC;
using Domain.Constants;
using Domain.Constants.KYC;
using Domain.Constants.Logging;
using Domain.DTOs;
using Domain.DTOs.KYC;
using Domain.DTOs.Logging;
using Domain.Exceptions;
using Domain.Exceptions.KYC;
using Domain.Models.KYC;
using Infrastructure.Services.Base;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using Newtonsoft.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Infrastructure.Services.KYC
{
    public class KycService : BaseService<KycData>, IKycService
    {
        private readonly IDocumentService _documentService;
        private readonly IKycAuditService _kycAuditService;
        private readonly IKycSessionService _kycSessionService;
        private readonly ILiveCaptureService _liveCaptureService;
        private readonly IDataProtector _dataProtector;
        private readonly IConfiguration _configuration;

        public KycService(
            IServiceProvider serviceProvider,
            IKycAuditService kycAuditService,
            IKycSessionService kycSessionService,
            IDocumentService documentService,
            ILiveCaptureService liveCaptureService,
            IDataProtectionProvider dataProtectionProvider,
            IConfiguration configuration) : base(serviceProvider)
        {
            _kycAuditService = kycAuditService ?? throw new ArgumentNullException(nameof(kycAuditService));
            _kycSessionService = kycSessionService ?? throw new ArgumentNullException(nameof(kycSessionService));
            _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
            _liveCaptureService = liveCaptureService ?? throw new ArgumentNullException(nameof(liveCaptureService));
            _dataProtector = dataProtectionProvider.CreateProtector("KYC.PersonalData");
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        // TO-DO: This function will only return KycData with sensible user information Decrypted!!! For other KycStatusResponse requirements make a new function that returns limited public information
        public async Task<ResultWrapper<KycData?>> GetUserKycDataDecryptedAsync(Guid userId, string? statusFilter = null, string? levelfilter = null)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.KYC",
                    FileName = "Kycrvice",
                    OperationName = "GetUserKycDataDecryptedAsync(Guid userId, string? statusFilter = null)",
                    State = {
                        ["UserId"] = userId,
                        ["StatusFilter"] = statusFilter,
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    await _kycAuditService.LogAuditEvent(userId, "GetKycStatus", "User KYC status requested");

                    var filters = new List<FilterDefinition<KycData>>
                    {
                        Builders<KycData>.Filter.Eq(k => k.UserId, userId)
                    };

                    if (!string.IsNullOrWhiteSpace(statusFilter) && IsValidKycStatus(statusFilter))
                    {
                        filters.Add(Builders<KycData>.Filter.Eq(k => k.Status, statusFilter.ToUpperInvariant()));
                    }

                    if (!string.IsNullOrWhiteSpace(levelfilter) && IsValidKycLevel(levelfilter))
                    {
                        filters.Add(Builders<KycData>.Filter.Eq(k => k.VerificationLevel, levelfilter));
                    }

                    var kycDataResult = await GetManyAsync(Builders<KycData>.Filter.And(filters));

                    if (kycDataResult == null || !kycDataResult.IsSuccess)
                        throw new DatabaseException($"Failed to fetch KYC data: {kycDataResult?.ErrorMessage ?? "Fetch result returned null"}");

                    var kycData = kycDataResult.Data;

                    if (kycData.Count == 0)
                    {
                        return null;
                    }

                    // Return the most recent KYC data
                    var latestKyc = kycData.OrderByDescending(k => k.UpdatedAt).First();

                    // Decrypt sensitive data if needed
                    if (latestKyc.EncryptedPersonalData != null)
                    {
                        latestKyc.PersonalData = DecryptPersonalData(latestKyc.EncryptedPersonalData);
                    }

                    return latestKyc;
                })
                .OnError(async (ex)=>
                {
                    await _kycAuditService.LogAuditEvent(userId, "GetKycStatus", $"Error: {ex.Message}");
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<KycDto>> GetUserKycStatusWithHighestLevelAsync(Guid userId, bool? includePending = false)
        {
            return await _resilienceService.CreateBuilder(
            new Scope
            {
                NameSpace = "Infrastructure.Services.KYC",
                FileName = "Kycrvice",
                OperationName = "GetUserKycStatusAsync(Guid userId)",
                State = {
                    ["UserId"] = userId,
                }
            },
            async () =>
            {
                await _kycAuditService.LogAuditEvent(userId, "GetKycStatus", "User KYC status requested");

                List<string> statusList = [KycStatus.Approved];

                if (includePending == true)
                    statusList.Add(KycStatus.Pending);

                var filters = new List<FilterDefinition<KycData>>
                {
                    Builders<KycData>.Filter.Eq(k => k.UserId, userId),
                    Builders<KycData>.Filter.In(k => k.Status, statusList) // Only fetch kyc records with approved status for this method
                };

                var kycDataResult = await GetManyAsync(Builders<KycData>.Filter.And(filters));
                if (kycDataResult == null || !kycDataResult.IsSuccess)
                    throw new DatabaseException($"Failed to fetch KYC data: {kycDataResult?.ErrorMessage ?? "Fetch result returned null"}");

                var kycData = kycDataResult.Data;

                if (kycData.Count == 0)
                {
                    return new KycDto
                    {
                        Status = KycStatus.NotStarted,
                        VerificationLevel = KycLevel.None
                    };
                }

                // Return the most recent KYC data
                var latestKyc = kycData.OrderByDescending(k => VerificationLevel.GetIndex(k.VerificationLevel)).First();

                return KycDto.FromKycData(latestKyc);
            })
            .OnError(async (ex) =>
                {
                    await _kycAuditService.LogAuditEvent(userId, "GetKycStatus", $"Error: {ex.Message}");
                })
            .ExecuteAsync();
        }

        public async Task<ResultWrapper<List<KycDto>>> GetUserKycStatusPerLevelAsync(Guid userId)
        {
            return await _resilienceService.CreateBuilder(
            new Scope
            {
                NameSpace = "Infrastructure.Services.KYC",
                FileName = "Kycrvice",
                OperationName = "GetUserPerLevelStatusAsync(Guid userId)",
                State = {
                    ["UserId"] = userId,
                }
            },
            async () =>
            {
                var filters = new List<FilterDefinition<KycData>>
                {
                    Builders<KycData>.Filter.Eq(k => k.UserId, userId),
                    Builders<KycData>.Filter.In(k => k.Status, [KycStatus.Approved, KycStatus.Pending]) // Only fetch kyc records with approved or pending status for this method
                };
                var kycDataResult = await GetManyAsync(Builders<KycData>.Filter.And(filters));
                if (kycDataResult == null || !kycDataResult.IsSuccess)
                    throw new DatabaseException($"Failed to fetch KYC data: {kycDataResult?.ErrorMessage ?? "Fetch result returned null"}");
                var kycData = kycDataResult.Data;
                if (kycData.Count == 0)
                {
                    return
                    [
                        new KycDto
                        {
                            Status = KycStatus.NotStarted,
                            VerificationLevel = KycLevel.None
                        }
                    ];
                }
                // Group by verification level and get the latest status per level
                var perLevelStatus = kycData
                    .GroupBy(k => k.VerificationLevel)
                    .Select(g => g.OrderByDescending(k => k.UpdatedAt).First())
                    .Select(KycDto.FromKycData)
                    .ToList();
                return perLevelStatus;
            })
            .OnError(async (ex) =>
            {
                await _kycAuditService.LogAuditEvent(userId, "GetKycPerLevelStatus", $"Error: {ex.Message}");
            })
            .ExecuteAsync();
        }

        public async Task<ResultWrapper<KycData>> GetOrCreateAsync(KycVerificationRequest request)
        {
            // Enhanced input validation
            var validationResult = ValidateVerificationRequest(request);
            if (!validationResult.IsValid)
            {
                return ResultWrapper<KycData>.Failure(FailureReason.ValidationError, validationResult.ErrorMessage);

            }

            return await _resilienceService.CreateBuilder(
             new Scope
             {
                 NameSpace = "Infrastructure.Services.KYC",
                 FileName = "Kycrvice",
                 OperationName = "VerifyAsync(KycVerificationRequest request)",
                 State = {
                    ["UserId"] = request.UserId,
                    ["SessionId"] = request.SessionId,
                    ["VerificationLevel"] = request.VerificationLevel,
                 }
             },
             async () =>
             {
                 await _kycAuditService.LogAuditEvent(request.UserId, "VerificationStarted", "KYC verification process initiated");

                 // Verify session
                 var sessionResult = await _kycSessionService.ValidateSessionAsync(request.SessionId, request.UserId);
                 if (!sessionResult.IsSuccess)
                 {
                     throw new ValidationException($"Invalid KYC session: {sessionResult.ErrorMessage}", []);
                 }

                 // Get KYC record
                 var kycResult = await GetUserKycDataDecryptedAsync(request.UserId, levelfilter: request.VerificationLevel);

                 if (kycResult == null || !kycResult.IsSuccess)
                 {
                     throw new KycVerificationException($"Failed to fetch KYC record for user {request.UserId}");
                 }

                 KycData? newKycData = null;

                 // Create if no kyc record was found
                 if (kycResult.Data == null)
                 {
                     // Create new KYC entry if not found
                     newKycData = new KycData
                     {
                         UserId = request.UserId,
                         Status = KycStatus.NotStarted,
                         VerificationLevel = KycLevel.None,
                         CreatedAt = DateTime.UtcNow,
                         UpdatedAt = DateTime.UtcNow,
                         SecurityFlags = new KycSecurityFlags { RequiresReview = false }
                     };

                     var createResult = await InsertAsync(newKycData);

                     if (createResult == null || !createResult.IsSuccess)
                     {
                         throw new DatabaseException($"Failed to create KYC record: {createResult?.ErrorMessage ?? "Insert result returned null"}");
                     }
                 }

                 var kycData = newKycData ?? kycResult.Data!;

                 // Process verification based on level
                 var verificationResult = await ProcessVerification(request);
                 if (!verificationResult.IsSuccess)
                 {
                     await _kycAuditService.LogAuditEvent(request.UserId, "VerificationFailed", verificationResult.ErrorMessage);
                     throw new ValidationException("KYC verification failed", []);
                 }

                 // Update KYC record with verification results
                 await UpdateKycWithVerificationResults(kycData, request, verificationResult.Data);

                 return kycData;
             })
                .OnSuccess(async (kycData) =>
                {
                    await _kycAuditService.LogAuditEvent(request.UserId, "VerificationCompleted",
                        $"KYC verification completed with status: {kycData.Status}");

                    await SendVerificationNotification(kycData); ;
                })
                .OnError(async (ex) =>
                {
                    await _kycAuditService.LogAuditEvent(request.UserId, "VerificationError", $"Error: {ex.Message}");
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<bool>> IsUserVerifiedAsync(Guid userId, string requiredLevel = KycLevel.Basic)
        {
            return await _resilienceService.CreateBuilder(
             new Scope
             {
                 NameSpace = "Infrastructure.Services.KYC",
                 FileName = "Kycrvice",
                 OperationName = "IsUserVerifiedAsync(Guid userId, string requiredLevel = KycLevel.Basic)",
                 State = {
                    ["UserId"] = userId,
                    ["RequiredLevel"] = requiredLevel,
                 }
             },
             async () =>
             {
                 var kycResult = await GetUserKycStatusWithHighestLevelAsync(userId);
                 if (kycResult == null || !kycResult.IsSuccess)
                 {
                     throw new KycVerificationException($"KYC record not found for user {userId}");
                 }

                 var kycData = kycResult.Data;

                 // Check verification status and level
                 var isVerified = kycData.Status == KycStatus.Approved &&
                                 GetKycLevelValue(kycData.VerificationLevel) >= GetKycLevelValue(requiredLevel) &&
                                 !IsVerificationExpired(kycData);

                 return isVerified;
             })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<CrudResult<KycData>>> UpdateKycStatusAsync(Guid userId, string verificationLevel, string status, string? adminUserId = null, string? reason = null, string? comments = null)
        {
            if (!IsValidKycStatus(status))
            {
                return ResultWrapper<CrudResult<KycData>>.Failure(FailureReason.ValidationError, "Invalid KYC status provided");
            }

            return await _resilienceService.CreateBuilder(
             new Scope
             {
                 NameSpace = "Infrastructure.Services.KYC",
                 FileName = "Kycrvice",
                 OperationName = "IsUserVerifiedAsync(Guid userId, string requiredLevel = KycLevel.Standard)",
                 State = {
                    ["UserId"] = userId,
                    ["Status"] = status,
                    ["AdminUserId"] = adminUserId,
                    ["Reason"] = reason,
                 },
                 LogLevel = LogLevel.Critical
             },
             async () =>
             {
                 var kycResult = await GetUserKycDataDecryptedAsync(userId, levelfilter: verificationLevel);
                 if (!kycResult.IsSuccess)
                 {
                     throw new ResourceNotFoundException($"KYC record not found for user {userId}", userId.ToString());
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
                     Comments = comments,
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

                 var updateResult = await UpdateAsync(kycData.Id, updateFields);
                 if (updateResult == null || !updateResult.IsSuccess || updateResult.Data == null || !updateResult.Data.IsSuccess)
                 {
                     throw new DatabaseException($"Failed to update KYC status: {updateResult?.ErrorMessage ?? "Update result returned null"}");
                 }

                 return updateResult.Data;
             })
                .OnSuccess(async(crudResult) =>
                {
                    var kycData = crudResult.Documents.First();
                    var previousStatus = kycData.History.Last().PreviousStatus;
                    await _kycAuditService.LogAuditEvent(userId, "StatusUpdated",
                     $"Status changed from {previousStatus} to {status} by {adminUserId ?? "SYSTEM"}");

                    // Send notification
                    await SendStatusUpdateNotification(kycData, previousStatus);
                })
                .OnError(async (ex) =>
                {
                    await _kycAuditService.LogAuditEvent(userId, "StatusUpdateError", $"Error: {ex.Message}");
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<PaginatedResult<KycData>>> GetPendingVerificationsAsync(int page = 1, int pageSize = 20)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                 {
                     NameSpace = "Infrastructure.Services.KYC",
                     FileName = "Kycrvice",
                     OperationName = "GetPendingVerificationsAsync(int page = 1, int pageSize = 20)",
                     State = []
                 },
                 async () =>
                 {
                     var statusFilter = Builders<KycData>.Filter.Or(
                        Builders<KycData>.Filter.Eq(k => k.Status, KycStatus.Pending),
                        Builders<KycData>.Filter.Eq(k => k.Status, KycStatus.NeedsReview)
                    );

                     var sort = Builders<KycData>.Sort.Descending(k => k.UpdatedAt);

                     var paginatedResult = await GetPaginatedAsync(statusFilter, sort, page, pageSize);

                     if(paginatedResult == null || !paginatedResult.IsSuccess)
                     {
                         throw new DatabaseException($"Failed to fetch pending verifications: {paginatedResult?.ErrorMessage ?? "Fetch result returned null"}");
                     }

                     // Decrypt personal data for admin view
                     foreach (var item in paginatedResult.Data.Items)
                     {
                         if (item.EncryptedPersonalData != null)
                         {
                             item.PersonalData = DecryptPersonalData(item.EncryptedPersonalData);
                         }
                     }

                     var result = new PaginatedResult<KycData>
                     {
                         Items = paginatedResult.Data.Items,
                         Page = page,
                         PageSize = pageSize,
                         TotalCount = paginatedResult.Data.Items.Count()
                     };

                     return result;
                 })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<AmlResult>> PerformAmlCheckAsync(Guid userId)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.KYC",
                    FileName = "Kycrvice",
                    OperationName = "PerformAmlCheckAsync(Guid userId)",
                    State =
                    {
                        ["UserId"] = userId
                    }
                },
                 async () =>
                 {
                     await _kycAuditService.LogAuditEvent(userId, "AmlCheckStarted", "AML check initiated");

                     var kycResult = await GetUserKycDataDecryptedAsync(userId);
                     if (!kycResult.IsSuccess)
                     {
                         throw new KycVerificationException($"KYC record not found for user {userId}");
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

                     var updateResult = await UpdateAsync(kycData.Id, updateFields);

                     if (updateResult == null || !updateResult.IsSuccess || updateResult.Data == null || !updateResult.Data.IsSuccess)
                     {
                         throw new DatabaseException($"Failed to update KYC AML check: {updateResult?.ErrorMessage ?? "Update result returned null"}");
                     }
                     return amlResult;
                 })
                .OnSuccess(async (amlResult) =>
                {
                    await _kycAuditService.LogAuditEvent(userId, "AmlCheckCompleted",
                         $"AML check completed with status: {amlResult.Status}, Risk: {amlResult.RiskLevel}");
                })
                .OnError(async ex =>
                {
                    await _kycAuditService.LogAuditEvent(userId, "AmlCheckError", $"Error: {ex.Message}");
                })
                .ExecuteAsync();
        }

        // Private helper methods

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

            var personalData = new Dictionary<string, object>();

            // Encrypt and store personal data
            if (request.Data.TryGetValue("personalInfo", out object? personalInfo))
            {
                using var newDocument = JsonDocument.Parse(JsonConvert.SerializeObject(personalInfo));

                foreach (var property in newDocument.RootElement.EnumerateObject())
                {
                    personalData[property.Name] = ConvertJsonElement(property.Value);
                }

                kycData.EncryptedPersonalData = EncryptPersonalData(personalData);
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
            var liveCaptures = await _liveCaptureService.GetSessionLiveCapturesAsync(request.SessionId, request.UserId);

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

            var updateResult = await UpdateAsync(kycData.Id, updateFields);
            if (updateResult == null || !updateResult.IsSuccess || updateResult.Data.ModifiedCount == 0)
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

        private string? EncryptPersonalData(object personalData)
        {
            try
            {
                return _dataProtector.Protect(JsonConvert.SerializeObject(personalData));
            }
            catch (Exception ex)
            {
                _loggingService.LogError($"Error encrypting personal data: {ex.Message}");
                return null;
            }
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
                _loggingService.LogError($"Error decrypting personal data: {ex.Message}");
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

        public int GetKycLevelValue(string level)
        {
            return level switch
            {
                KycLevel.Basic => 1,
                KycLevel.Standard => 2,
                KycLevel.Advanced => 3,
                KycLevel.Enhanced => 4,
                _ => 0
            };
        }

        private bool IsVerificationExpired(KycData kycData)
        {
            return kycData.ExpiresAt.HasValue && kycData.ExpiresAt.Value <= DateTime.UtcNow;
        }

        private bool IsVerificationExpired(KycDto kycDto)
        {
            return kycDto.ExpiresAt.HasValue && kycDto.ExpiresAt.Value <= DateTime.UtcNow;
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