// Infrastructure/Services/KYC/KycServiceFactory.cs
using Application.Interfaces.KYC;
using Domain.DTOs.Settings;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services.KYC
{
    public class KycServiceFactory : IKycServiceFactory
    {
        private readonly OnfidoKycService _onfidoService;
        private readonly SumSubKycService _sumSubService;
        private readonly KycServiceSettings _settings;

        public KycServiceFactory(
            OnfidoKycService onfidoService,
            SumSubKycService sumSubService,
            IOptions<KycServiceSettings> settings)
        {
            _onfidoService = onfidoService ?? throw new ArgumentNullException(nameof(onfidoService));
            _sumSubService = sumSubService ?? throw new ArgumentNullException(nameof(sumSubService));
            _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        }

        public IKycService GetKycService()
        {
            return _settings.DefaultProvider switch
            {
                "SumSub" => _sumSubService,
                "Onfido" => _onfidoService,
                _ => _sumSubService // Default to SumSub if not specified
            };
        }

        public IKycService GetKycService(string providerName)
        {
            return providerName switch
            {
                "SumSub" => _sumSubService,
                "Onfido" => _onfidoService,
                _ => throw new ArgumentException($"Unsupported KYC provider: {providerName}")
            };
        }
    }
}