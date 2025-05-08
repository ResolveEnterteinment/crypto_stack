// Application/Interfaces/KYC/IKycServiceFactory.cs
namespace Application.Interfaces.KYC
{
    public interface IKycServiceFactory
    {
        IKycService GetKycService();
        IKycService GetKycService(string providerName);
        IKycService GetKycServiceByUserId(Guid userId);
    }
}