// Application/Interfaces/KYC/IKycService.cs
using Domain.Constants.KYC;
using Domain.DTOs;
using Domain.DTOs.KYC;
using Domain.Models.KYC;

namespace Application.Interfaces.KYC
{
    public interface IKycService
    {
        Task<ResultWrapper<List<KycDto>>> GetUserKycStatusPerLevelAsync(Guid userId);
        Task<ResultWrapper<KycDto>> GetUserKycStatusWithHighestLevelAsync(Guid userId, bool? includePending = false);
        Task<ResultWrapper<KycData?>> GetUserKycDataDecryptedAsync(Guid userId, string? statusFilter = null, string? levelfilter = null);
        Task<ResultWrapper<KycData>> GetOrCreateAsync(KycVerificationRequest request);
        Task<ResultWrapper<bool>> IsUserVerifiedAsync(Guid userId, string requiredLevel = KycLevel.Standard);
        Task<ResultWrapper<PaginatedResult<KycData>>> GetPendingVerificationsAsync(int page = 1, int pageSize = 20);
        Task<ResultWrapper<CrudResult<KycData>>> UpdateKycStatusAsync(Guid userId, string verificationLevel, string status, string? adminUserId = null, string? reason = null, string? comments = null);
        Task<ResultWrapper<AmlResult>> PerformAmlCheckAsync(Guid userId);
        int GetKycLevelValue(string level);
    }
}