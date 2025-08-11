// Application/Interfaces/KYC/IKycService.cs
using Domain.Constants.KYC;
using Domain.DTOs;
using Domain.DTOs.KYC;
using Domain.Models.KYC;

namespace Application.Interfaces.KYC
{
    public interface IKycService
    {
        Task<ResultWrapper<KycStatusDto>> GetUserKycStatusAsync(Guid userId);
        Task<ResultWrapper<KycData>> GetUserKycDataDecryptedAsync(Guid userId, string? statusFilter = null);
        Task<ResultWrapper<KycData>> VerifyAsync(KycVerificationRequest request);
        Task<ResultWrapper<bool>> IsUserVerifiedAsync(Guid userId, string requiredLevel = KycLevel.Standard);
        Task<ResultWrapper<PaginatedResult<KycData>>> GetPendingVerificationsAsync(int page = 1, int pageSize = 20);
        Task<ResultWrapper<bool>> IsUserEligibleForTrading(Guid userId);
        Task<ResultWrapper<CrudResult<KycData>>> UpdateKycStatusAsync(Guid userId, string status, string? adminUserId = null, string? reason = null);
        Task<ResultWrapper<AmlResult>> PerformAmlCheckAsync(Guid userId);
    }
}