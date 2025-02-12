using Domain.Models;

namespace Application.Interfaces
{
    public interface IExchangeService
    {
        public Task<ExchangeOrderData?> CreateOrder(string symbol, decimal quantity, string side = "BUY", string type = "MARKET");
    }
}
