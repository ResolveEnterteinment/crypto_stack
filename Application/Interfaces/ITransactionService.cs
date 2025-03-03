using Domain.DTOs;
using Domain.Models;

namespace Application.Interfaces
{
    public interface ITransactionService
    {
        public Task<InsertResult> AddTransaction(BaseTransaction transaction);
    }
}
