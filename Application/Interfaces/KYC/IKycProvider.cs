// Application/Interfaces/KYC/IKycProvider.cs
using Domain.DTOs;
using Domain.DTOs.KYC;
using Domain.Models.KYC;

namespace Application.Interfaces.KYC
{
    public interface IKycProvider
    {
        string ProviderName { get; }
        Task<ResultWrapper<KycSessionData>> GetOrCreateUserSession(Guid userId, string verificationLevel);
        Task<ResultWrapper<KycSessionDto>> InitiateVerificationAsync(KycVerificationRequest request, KycData existingData);
        Task<ResultWrapper<KycData>> ProcessCallbackAsync(KycCallbackRequest callback);
        Task<ResultWrapper> PerformAmlCheckAsync(Guid userId, KycData kycData);
        Task<ResultWrapper<bool>> ValidateCallbackSignature(string signature, string payload);
    }
}