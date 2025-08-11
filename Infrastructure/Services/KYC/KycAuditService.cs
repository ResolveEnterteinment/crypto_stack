using Application.Interfaces.KYC;
using Domain.Models.KYC;
using Infrastructure.Services.Base;
using Infrastructure.Services.Http;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Services.KYC
{
    public class KycAuditService : BaseService<KycAuditLogData>, IKycAuditService
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpContextService _httpContextService;

        public KycAuditService(
            IServiceProvider serviceProvider,
            IConfiguration configuration,
            IHttpContextService httpContextService) : base(serviceProvider)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _httpContextService = httpContextService ?? throw new ArgumentNullException(nameof(httpContextService));
        }

        public async Task LogAuditEvent(Guid userId, string action, string details)
        {
            var auditLog = new KycAuditLogData
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Action = action,
                Details = details,
                Timestamp = DateTime.UtcNow,
                IpAddress = _httpContextService.GetClientIpAddress(),
                UserAgent = _httpContextService.GetUserAgent()
            };

            await _repository.InsertAsync(auditLog);
        }
    }
}