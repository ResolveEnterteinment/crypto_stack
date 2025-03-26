using Application.Contracts.Requests.Payment;
using Application.Interfaces;
using Application.Interfaces.Exchange;
using Domain.Models.Payment;
using Microsoft.AspNetCore.Mvc;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ExchangeController(
        IExchangeService exchangeService,
        IPaymentProcessingService paymentProcessingService,
        IBalanceManagementService balanceManagementService,
        IAssetService assetService) : ControllerBase
    {
        private readonly IExchangeService _exchangeService = exchangeService;
        private readonly IPaymentProcessingService _paymentProcessingService = paymentProcessingService;
        private readonly IBalanceManagementService _balanceManagementService = balanceManagementService;
        private readonly IAssetService _assetService = assetService;

        [HttpPost]
        [Route("payment")]
        public async Task<IActionResult> Payment([FromBody] PaymentIntentRequest paymentRequest)
        {
            if (paymentRequest is null)
            {
                return BadRequest("A valid payment is required.");
            }
            PaymentData paymentData = new()
            {
                UserId = Guid.Parse(paymentRequest.UserId),
                SubscriptionId = Guid.Parse(paymentRequest.SubscriptionId),
                Provider = paymentRequest.Provider,
                PaymentProviderId = paymentRequest.PaymentId,
                PaymentProviderFee = paymentRequest.PaymentProviderFee,
                TotalAmount = paymentRequest.TotalAmount,
                PlatformFee = paymentRequest.PlatformFee,
                NetAmount = paymentRequest.NetAmount,
                Currency = paymentRequest.Currency,
                Status = paymentRequest.Status,
            };
            try
            {
                var result = await _paymentProcessingService.ProcessPayment(paymentData);
                return result is null ? throw new NullReferenceException(nameof(result)) : (IActionResult)Ok(result);
            }
            catch (Exception ex)
            {
                var message = string.Format("Unable to process payment. {0}", ex.Message);
                return BadRequest(message);
            }
        }
    }
}
