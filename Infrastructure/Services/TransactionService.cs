using Application.Interfaces;
using Domain.DTOs.Settings;
using Domain.Models.Transaction;
using Microsoft.Extensions.Caching.Memory;
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
            ILogger<TransactionService> logger,
            IMemoryCache cache
            ) : base(
                mongoClient,
                mongoDbSettings,
                "transactions",
                logger,
                cache)
        {
        }
    }
}
