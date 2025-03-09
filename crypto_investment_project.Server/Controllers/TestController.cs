using Application.Contracts.Requests.Payment;
using Application.Interfaces;
using Domain.Models.Payment;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TestController(IExchangeService exchangeService) : ControllerBase
    {
        private readonly IExchangeService _exchangeService = exchangeService;

        [HttpPost]
        [Route("newTransaction")]
        public async Task<IActionResult> Post([FromBody] PaymentIntentRequest paymentRequest)
        {
            if (paymentRequest is null)
            {
                return BadRequest("A valid transaction is required.");
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
                var message = string.Format("Exchange order could not be initiated. {0}", ex.Message);
                return BadRequest(message);
            }
        }
    }
}
