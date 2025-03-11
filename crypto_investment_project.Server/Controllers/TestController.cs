using Application.Contracts.Requests.Payment;
using Application.Interfaces;
using Domain.Models.Crypto;
using Domain.Models.Payment;
using Domain.Models.Subscription;
using Microsoft.AspNetCore.Mvc;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TestController(
        IExchangeService exchangeService,
        IAssetService assetService,
        ISubscriptionService subscriptionService) : ControllerBase
    {
        private readonly IExchangeService _exchangeService = exchangeService;
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
            PaymentData paymentData = new()
            {
                UserId = paymentRequest.UserId,
                SubscriptionId = new Guid(paymentRequest.SubscriptionId),
                PaymentProviderId = paymentRequest.PaymentId,
                PaymentProviderFee = paymentRequest.PaymentProviderFee,
                TotalAmount = paymentRequest.TotalAmount,
                PlatformFee = paymentRequest.PlatformFee,
                NetAmount = paymentRequest.NetAmount,
                Status = paymentRequest.Status,
            };
            try
            {
                var result = await _exchangeService.ProcessPayment(paymentData);
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
        public async Task<IActionResult> NewAsset([FromBody] AssetData assetData)
        {
            if (assetData is null)
            {
                return BadRequest("A valid asset data is required.");
            }
            try
            {
                var result = await _assetService.InsertOneAsync(assetData);
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
        public async Task<IActionResult> NewAsset([FromBody] SubscriptionData subscriptionData)
        {
            if (subscriptionData is null)
            {
                return BadRequest("A valid asset data is required.");
            }
            try
            {
                var result = await _subscriptionService.InsertOneAsync(subscriptionData);
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
