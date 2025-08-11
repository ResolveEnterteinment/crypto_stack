// Application/Interfaces/KYC/IKycService.cs
using Domain.Constants.KYC;
using Domain.DTOs;
using Domain.DTOs.KYC;
using Domain.Models.KYC;

namespace Application.Interfaces.KYC
{
    public interface IKycAuditService
    {
        Task LogAuditEvent(Guid userId, string action, string details);
    }
}