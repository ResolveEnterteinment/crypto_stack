using Domain.Interfaces;
using Domain.Models.Transaction;

namespace Application.Interfaces
{
    public interface ITransactionService : IRepository<TransactionData>
    {
    }
}
