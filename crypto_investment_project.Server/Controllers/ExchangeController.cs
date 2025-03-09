using Application.Contracts.Requests.Payment;
using Application.Interfaces;
using Domain.Models.Payment;
using Domain.Services;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ExchangeController(IExchangeService exchangeService, IAssetService assetService) : ControllerBase
    {
        private readonly IExchangeService _exchangeService = exchangeService;
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
                UserId = new ObjectId(paymentRequest.UserId),
                SubscriptionId = new ObjectId(paymentRequest.SubscriptionId),
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
                var message = string.Format("Unable to process payment. {0}", ex.Message);
                return BadRequest(message);
            }
        }

        [HttpPost]
        [Route("reset")]
        public async Task<IActionResult> ResetBalances()
        {
            try
            {
                var supportedAssetsResult = await _assetService.GetSupportedTickersAsync();
                var filter = supportedAssetsResult.IsSuccess ? supportedAssetsResult.Data : null;
                var result = await _exchangeService.ResetBalances(filter);
                return !result.IsSuccess ? throw new Exception(result.ErrorMessage) : (IActionResult)Ok(result.Data);
            }
            catch (Exception ex)
            {
                var message = string.Format("Unable to reset balances. {0}", ex.Message);
                return BadRequest(message);
            }
        }
    }
}
