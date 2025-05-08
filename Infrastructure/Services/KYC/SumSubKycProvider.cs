// Infrastructure/Services/KYC/SumSubKycProvider.cs
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
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Infrastructure.Services.KYC
{
    public class SumSubKycProvider : IKycProvider
    {
        private readonly HttpClient _httpClient;
        private readonly SumSubSettings _sumSubSettings;
        private readonly ILoggingService _logger;
        private readonly ICrudRepository<KycData> _repository;

        public string ProviderName => "SumSub";

        public SumSubKycProvider(
            HttpClient httpClient,
            IOptions<SumSubSettings> sumSubSettings,
            ILoggingService logger,
            ICrudRepository<KycData> repository)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _sumSubSettings = sumSubSettings?.Value ?? throw new ArgumentNullException(nameof(sumSubSettings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));

            _httpClient.BaseAddress = new Uri(_sumSubSettings.ApiUrl);
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public async Task<ResultWrapper<KycSessionDto>> InitiateVerificationAsync(KycVerificationRequest request, KycData existingData)
        {
            try
            {
                // Map KYC level to SumSub level
                string levelName = GetLevelName(request.VerificationLevel);

                // Format the current date for the timestamp
                string ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

                // Create applicant externalUserId from userId
                string externalUserId = request.UserId.ToString();

                // Create the access signature
                string signature = GenerateSignature($"POST/resources/accessTokens?userId={externalUserId}&levelName={levelName}&ttlInSecs=3600{ts}");

                // Add required headers
                _httpClient.DefaultRequestHeaders.Remove("X-App-Token");
                _httpClient.DefaultRequestHeaders.Remove("X-App-Access-Sig");
                _httpClient.DefaultRequestHeaders.Remove("X-App-Access-Ts");

                _httpClient.DefaultRequestHeaders.Add("X-App-Token", _sumSubSettings.AppToken);
                _httpClient.DefaultRequestHeaders.Add("X-App-Access-Sig", signature);
                _httpClient.DefaultRequestHeaders.Add("X-App-Access-Ts", ts);

                // Request access token
                var response = await _httpClient.PostAsync(
                    $"/resources/accessTokens?userId={externalUserId}&levelName={levelName}&ttlInSecs=3600",
                    null);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                var tokenResponse = JObject.Parse(responseContent);
                string accessToken = tokenResponse["token"].ToString();

                // Update KYC data with SumSub reference
                var updateFields = new Dictionary<string, object>
                {
                    ["ReferenceId"] = externalUserId,
                    ["SubmittedAt"] = DateTime.UtcNow
                };

                await _repository.UpdateAsync(existingData.Id, updateFields);

                // Return session details
                var sessionDto = new KycSessionDto
                {
                    SessionId = accessToken,
                    VerificationUrl = $"https://api.sumsub.com/idensic/start#{accessToken}",
                    ExpiresAt = DateTime.UtcNow.AddHours(1),
                    Status = KycStatus.InProgress
                };

                return ResultWrapper<KycSessionDto>.Success(sessionDto);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError($"SumSub API error: {ex.Message}");
                return ResultWrapper<KycSessionDto>.Failure(
                    FailureReason.ThirdPartyServiceUnavailable,
                    "KYC service unavailable, please try again later");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error initiating SumSub verification: {ex.Message}");
                return ResultWrapper<KycSessionDto>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<KycData>> ProcessCallbackAsync(KycCallbackRequest callback)
        {
            try
            {
                // Parse the SumSub callback data
                var applicantId = callback.ReferenceId;

                // Find KYC data by the applicant ID
                var filter = Builders<KycData>.Filter.Eq(k => k.ReferenceId, applicantId);
                var kycData = await _repository.GetOneAsync(filter);

                if (kycData == null)
                {
                    return ResultWrapper<KycData>.Failure(
                        FailureReason.ResourceNotFound,
                        $"No KYC verification found with reference ID: {applicantId}");
                }

                // Map the SumSub status to our system status
                string reviewStatus = callback.VerificationResult.TryGetValue("reviewStatus", out var status)
                    ? status.ToString()
                    : string.Empty;

                string newStatus = MapSumSubStatus(reviewStatus);

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
                         callback.VerificationResult.TryGetValue("reviewRejectType", out var reason))
                {
                    updateFields["RejectionReason"] = reason.ToString();
                }

                // Check if there's any risk flags
                if (callback.VerificationResult.TryGetValue("isHighRisk", out var highRisk))
                {
                    updateFields["IsHighRisk"] = Convert.ToBoolean(highRisk);
                }

                if (callback.VerificationResult.TryGetValue("isPoliticallyExposed", out var pep))
                {
                    updateFields["IsPoliticallyExposed"] = Convert.ToBoolean(pep);
                }

                var history = new KycHistoryEntry
                {
                    Action = "Verification Completed",
                    Status = newStatus,
                    PerformedBy = "SYSTEM",
                    Details = new Dictionary<string, object>
                    {
                        ["ProviderStatus"] = reviewStatus,
                        ["SessionId"] = callback.SessionId,
                        ["Provider"] = ProviderName
                    }
                };

                kycData.History.Add(history);
                updateFields["History"] = kycData.History;

                await _repository.UpdateAsync(kycData.Id, updateFields);

                // Reload the data
                kycData = await _repository.GetOneAsync(filter);

                // Perform additional checks if approved
                if (newStatus == KycStatus.Approved || newStatus == KycStatus.NeedsReview)
                {
                    // Get additional risk indicators from SumSub
                    await GetApplicantRiskData(applicantId, kycData.Id);
                }

                return ResultWrapper<KycData>.Success(kycData);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing SumSub callback: {ex.Message}");
                return ResultWrapper<KycData>.FromException(ex);
            }
        }

        public async Task<ResultWrapper> PerformAmlCheckAsync(Guid userId, KycData kycData)
        {
            try
            {
                string applicantId = kycData.ReferenceId;
                if (string.IsNullOrEmpty(applicantId))
                {
                    return ResultWrapper.Failure(FailureReason.ValidationError, "No reference ID found for AML check");
                }

                // Format the current date for the timestamp
                string ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

                // Create the access signature
                string signature = GenerateSignature($"GET/resources/applicants/{applicantId}/amlInfo{ts}");

                // Add required headers
                _httpClient.DefaultRequestHeaders.Remove("X-App-Token");
                _httpClient.DefaultRequestHeaders.Remove("X-App-Access-Sig");
                _httpClient.DefaultRequestHeaders.Remove("X-App-Access-Ts");

                _httpClient.DefaultRequestHeaders.Add("X-App-Token", _sumSubSettings.AppToken);
                _httpClient.DefaultRequestHeaders.Add("X-App-Access-Sig", signature);
                _httpClient.DefaultRequestHeaders.Add("X-App-Access-Ts", ts);

                // Get AML info
                var response = await _httpClient.GetAsync($"/resources/applicants/{applicantId}/amlInfo");
                response.EnsureSuccessStatusCode();

                var amlInfoJson = await response.Content.ReadAsStringAsync();
                var amlData = JObject.Parse(amlInfoJson);

                // Extract AML risk indicators
                bool isPoliticallyExposed = amlData["isInPepList"]?.Value<bool>() ?? false;
                bool isHighRisk = amlData["isInSanctionsList"]?.Value<bool>() ?? false;
                string riskScore = amlData["riskScore"]?.Value<string>() ?? "low";

                // Update KYC record with AML results
                var updateFields = new Dictionary<string, object>
                {
                    ["IsPoliticallyExposed"] = isPoliticallyExposed,
                    ["IsHighRisk"] = isHighRisk,
                    ["RiskScore"] = riskScore,
                    ["AdditionalInfo"] = new Dictionary<string, object>
                    {
                        ["AmlCheckDate"] = DateTime.UtcNow,
                        ["AmlProvider"] = ProviderName
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
                        ["IsHighRisk"] = isHighRisk,
                        ["Provider"] = ProviderName
                    }
                };

                kycData.History.Add(history);
                updateFields["History"] = kycData.History;

                await _repository.UpdateAsync(kycData.Id, updateFields);

                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error performing SumSub AML check: {ex.Message}");
                return ResultWrapper.FromException(ex);
            }
        }

        public Task<ResultWrapper<bool>> ValidateCallbackSignature(string signature, string payload)
        {
            try
            {
                // Get the webhook secret from settings
                string secret = _sumSubSettings.WebhookSecret;
                if (string.IsNullOrEmpty(secret))
                {
                    return Task.FromResult(ResultWrapper<bool>.Failure(
                        FailureReason.ConfigurationError,
                        "Webhook secret is not configured"));
                }

                // Calculate expected signature
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                var calculatedSignature = Convert.ToBase64String(hash);

                // Compare signatures
                bool isValid = signature == calculatedSignature;

                return Task.FromResult(ResultWrapper<bool>.Success(isValid));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error validating SumSub callback signature: {ex.Message}");
                return Task.FromResult(ResultWrapper<bool>.FromException(ex));
            }
        }

        #region Private Helper Methods

        private string GetLevelName(string verificationLevel) => verificationLevel switch
        {
            KycLevel.Basic => _sumSubSettings.LevelNameBasic,
            KycLevel.Standard => _sumSubSettings.LevelNameStandard,
            KycLevel.Advanced => _sumSubSettings.LevelNameAdvanced,
            KycLevel.Enhanced => _sumSubSettings.LevelNameEnhanced,
            _ => _sumSubSettings.LevelNameBasic
        };

        private string MapSumSubStatus(string sumSubStatus) => sumSubStatus.ToLower() switch
        {
            "approved" => KycStatus.Approved,
            "completed" => KycStatus.Approved,
            "pending" => KycStatus.Pending,
            "pending_review" => KycStatus.NeedsReview,
            "queued" => KycStatus.Pending,
            "prechecked" => KycStatus.Pending,
            "onhold" => KycStatus.NeedsReview,
            "rejected" => KycStatus.Rejected,
            _ => KycStatus.Pending
        };

        private string GenerateSignature(string dataToSign)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_sumSubSettings.SecretKey));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataToSign));
            return Convert.ToHexString(hash).ToLower();
        }

        private async Task GetApplicantRiskData(string applicantId, Guid kycDataId)
        {
            try
            {
                // Format the current date for the timestamp
                string ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

                // Create the access signature for risk info
                string signature = GenerateSignature($"GET/resources/applicants/{applicantId}/riskinfo{ts}");

                // Add required headers
                _httpClient.DefaultRequestHeaders.Remove("X-App-Token");
                _httpClient.DefaultRequestHeaders.Remove("X-App-Access-Sig");
                _httpClient.DefaultRequestHeaders.Remove("X-App-Access-Ts");

                _httpClient.DefaultRequestHeaders.Add("X-App-Token", _sumSubSettings.AppToken);
                _httpClient.DefaultRequestHeaders.Add("X-App-Access-Sig", signature);
                _httpClient.DefaultRequestHeaders.Add("X-App-Access-Ts", ts);

                // Get risk info
                var response = await _httpClient.GetAsync($"/resources/applicants/{applicantId}/riskinfo");
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"Failed to get risk data from SumSub: {response.StatusCode}");
                    return;
                }

                var riskInfoJson = await response.Content.ReadAsStringAsync();
                var riskData = JObject.Parse(riskInfoJson);

                // Extract risk information
                var riskInfoData = new Dictionary<string, object>();

                if (riskData["riskScore"] != null)
                {
                    riskInfoData["RiskScore"] = riskData["riskScore"].ToString();
                }

                if (riskData["restrictedRegion"] != null)
                {
                    bool isRestrictedRegion = riskData["restrictedRegion"].Value<bool>();
                    riskInfoData["IsRestrictedRegion"] = isRestrictedRegion;

                    // Update the IsRestrictedRegion field in KYC data
                    await _repository.UpdateAsync(kycDataId, new Dictionary<string, object>
                    {
                        ["IsRestrictedRegion"] = isRestrictedRegion,
                        ["AdditionalInfo.RiskData"] = riskInfoData
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting applicant risk data: {ex.Message}");
                // Don't throw, this is additional data only
            }
        }

        #endregion
    }
}