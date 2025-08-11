using Domain.DTOs;
using Domain.DTOs.Exchange;

namespace Application.Interfaces.Exchange
{
    public interface IBalanceManagementService
    {
        Task<ResultWrapper<bool>> CheckExchangeBalanceAsync(string exchange, string ticker, decimal amount);
        /// <summary>
        /// Gets exchange balance with caching
        /// </summary>
        Task<ResultWrapper<ExchangeBalance>> GetCachedExchangeBalanceAsync(string exchange, string ticker);
        Task RequestFunding(decimal amount);
    }
}
