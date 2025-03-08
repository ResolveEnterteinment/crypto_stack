using Domain.DTOs;
using Domain.Interfaces;
using Domain.Models.Transaction;
using MongoDB.Driver;

namespace Application.Interfaces
{
    public interface ITransactionService : IRepository<TransactionData>
    {
        public Task<InsertResult> AddTransactionAsync(TransactionData transaction, IClientSessionHandle? session = null);
    }
}
