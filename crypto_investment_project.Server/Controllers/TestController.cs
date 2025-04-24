using Application.Contracts.Requests.Payment;
using Application.Interfaces.Asset;
using Application.Interfaces.Exchange;
using Application.Interfaces.Subscription;
using Domain.Models.Payment;
using Microsoft.AspNetCore.Mvc;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [IgnoreAntiforgeryToken]
    public class TestController(
        IPaymentProcessingService paymentProcessingService,
        IAssetService assetService,
        ISubscriptionService subscriptionService) : ControllerBase
    {
        private readonly IPaymentProcessingService _paymentProcessingService = paymentProcessingService;
        private readonly IAssetService _assetService = assetService;
        private readonly ISubscriptionService _subscriptionService = subscriptionService;

        [HttpPost]
        [Route("ProcessTransactionRequest")]
        public async Task<IActionResult> ProcessTransactionRequest([FromBody] PaymentIntentRequest paymentRequest)
        {
            if (paymentRequest is null)
            {
                return BadRequest("A valid transaction is required.");
            }

            var providerFee = paymentRequest.Amount * 0.03m + 0.3m;
            var platformFee = paymentRequest.Amount * 0.01m;
            PaymentData paymentData = new()
            {
                UserId = Guid.Parse(paymentRequest.UserId),
                SubscriptionId = Guid.Parse(paymentRequest.SubscriptionId),
                Provider = "Stripe",
                PaymentProviderId = paymentRequest.PaymentId,
                InvoiceId = paymentRequest.InvoiceId,
                PaymentProviderFee = providerFee,
                TotalAmount = paymentRequest.Amount,
                PlatformFee = platformFee,
                NetAmount = paymentRequest.Amount - providerFee - platformFee,
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
                var message = string.Format("Exchange order could not be initiated. {0}", ex.Message);
                return BadRequest(message);
            }
        }
        [HttpPost]
        [Route("newAsset")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> NewAsset([FromBody] Domain.Models.Asset.AssetData assetData)
        {
            if (assetData is null)
            {
                return BadRequest("A valid asset data is required.");
            }
            try
            {
                var result = await _assetService.InsertAsync(assetData);
                return result is null ? throw new NullReferenceException(nameof(result)) : (IActionResult)Ok(result);
            }
            catch (Exception ex)
            {
                var message = string.Format("Failed to create asset data: {0}", ex.Message);
                return BadRequest(message);
            }
        }
        [HttpPost]
        [Route("newSubscription")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> NewSubscription([FromBody] Domain.Models.Subscription.SubscriptionData subscriptionData)
        {
            if (subscriptionData is null)
            {
                return BadRequest("A valid asset data is required.");
            }
            try
            {
                var result = await _subscriptionService.InsertAsync(subscriptionData);
                return result is null ? throw new NullReferenceException(nameof(result)) : (IActionResult)Ok(result);
            }
            catch (Exception ex)
            {
                var message = string.Format("Failed to create asset data: {0}", ex.Message);
                return BadRequest(message);
            }
        }
    }
}
