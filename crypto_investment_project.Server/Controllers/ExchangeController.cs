using Application.Contracts.Requests.Exchange;
using Application.Interfaces;
using Domain.Models.Transaction;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ExchangeController(IExchangeService exchangeService) : ControllerBase
    {
        private readonly IExchangeService _exchangeService = exchangeService;

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] ExchangeRequest exchangeRequest)
        {
            if (exchangeRequest is null)
            {
                return BadRequest("A valid transaction is required.");
            }

            try
            {
                TransactionData transactionData = new()
                {
                    _id = new ObjectId(exchangeRequest.Id),
                    CreateTime = exchangeRequest.CreateTime,
                    UserId = new ObjectId(exchangeRequest.UserId),
                    SubscriptionId = new ObjectId(exchangeRequest.SubscriptionId),
                    PaymentProviderId = exchangeRequest.PaymentProviderId,
                    PaymentProviderFee = exchangeRequest.PaymentProviderFee,
                    TotalAmount = exchangeRequest.TotalAmount,
                    PlatformFee = exchangeRequest.PlatformFee,
                    NetAmount = exchangeRequest.NetAmount,
                    Status = exchangeRequest.Status,
                };
                var result = await _exchangeService.ProcessTransaction(transactionData);
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
