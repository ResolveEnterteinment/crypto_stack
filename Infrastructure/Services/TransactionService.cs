using Application.Interfaces;
using AspNetCore.Identity.MongoDbCore.Infrastructure;
using Domain.DTOs;
using Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class TransactionService : BaseService<BaseTransaction>, ITransactionService
    {
        public TransactionService(
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            ILogger<TransactionService> logger) : base(mongoClient, mongoDbSettings, "transactions", logger)
        {

        }

        public async Task<InsertResult> AddTransaction(BaseTransaction transaction)
        {
            return await InsertOneAsync(transaction);
        }
    }
}
