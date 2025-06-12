// Infrastructure/Services/KYC/BaseKycService.cs
using Application.Interfaces.Base;
using Application.Interfaces.KYC;
using Application.Interfaces.Logging;
using Domain.Constants;
using Domain.Constants.KYC;
using Domain.DTOs;
using Domain.DTOs.KYC;
using Domain.Exceptions;
using Domain.Models.KYC;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using System.Text.Json;

namespace Infrastructure.Services.KYC
{
    public class KycService : IKycService
    {
        protected readonly ILoggingService Logger;
        protected readonly ICrudRepository<KycData> Repository;
        private readonly ICrudRepository<KycSessionData> _kycSessionRepository;
        private readonly HttpClient _httpClient;
        private readonly string _openSanctionsApiKey;
        private readonly string _webhookSecret;
        private readonly INotificationService _notificationService;

        public KycService(
            ICrudRepository<KycData> repository,
            ICrudRepository<KycSessionData> kycSessionRepository,
            ILoggingService logger,
            HttpClient httpClient,
            IConfiguration configuration,
            INotificationService notificationService
            )
        {
            Repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _kycSessionRepository = kycSessionRepository ?? throw new ArgumentNullException(nameof(kycSessionRepository));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _openSanctionsApiKey = configuration["MinimalKyc:OpenSanctionsApiKey"] ?? "";
            _webhookSecret = configuration["MinimalKyc:WebhookSecret"] ?? Guid.NewGuid().ToString();
            _notificationService = notificationService;
        }

