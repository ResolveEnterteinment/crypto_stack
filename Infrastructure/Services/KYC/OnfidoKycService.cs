// Infrastructure/Services/KYC/OnfidoKycService.cs
using Application.Interfaces.Base;
using Application.Interfaces.KYC;
using Application.Interfaces.Logging;
using Domain.Constants;
using Domain.Constants.KYC;
using Domain.DTOs;
using Domain.DTOs.KYC;
using Domain.DTOs.Settings;
using Domain.Models.KYC;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using System.Text;

namespace Infrastructure.Services.KYC
{
    public class OnfidoKycService : IKycService
    {
        private readonly HttpClient _httpClient;
        private readonly OnfidoSettings _onfidoSettings;
        private readonly ILoggingService _logger;
        private readonly ICrudRepository<KycData> _repository;

        public OnfidoKycService(
            HttpClient httpClient,
            IOptions<OnfidoSettings> onfidoSettings,
            ILoggingService logger,
            ICrudRepository<KycData> repository)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _onfidoSettings = onfidoSettings?.Value ?? throw new ArgumentNullException(nameof(onfidoSettings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));

            _httpClient.BaseAddress = new Uri(_onfidoSettings.ApiUrl);
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", $"token={_onfidoSettings.ApiKey}");
        }

        public async Task<ResultWrapper<KycData>> GetUserKycStatusAsync(Guid userId)
        {
            try
            {
                var filter = Builders<KycData>.Filter.Eq(k => k.UserId, userId);
                var kycData = await _repository.GetOneAsync(filter);

                if (kycData == null)
                {
                    // Create new KYC entry if not found
                    var newKycData = new KycData
                    {
                        UserId = userId,
                        Status = KycStatus.NotStarted,
                        VerificationLevel = KycLevel.None,
                        CreatedAt = DateTime.UtcNow
                    };

                    await _repository.InsertAsync(newKycData);
                    return ResultWrapper<KycData>.Success(newKycData);
                }

                return ResultWrapper<KycData>.Success(kycData);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting KYC status: {ex.Message}");
                return ResultWrapper<KycData>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<KycSessionDto>> InitiateKycVerificationAsync(KycVerificationRequest request)
        {
            try
            {
                // First check if user already has KYC in progress
                var kycStatusResult = await GetUserKycStatusAsync(request.UserId);
                if (!kycStatusResult.IsSuccess)
                {
                    return ResultWrapper<KycSessionDto>.Failure(
                        kycStatusResult.Reason,
                        kycStatusResult.ErrorMessage);
                }

                var kycData = kycStatusResult.Data;

                // Don't allow new verification if one is already in progress or approved
                if (kycData.Status == KycStatus.InProgress || kycData.Status == KycStatus.Pending)
                {
                    return ResultWrapper<KycSessionDto>.Failure(
                        FailureReason.ValidationError,
                        "KYC verification already in progress");
                }

                if (kycData.Status == KycStatus.Approved &&
                    KycLevelValue(kycData.VerificationLevel) >= KycLevelValue(request.VerificationLevel))
                {
                    return ResultWrapper<KycSessionDto>.Failure(
                        FailureReason.ValidationError,
                        "User is already verified at an equal or higher level");
                }

                // Create applicant in Onfido
                var applicantData = new
                {
                    first_name = request.UserData.TryGetValue("firstName", out var firstName) ? firstName : "",
                    last_name = request.UserData.TryGetValue("lastName", out var lastName) ? lastName : "",
                    email = request.UserData.TryGetValue("email", out var email) ? email : "",
                    address = request.UserData.TryGetValue("address", out var address) ? address : new { }
                };

                var applicantContent = new StringContent(
                    JsonConvert.SerializeObject(applicantData),
                    Encoding.UTF8,
                    "application/json");

                var applicantResponse = await _httpClient.PostAsync("applicants", applicantContent);
                applicantResponse.EnsureSuccessStatusCode();

                var applicantResult = await applicantResponse.Content.ReadAsStringAsync();
                var applicantJson = JObject.Parse(applicantResult);
                string applicantId = applicantJson["id"].ToString();

                // Create SDK token
                var sdkTokenData = new
                {
                    applicant_id = applicantId,
                    referrer = _onfidoSettings.AllowedReferrers
                };

                var sdkTokenContent = new StringContent(
                    JsonConvert.SerializeObject(sdkTokenData),
                    Encoding.UTF8,
                    "application/json");

                var sdkTokenResponse = await _httpClient.PostAsync("sdk_token", sdkTokenContent);
                sdkTokenResponse.EnsureSuccessStatusCode();

                var sdkTokenResult = await sdkTokenResponse.Content.ReadAsStringAsync();
                var sdkTokenJson = JObject.Parse(sdkTokenResult);
                string token = sdkTokenJson["token"].ToString();

                // Update KYC status
                var updateFields = new Dictionary<string, object>
                {
                    ["Status"] = KycStatus.InProgress,
                    ["ReferenceId"] = applicantId,
                    ["ProviderName"] = "Onfido",
                    ["SubmittedAt"] = DateTime.UtcNow,
                    ["VerificationLevel"] = request.VerificationLevel
                };

                var history = new KycHistoryEntry
                {
                    Action = "Verification Started",
                    Status = KycStatus.InProgress,
                    PerformedBy = request.UserId.ToString(),
                    Details = new Dictionary<string, object>
                    {
                        ["VerificationLevel"] = request.VerificationLevel
                    }
                };

                updateFields["History"] = kycData.History.Append(history).ToList();

                await _repository.UpdateAsync(kycData.Id, updateFields);

                // Return session details
                var sessionDto = new KycSessionDto
                {
                    SessionId = token,
                    VerificationUrl = $"{_onfidoSettings.SdkUrl}?token={token}",
                    ExpiresAt = DateTime.UtcNow.AddHours(1),
                    Status = KycStatus.InProgress
                };

                return ResultWrapper<KycSessionDto>.Success(sessionDto);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError($"KYC provider API error: {ex.Message}");
                return ResultWrapper<KycSessionDto>.Failure(
                    FailureReason.ThirdPartyServiceUnavailable,
                    "KYC service unavailable, please try again later");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error initiating KYC verification: {ex.Message}");
                return ResultWrapper<KycSessionDto>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<KycData>> ProcessKycCallbackAsync(KycCallbackRequest callback)
        {
            try
            {
                // Find KYC data by reference ID
                var filter = Builders<KycData>.Filter.Eq(k => k.ReferenceId, callback.ReferenceId);
                var kycData = await _repository.GetOneAsync(filter);

                if (kycData == null)
                {
                    return ResultWrapper<KycData>.Failure(
                        FailureReason.ResourceNotFound,
                        $"No KYC verification found with reference ID: {callback.ReferenceId}");
                }

                // Map the status from provider to our system status
                string newStatus = callback.Status switch
                {
                    "clear" => KycStatus.Approved,
                    "consider" => KycStatus.NeedsReview,
                    "rejected" => KycStatus.Rejected,
                    _ => KycStatus.Pending
                };

                // Perform additional AML check if the user passed KYC
                if (newStatus == KycStatus.Approved || newStatus == KycStatus.NeedsReview)
                {
                    await PerformAmlCheckAsync(kycData.UserId);
                }

                // Update KYC data with result
                var updateFields = new Dictionary<string, object>
                {
                    ["Status"] = newStatus,
                    ["LastCheckedAt"] = DateTime.UtcNow,
                    ["VerificationData"] = callback.VerificationResult
                };

                if (newStatus == KycStatus.Approved)
                {
                    updateFields["VerifiedAt"] = DateTime.UtcNow;
                }
                else if (newStatus == KycStatus.Rejected &&
                         callback.VerificationResult.TryGetValue("reason", out var reason))
                {
                    updateFields["RejectionReason"] = reason.ToString();
                }

                var history = new KycHistoryEntry
                {
                    Action = "Verification Completed",
                    Status = newStatus,
                    PerformedBy = "SYSTEM",
                    Details = new Dictionary<string, object>
                    {
                        ["ProviderStatus"] = callback.Status,
                        ["SessionId"] = callback.SessionId
                    }
                };

                kycData.History.Add(history);
                updateFields["History"] = kycData.History;

                await _repository.UpdateAsync(kycData.Id, updateFields);

                // Reload the data
                kycData = await _repository.GetOneAsync(filter);

                return ResultWrapper<KycData>.Success(kycData);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing KYC callback: {ex.Message}");
                return ResultWrapper<KycData>.FromException(ex);
            }
        }

        public async Task<ResultWrapper> PerformAmlCheckAsync(Guid userId)
        {
            try
            {
                // Get current KYC data
                var kycResult = await GetUserKycStatusAsync(userId);
                if (!kycResult.IsSuccess)
                {
                    return ResultWrapper.Failure(kycResult.Reason, kycResult.ErrorMessage);
                }

                var kycData = kycResult.Data;

                // Example AML check using Onfido
                var applicantId = kycData.ReferenceId;

                // Request watchlist check
                var checkData = new
                {
                    applicant_id = applicantId,
                    report_names = new[] { "watchlist_standard" }
                };

                var content = new StringContent(
                    JsonConvert.SerializeObject(checkData),
                    Encoding.UTF8,
                    "application/json");

                var response = await _httpClient.PostAsync("checks", content);
                response.EnsureSuccessStatusCode();

                var checkResult = await response.Content.ReadAsStringAsync();
                var checkJson = JObject.Parse(checkResult);

                // Update KYC data with AML result
                var isPoliticallyExposed = false;
                var isHighRisk = false;
                var riskScore = "low";

                // In a real implementation, parse the actual response from Onfido
                // This is just a placeholder example
                if (checkJson["results"] != null && checkJson["results"]?["watchlist_standard"] != null)
                {
                    var watchlistResult = checkJson["results"]["watchlist_standard"];

                    isHighRisk = (watchlistResult["result"] != null &&
                                 watchlistResult["result"].ToString() == "consider");

                    isPoliticallyExposed = (watchlistResult["tags"] != null &&
                                          watchlistResult["tags"].Any(t => t.ToString() == "pep"));

                    riskScore = watchlistResult["risk_level"]?.ToString() ?? "low";
                }

                // Update KYC record with AML results
                var updateFields = new Dictionary<string, object>
                {
                    ["IsPoliticallyExposed"] = isPoliticallyExposed,
                    ["IsHighRisk"] = isHighRisk,
                    ["RiskScore"] = riskScore,
                    ["AdditionalInfo"] = new Dictionary<string, object>
                    {
                        ["AmlCheckDate"] = DateTime.UtcNow,
                        ["AmlProvider"] = "Onfido"
                    }
                };

                // If high risk, change status to needs review
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
                        ["IsHighRisk"] = isHighRisk
                    }
                };

                kycData.History.Add(history);
                updateFields["History"] = kycData.History;

                await _repository.UpdateAsync(kycData.Id, updateFields);

                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error performing AML check: {ex.Message}");
                return ResultWrapper.FromException(ex);
            }
        }

        public async Task<ResultWrapper> UpdateKycStatusAsync(Guid userId, string status, string comment = null)
        {
            try
            {
                // Get current KYC data
                var kycResult = await GetUserKycStatusAsync(userId);
                if (!kycResult.IsSuccess)
                {
                    return ResultWrapper.Failure(kycResult.Reason, kycResult.ErrorMessage);
                }

                var kycData = kycResult.Data;

                // Update status
                var updateFields = new Dictionary<string, object>
                {
                    ["Status"] = status,
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

                var history = new KycHistoryEntry
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

                await _repository.UpdateAsync(kycData.Id, updateFields);

                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error updating KYC status: {ex.Message}");
                return ResultWrapper.FromException(ex);
            }
        }

        public async Task<ResultWrapper<bool>> IsUserVerifiedAsync(Guid userId, string requiredLevel = KycLevel.Standard)
        {
            try
            {
                var kycResult = await GetUserKycStatusAsync(userId);
                if (!kycResult.IsSuccess)
                {
                    return ResultWrapper<bool>.Failure(kycResult.Reason, kycResult.ErrorMessage);
                }

                var kycData = kycResult.Data;

                // Check if verified and at required level
                bool isVerified = kycData.Status == KycStatus.Approved &&
                                  KycLevelValue(kycData.VerificationLevel) >= KycLevelValue(requiredLevel);

                return ResultWrapper<bool>.Success(isVerified);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error checking user verification: {ex.Message}");
                return ResultWrapper<bool>.FromException(ex);
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

                var sort = Builders<KycData>.Sort.Descending(k => k.LastCheckedAt);

                var pendingVerifications = await _repository.GetPaginatedAsync(
                    statusFilter, sort, page, pageSize);

                return ResultWrapper<PaginatedResult<KycData>>.Success(pendingVerifications);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting pending verifications: {ex.Message}");
                return ResultWrapper<PaginatedResult<KycData>>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<bool>> IsUserEligibleForTrading(Guid userId)
        {
            try
            {
                var kycResult = await GetUserKycStatusAsync(userId);
                if (!kycResult.IsSuccess)
                {
                    return ResultWrapper<bool>.Failure(kycResult.Reason, kycResult.ErrorMessage);
                }

                var kycData = kycResult.Data;

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
                _logger.LogError($"Error checking trading eligibility: {ex.Message}");
                return ResultWrapper<bool>.FromException(ex);
            }
        }

        // Helper to convert KYC level to a numeric value for comparison
        private int KycLevelValue(string level) => level switch
        {
            KycLevel.None => 0,
            KycLevel.Basic => 1,
            KycLevel.Standard => 2,
            KycLevel.Advanced => 3,
            KycLevel.Enhanced => 4,
            _ => 0
        };
    }
}