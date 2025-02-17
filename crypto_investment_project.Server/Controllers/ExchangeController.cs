using Application.Interfaces;
using Domain.Models.Transaction;
using Microsoft.AspNetCore.Mvc;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ExchangeController(IExchangeService exchangeService) : ControllerBase
    {
        private readonly IExchangeService _exchangeService = exchangeService;

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] TransactionData transactionData)
        {
            if (transactionData is null)
            {
                return BadRequest("A valid transaction is required.");
            }

            try
            {
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
