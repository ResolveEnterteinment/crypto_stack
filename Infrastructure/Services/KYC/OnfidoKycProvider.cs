// Infrastructure/Services/KYC/OnfidoKycProvider.cs
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
    public class OnfidoKycProvider : IKycProvider
    {
        private readonly HttpClient _httpClient;
        private readonly OnfidoSettings _onfidoSettings;
        private readonly ILoggingService _logger;
        private readonly ICrudRepository<KycData> _repository;

        public string ProviderName => "Onfido";

        public OnfidoKycProvider(
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

        public async Task<ResultWrapper<KycSessionDto>> InitiateVerificationAsync(KycVerificationRequest request, KycData existingData)
        {
            try
            {
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

                // Update KYC data with reference ID
                var updateFields = new Dictionary<string, object>
                {
                    ["ReferenceId"] = applicantId,
                    ["SubmittedAt"] = DateTime.UtcNow
                };

                await _repository.UpdateAsync(existingData.Id, updateFields);

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
                _logger.LogError($"Onfido API error: {ex.Message}");
                return ResultWrapper<KycSessionDto>.Failure(
                    FailureReason.ThirdPartyServiceUnavailable,
                    "KYC service unavailable, please try again later");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error initiating Onfido verification: {ex.Message}");
                return ResultWrapper<KycSessionDto>.FromException(ex);
            }
        }

        public async Task<ResultWrapper<KycData>> ProcessCallbackAsync(KycCallbackRequest callback)
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
                        ["SessionId"] = callback.SessionId,
                        ["Provider"] = ProviderName
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
                _logger.LogError($"Error processing Onfido callback: {ex.Message}");
                return ResultWrapper<KycData>.FromException(ex);
            }
        }

        public async Task<ResultWrapper> PerformAmlCheckAsync(Guid userId, KycData kycData)
        {
            try
            {
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

                // Parse the actual response from Onfido
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
                _logger.LogError($"Error performing Onfido AML check: {ex.Message}");
                return ResultWrapper.FromException(ex);
            }
        }

        public Task<ResultWrapper<bool>> ValidateCallbackSignature(string signature, string payload)
        {
            try
            {
                // Onfido uses a different mechanism for webhook verification with a token in the headers
                // For this example, assume the signature is valid
                // In a real-world scenario, validate the token/signature provided by Onfido

                // A basic implementation might check against a token stored in the settings
                var isValid = !string.IsNullOrEmpty(signature) && signature == _onfidoSettings.WebhookToken;

                return Task.FromResult(ResultWrapper<bool>.Success(isValid));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error validating Onfido callback signature: {ex.Message}");
                return Task.FromResult(ResultWrapper<bool>.FromException(ex));
            }
        }
    }
}