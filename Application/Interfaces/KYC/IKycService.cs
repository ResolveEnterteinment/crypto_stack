// Application/Interfaces/KYC/IKycService.cs
using Domain.Constants.KYC;
using Domain.DTOs;
using Domain.DTOs.KYC;
using Domain.Models.KYC;

namespace Application.Interfaces.KYC
{
    public interface IKycService
    {
        Task<ResultWrapper<KycData>> GetUserKycStatusAsync(Guid userId);
        Task<ResultWrapper<KycSessionDto>> InitiateKycVerificationAsync(KycVerificationRequest request);
        Task<ResultWrapper<KycData>> ProcessKycCallbackAsync(KycCallbackRequest callback);
        Task<ResultWrapper> UpdateKycStatusAsync(Guid userId, string status, string comment = null);
        Task<ResultWrapper<bool>> IsUserVerifiedAsync(Guid userId, string requiredLevel = KycLevel.Standard);
        Task<ResultWrapper<PaginatedResult<KycData>>> GetPendingVerificationsAsync(int page = 1, int pageSize = 20);
        Task<ResultWrapper> PerformAmlCheckAsync(Guid userId);
        Task<ResultWrapper<bool>> IsUserEligibleForTrading(Guid userId);
    }
}