// Application/Interfaces/KYC/IKycService.cs
using Domain.Constants.KYC;
using Domain.DTOs;
using Domain.DTOs.KYC;
using Domain.Models.KYC;

namespace Application.Interfaces.KYC
{
    public interface IKycSessionService
    {
        Task<ResultWrapper<KycSessionData>> GetOrCreateUserSessionAsync(Guid userId);
        Task<ResultWrapper<KycSessionData>> ValidateSessionAsync(Guid sessionId, Guid userId);
        Task<ResultWrapper> InvalidateSessionAsync(Guid sessionId, Guid userId, string reason = "Manual invalidation");
        Task<ResultWrapper> UpdateSessionProgressAsync(Guid sessionId, SessionProgress progress);
    }
}