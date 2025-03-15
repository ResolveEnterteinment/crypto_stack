using Application.Interfaces;
using Domain.DTOs;
using Domain.Models.Transaction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class TransactionService : BaseService<TransactionData>, ITransactionService
    {
        public TransactionService(
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            ILogger<TransactionService> logger) : base(mongoClient, mongoDbSettings, "transactions", logger)
        {
        }
    }
}
