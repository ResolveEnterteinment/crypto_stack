﻿// Infrastructure/Services/KYC/OnfidoKycService.cs
using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Domain.Models.KYC;

namespace Infrastructure.Services.KYC
{
    public class OnfidoKycService : BaseKycService
    {
        public OnfidoKycService(
            ICrudRepository<KycData> repository,
            ILoggingService logger,
            OnfidoKycProvider provider)
            : base(repository, logger, provider)
        {
        }
    }
}