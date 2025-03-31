using Domain.DTOs;

namespace Application.Interfaces.Exchange
{
    public interface IBalanceManagementService
    {
        public Task<ResultWrapper<bool>> CheckExchangeBalanceAsync(string exchange, string ticker, decimal amount);
        public Task RequestFunding(decimal amount);
    }
}
