// Infrastructure/Services/KYC/SumSubKycService.cs
using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Domain.Models.KYC;

namespace Infrastructure.Services.KYC
{
    public class SumSubKycService : BaseKycService
    {
        public SumSubKycService(
            ICrudRepository<KycData> repository,
            ILoggingService logger,
            SumSubKycProvider provider)
            : base(repository, logger, provider)
        {
        }
    }
}