        public async Task<ResultWrapper<KycData>> GetUserKycStatusAsync(Guid userId, string? statusFilter = null)
        {
            try
            {
                List<FilterDefinition<KycData>> filter = new([Builders<KycData>.Filter.Eq(k => k.UserId, userId)]);

                if (!string.IsNullOrWhiteSpace(statusFilter) && KycStatus.AllValues.Contains(statusFilter.ToUpperInvariant()))
                    filter.Add(Builders<KycData>.Filter.Eq(k => k.Status, statusFilter.ToUpperInvariant()));

                // Retrieve data without sorting in the database query
                List<KycData> kycData = await Repository.GetAllAsync(Builders<KycData>.Filter.And(filter));

                // Sort in-memory after retrieving the data
                kycData = kycData.OrderByDescending(s => KycLevelValue(s.Status)).ToList();

                if (kycData == null || kycData.Count == 0)
                {
                    // Create new KYC entry if not found
                    KycData newKycData = new()
                    {
                        UserId = userId,
                        Status = KycStatus.NotStarted,
                        VerificationLevel = KycLevel.None,
                        CreatedAt = DateTime.UtcNow
                    };

                    return ResultWrapper<KycData>.Success(newKycData);
                }

                return ResultWrapper<KycData>.Success(kycData[0]);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting KYC status: {ex.Message}");
                return ResultWrapper<KycData>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<KycSessionData>> GetOrCreateUserSessionAsync(Guid userId, string verificationLevel)
        {
            try
            {
                if (userId == Guid.Empty)
                {
                    throw new ArgumentException("User ID cannot be empty.", nameof(userId));
                }

                var filter = Builders<KycSessionData>.Filter.And(
                    Builders<KycSessionData>.Filter.Eq(x => x.UserId, userId),
                    Builders<KycSessionData>.Filter.Eq(x => x.Status, KycStatus.InProgress),
                    Builders<KycSessionData>.Filter.Gt(x => x.ExpiresAt, DateTime.UtcNow) // Ensure session hasn't expired
                );

                var sessionData = await _kycSessionRepository.GetOneAsync(filter);

                if (sessionData is null)
                {
                    var newKycSession = new KycSessionData
                    {
                        UserId = userId,
                        VerificationLevel = verificationLevel,
                        ExpiresAt = DateTime.UtcNow.AddHours(2),
                        Status = KycStatus.InProgress
                    };

                    var session = await _kycSessionRepository.InsertAsync(newKycSession);
                    return session.IsSuccess
                        ? ResultWrapper<KycSessionData>.Success(newKycSession)
                        : ResultWrapper<KycSessionData>.Failure(FailureReason.DatabaseError, session.ErrorMessage);
                }

                return ResultWrapper<KycSessionData>.Success(sessionData);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting or creating user KYC session: {ex.Message}");
                return ResultWrapper<KycSessionData>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<KycData>> VerifyAsync(KycVerificationRequest request)
        {
            try
            {
                // First check if user already has KYC in progress
                var filter = Builders<KycData>.Filter.Eq(k => k.UserId, request.UserId);

                List<KycData> allKyc = await Repository.GetAllAsync(filter);

                if (allKyc.Count > 0)
                {
                    var isDuplicate = allKyc.FindAll(k => k.VerificationLevel == request.VerificationLevel && k.Status is KycStatus.InProgress or KycStatus.Pending).Count > 0;

                    // Don't allow new verification if one is already in progress or approved
                    if (isDuplicate)
                    {
                        return ResultWrapper<KycData>.Failure(
                            FailureReason.ValidationError,
                            "KYC verification already in progress");
                    }

                    allKyc.Sort((a, b) => KycLevelValue(a.VerificationLevel).CompareTo(KycLevelValue(b.VerificationLevel)));

                    var isAlreadyVerified = allKyc[0].Status == KycStatus.Approved &&
                        KycLevelValue(allKyc[0].VerificationLevel) >= KycLevelValue(request.VerificationLevel);

                    if (isAlreadyVerified)
                    {
                        return ResultWrapper<KycData>.Failure(
                            FailureReason.ValidationError,
                            "User is already verified at an equal or higher level");
                    }
                }

                var kycData = new KycData
                {
                    UserId = request.UserId,
                    Status = KycStatus.Pending,
                    VerificationLevel = request.VerificationLevel,
                    VerificationData = request.Data,
                    SubmittedAt = DateTime.UtcNow
                };

                KycHistoryEntry history = new()
                {
                    Action = "Verification Started",
                    SessionId = request.SessionId,
                    Status = KycStatus.InProgress,
                    PerformedBy = request.UserId.ToString(),
                    Details = new Dictionary<string, object>
                    {
                        ["VerificationLevel"] = request.VerificationLevel,
                    }
                };

                kycData.History.Add(history);

                // Insert new KYC data
                var insertResult = await Repository.InsertAsync(kycData);
                if (insertResult == null || !insertResult.IsSuccess)
                {
                    throw new DatabaseException(insertResult?.ErrorMessage ?? "KYC insert result returned null.");
                }

                Logger.LogInformation($"Initiated KYC verification level {request.VerificationLevel} for user {request.UserId}");

                return ResultWrapper<KycData>.Success(kycData);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error initiating KYC verification: {ex.Message}");
                return ResultWrapper<KycData>.FromException(ex);
            }
        }

        public async Task<ResultWrapper> PerformAmlCheckAsync(Guid userId)
        {
            try
            {
                // Get current KYC data
                ResultWrapper<KycData> kycResult = await GetUserKycStatusAsync(userId);
                if (!kycResult.IsSuccess)
                {
                    return ResultWrapper.Failure(kycResult.Reason, kycResult.ErrorMessage);
                }

                KycData kycData = kycResult.Data;

                Logger.LogInformation($"Performing AML check for user {userId}");

                var userData = ExtractUserDataForAmlCheck(kycData.VerificationData);
                var (isHighRisk, isPoliticallyExposed, riskScore) = await CheckOpenSanctions(userData);

                var updateFields = new Dictionary<string, object>
                {
                    ["IsPoliticallyExposed"] = isPoliticallyExposed,
                    ["IsHighRisk"] = isHighRisk,
                    ["RiskScore"] = riskScore,
                    ["AdditionalInfo"] = new Dictionary<string, object>
                    {
                        ["AmlCheckDate"] = DateTime.UtcNow,
                        ["AmlProvider"] = "OpenSanctions"
                    }
                };

                if (isHighRisk)
                {
                    updateFields["Status"] = KycStatus.NeedsReview;
                }

                var history = new KycHistoryEntry
                {
                    Action = "AML Check Completed",
                    Status = isHighRisk ? KycStatus.NeedsReview : kycData.Status,
                    PerformedBy = "SYSTEM",
                    Details = new Dictionary<string, object>
                    {
                        ["RiskScore"] = riskScore,
                        ["IsPoliticallyExposed"] = isPoliticallyExposed,
                        ["IsHighRisk"] = isHighRisk,
                        ["Provider"] = "OpenSanctions"
                    }
                };

                kycData.History.Add(history);
                updateFields["History"] = kycData.History;

                _ = await Repository.UpdateAsync(kycData.Id, updateFields);

                return ResultWrapper.Success();

            }
            catch (Exception ex)
            {
                Logger.LogError($"Error performing AML check: {ex.Message}");
                return ResultWrapper.FromException(ex);
            }
        }

        public async Task<ResultWrapper> UpdateKycStatusAsync(Guid userId, string status, string? comment = null)
        {
            try
            {
                // Get current KYC data
                ResultWrapper<KycData> kycResult = await GetUserKycStatusAsync(userId);
                if (kycResult == null || !kycResult.IsSuccess)
                {
                    return ResultWrapper.Failure(kycResult?.Reason ?? FailureReason.DatabaseError, kycResult?.ErrorMessage ?? "KYC result returned null.");
                }

                KycData kycData = kycResult.Data;

                // Update status
                Dictionary<string, object> updateFields = new()
                {
                    ["Status"] = status.ToUpperInvariant(),
                    ["LastCheckedAt"] = DateTime.UtcNow
                };

                if (status == KycStatus.Approved)
                {
                    updateFields["VerifiedAt"] = DateTime.UtcNow;
                }
                else if (status == KycStatus.Rejected)
                {
                    updateFields["RejectionReason"] = comment ?? "Rejected by admin";
                }

                KycHistoryEntry history = new()
                {
                    Action = "Manual Status Update",
                    Status = status,
                    PerformedBy = "ADMIN",
                    Details = new Dictionary<string, object>
                    {
                        ["PreviousStatus"] = kycData.Status,
                        ["Comment"] = comment ?? "Status manually updated"
                    }
                };

                kycData.History.Add(history);
                updateFields["History"] = kycData.History;

                var updateResult = await Repository.UpdateAsync(kycData.Id, updateFields);
                if (updateResult == null || !updateResult.IsSuccess)
                {
                    throw new DatabaseException(updateResult?.ErrorMessage ?? "KYC status update result returned null.");
                }

                _ = await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                {
                    UserId = userId.ToString(),
                    Message = $"Your verification status has been updated to {status}"
                });
                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error updating KYC status: {ex.Message}");
                return ResultWrapper.FromException(ex);
            }
        }

        public async Task<ResultWrapper<bool>> IsUserVerifiedAsync(Guid userId, string requiredLevel = KycLevel.Standard)
        {
            try
            {
                ResultWrapper<KycData> kycResult = await GetUserKycStatusAsync(userId);
                if (!kycResult.IsSuccess)
                {
                    return ResultWrapper<bool>.Failure(kycResult.Reason, kycResult.ErrorMessage);
                }

                KycData kycData = kycResult.Data;

                // Check if verified and at required level
                bool isVerified = kycData.Status == KycStatus.Approved &&
                                  KycLevelValue(kycData.VerificationLevel) >= KycLevelValue(requiredLevel);

                return ResultWrapper<bool>.Success(isVerified);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error checking user verification: {ex.Message}");
                return ResultWrapper<bool>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<PaginatedResult<KycData>>> GetPendingVerificationsAsync(int page = 1, int pageSize = 20)
        {
            try
            {
                FilterDefinition<KycData> statusFilter = Builders<KycData>.Filter.Or(
                    Builders<KycData>.Filter.Eq(k => k.Status, KycStatus.Pending),
                    Builders<KycData>.Filter.Eq(k => k.Status, KycStatus.NeedsReview)
                );

                SortDefinition<KycData> sort = Builders<KycData>.Sort.Descending(k => k.LastCheckedAt);

                PaginatedResult<KycData> pendingVerifications = await Repository.GetPaginatedAsync(
                    statusFilter, sort, page, pageSize);

                return ResultWrapper<PaginatedResult<KycData>>.Success(pendingVerifications);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting pending verifications: {ex.Message}");
                return ResultWrapper<PaginatedResult<KycData>>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<bool>> IsUserEligibleForTrading(Guid userId)
        {
            try
            {
                ResultWrapper<KycData> kycResult = await GetUserKycStatusAsync(userId);
                if (!kycResult.IsSuccess)
                {
                    return ResultWrapper<bool>.Failure(kycResult.Reason, kycResult.ErrorMessage);
                }

                KycData kycData = kycResult.Data;

                // User is eligible if:
                // 1. KYC is approved
                // 2. Not high risk
                // 3. Not from a restricted region
                bool isEligible = kycData.Status == KycStatus.Approved &&
                                 !kycData.IsHighRisk &&
                                 !kycData.IsRestrictedRegion;

                return ResultWrapper<bool>.Success(isEligible);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error checking trading eligibility: {ex.Message}");
                return ResultWrapper<bool>.FromException(ex);
            }
        }

        // Helper to convert KYC level to a numeric value for comparison
        protected static int KycLevelValue(string level)
        {
            return level switch
            {
                KycLevel.None => 0,
                KycLevel.Basic => 1,
                KycLevel.Standard => 2,
                KycLevel.Advanced => 3,
                KycLevel.Enhanced => 4,
                _ => 0
            };
        }

        private Dictionary<string, string> ExtractUserDataForAmlCheck(Dictionary<string, object> verificationData)
        {
            var result = new Dictionary<string, string>();

            if (verificationData != null &&
                verificationData.TryGetValue("personalInfo", out var personalInfo) &&
                personalInfo is Dictionary<string, object> personData)
            {
                foreach (var key in new[] { "firstName", "lastName", "birthDate", "nationality" })
                {
                    if (personData.TryGetValue(key, out var value))
                    {
                        result[key] = value.ToString();
                    }
                }
            }

            return result;
        }

        private async Task<(bool IsHighRisk, bool IsPoliticallyExposed, string RiskScore)> CheckOpenSanctions(
            Dictionary<string, string> userData)
        {
            // For production, you'd implement a call to OpenSanctions API
            // This is a simplified example that uses local data or a direct API call

            try
            {
                if (!userData.TryGetValue("firstName", out var firstName) ||
                    !userData.TryGetValue("lastName", out var lastName))
                {
                    return (false, false, "low");
                }

                var fullName = $"{firstName} {lastName}";

                // Example API call to OpenSanctions
                if (!string.IsNullOrEmpty(_openSanctionsApiKey))
                {
                    // Replace with actual OpenSanctions API endpoint
                    var queryParams = new Dictionary<string, string>
                    {
                        ["q"] = fullName,
                        ["apikey"] = _openSanctionsApiKey
                    };

                    var content = new FormUrlEncodedContent(queryParams);
                    var response = await _httpClient.PostAsync("https://api.opensanctions.org/match/default", content);

                    if (response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        var results = JsonSerializer.Deserialize<Dictionary<string, object>>(responseContent);

                        // Extract match results (simplified)
                        if (results?.TryGetValue("results", out var matchResults) == true &&
                            matchResults is JsonElement element &&
                            element.GetArrayLength() > 0)
                        {
                            return (true, true, "high");
                        }
                    }
                }

                // Fallback to local check (for development or when API unavailable)
                var sanctionedNames = new[] { "vladimir putin", "kim jong un" };
                var pepNames = new[] { "xi jinping", "joe biden" };

                var normalizedName = fullName.ToLowerInvariant();

                var isSanctioned = Array.Exists(sanctionedNames, normalizedName.Contains);
                var isPep = Array.Exists(pepNames, normalizedName.Contains);

                var riskLevel = isSanctioned ? "high" : (isPep ? "medium" : "low");

                return (isSanctioned, isPep, riskLevel);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error checking sanctions: {ex.Message}");
                return (false, false, "low");
            }
        }
    }
}