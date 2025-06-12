// Application/Interfaces/KYC/IKycService.cs
using Domain.Constants.KYC;
using Domain.DTOs;
using Domain.DTOs.KYC;
using Domain.Models.KYC;

namespace Application.Interfaces.KYC
{
    public interface IKycService
    {
        Task<ResultWrapper<KycData>> GetUserKycStatusAsync(Guid userId, string? statusFilter = null);
        Task<ResultWrapper<KycSessionData>> GetOrCreateUserSessionAsync(Guid userId, string verificationLevel);
        Task<ResultWrapper<KycData>> VerifyAsync(KycVerificationRequest request);
        Task<ResultWrapper> UpdateKycStatusAsync(Guid userId, string status, string? comment = null);
        Task<ResultWrapper<bool>> IsUserVerifiedAsync(Guid userId, string requiredLevel = KycLevel.Standard);
        Task<ResultWrapper<PaginatedResult<KycData>>> GetPendingVerificationsAsync(int page = 1, int pageSize = 20);
        Task<ResultWrapper> PerformAmlCheckAsync(Guid userId);
        Task<ResultWrapper<bool>> IsUserEligibleForTrading(Guid userId);
    }
}