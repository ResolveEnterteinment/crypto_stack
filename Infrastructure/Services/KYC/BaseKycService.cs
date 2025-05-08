// Infrastructure/Services/KYC/BaseKycService.cs
using Application.Interfaces.Base;
using Application.Interfaces.KYC;
using Application.Interfaces.Logging;
using Domain.Constants;
using Domain.Constants.KYC;
using Domain.DTOs;
using Domain.DTOs.KYC;
using Domain.Models.KYC;
using MongoDB.Driver;

namespace Infrastructure.Services.KYC
{
    public abstract class BaseKycService : IKycService
    {
        protected readonly ILoggingService Logger;
        protected readonly ICrudRepository<KycData> Repository;
        protected readonly IKycProvider Provider;

        protected BaseKycService(
            ICrudRepository<KycData> repository,
            ILoggingService logger,
            IKycProvider provider)
        {
            Repository = repository ?? throw new ArgumentNullException(nameof(repository));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public virtual async Task<ResultWrapper<KycData>> GetUserKycStatusAsync(Guid userId)
        {
            try
            {
                var filter = Builders<KycData>.Filter.Eq(k => k.UserId, userId);
                var kycData = await Repository.GetOneAsync(filter);

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

                    await Repository.InsertAsync(newKycData);
                    return ResultWrapper<KycData>.Success(newKycData);
                }

                return ResultWrapper<KycData>.Success(kycData);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting KYC status: {ex.Message}");
                return ResultWrapper<KycData>.FromException(ex);
            }
        }

        public virtual async Task<ResultWrapper<KycSessionDto>> InitiateKycVerificationAsync(KycVerificationRequest request)
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

                // Update KYC status to in progress
                var updateFields = new Dictionary<string, object>
                {
                    ["Status"] = KycStatus.InProgress,
                    ["VerificationLevel"] = request.VerificationLevel,
                    ["ProviderName"] = Provider.ProviderName
                };

                var history = new KycHistoryEntry
                {
                    Action = "Verification Started",
                    Status = KycStatus.InProgress,
                    PerformedBy = request.UserId.ToString(),
                    Details = new Dictionary<string, object>
                    {
                        ["VerificationLevel"] = request.VerificationLevel,
                        ["Provider"] = Provider.ProviderName
                    }
                };

                kycData.History.Add(history);
                updateFields["History"] = kycData.History;

                await Repository.UpdateAsync(kycData.Id, updateFields);

                // Use the provider to initiate verification
                return await Provider.InitiateVerificationAsync(request, kycData);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error initiating KYC verification: {ex.Message}");
                return ResultWrapper<KycSessionDto>.FromException(ex);
            }
        }

        public virtual async Task<ResultWrapper<KycData>> ProcessKycCallbackAsync(KycCallbackRequest callback)
        {
            try
            {
                return await Provider.ProcessCallbackAsync(callback);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error processing KYC callback: {ex.Message}");
                return ResultWrapper<KycData>.FromException(ex);
            }
        }

        public virtual async Task<ResultWrapper> PerformAmlCheckAsync(Guid userId)
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

                // Use the provider to perform AML check
                return await Provider.PerformAmlCheckAsync(userId, kycData);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error performing AML check: {ex.Message}");
                return ResultWrapper.FromException(ex);
            }
        }

        public virtual async Task<ResultWrapper> UpdateKycStatusAsync(Guid userId, string status, string comment = null)
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

                await Repository.UpdateAsync(kycData.Id, updateFields);

                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error updating KYC status: {ex.Message}");
                return ResultWrapper.FromException(ex);
            }
        }

        public virtual async Task<ResultWrapper<bool>> IsUserVerifiedAsync(Guid userId, string requiredLevel = KycLevel.Standard)
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
                Logger.LogError($"Error checking user verification: {ex.Message}");
                return ResultWrapper<bool>.FromException(ex);
            }
        }

        public virtual async Task<ResultWrapper<PaginatedResult<KycData>>> GetPendingVerificationsAsync(int page = 1, int pageSize = 20)
        {
            try
            {
                var statusFilter = Builders<KycData>.Filter.Or(
                    Builders<KycData>.Filter.Eq(k => k.Status, KycStatus.Pending),
                    Builders<KycData>.Filter.Eq(k => k.Status, KycStatus.NeedsReview)
                );

                var sort = Builders<KycData>.Sort.Descending(k => k.LastCheckedAt);

                var pendingVerifications = await Repository.GetPaginatedAsync(
                    statusFilter, sort, page, pageSize);

                return ResultWrapper<PaginatedResult<KycData>>.Success(pendingVerifications);
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting pending verifications: {ex.Message}");
                return ResultWrapper<PaginatedResult<KycData>>.FromException(ex);
            }
        }

        public virtual async Task<ResultWrapper<bool>> IsUserEligibleForTrading(Guid userId)
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
                Logger.LogError($"Error checking trading eligibility: {ex.Message}");
                return ResultWrapper<bool>.FromException(ex);
            }
        }

        // Helper to convert KYC level to a numeric value for comparison
        protected static int KycLevelValue(string level) => level switch
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