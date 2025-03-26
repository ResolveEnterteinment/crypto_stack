using Domain.Interfaces;
using Domain.Models.Exchange;
using MongoDB.Driver;

namespace Application.Interfaces.Exchange
{
    public interface IExchangeService : IRepository<ExchangeOrderData>
    {
        public Dictionary<string, IExchange> Exchanges { get; }
        public Task<IClientSessionHandle> StartDBSession();
    }
}
