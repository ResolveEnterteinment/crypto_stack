﻿// Improved and refactored TransactionService
using Application.Interfaces;
using Application.Interfaces.Payment;
using Domain.DTOs;
using Domain.DTOs.Settings;
using Domain.DTOs.Subscription;
using Domain.Models.Payment;
using Domain.Models.Transaction;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class TransactionService : BaseService<TransactionData>, ITransactionService
    {
        // Cache duration specific to transactions
        private static readonly TimeSpan TRANSACTION_CACHE_DURATION = TimeSpan.FromMinutes(10);
        private readonly ISubscriptionService _subscriptionService;
        private readonly IAssetService _assetService;
        private readonly IBalanceService _balanceService;
        private readonly IPaymentService _paymentService;

        public TransactionService(
            IOptions<MongoDbSettings> mongoDbSettings,
            IMongoClient mongoClient,
            ILogger<TransactionService> logger,
            ISubscriptionService subscriptionService,
            IAssetService assetService,
            IBalanceService balanceService,
            IPaymentService paymentService,
            IMemoryCache cache
        ) : base(
            mongoClient,
            mongoDbSettings,
            "transactions",
            logger,
            cache,
            new List<CreateIndexModel<TransactionData>>
            {
                new CreateIndexModel<TransactionData>(
                    Builders<TransactionData>.IndexKeys.Ascending(t => t.UserId),
                    new CreateIndexOptions { Name = "UserId_1" }
                ),
                new CreateIndexModel<TransactionData>(
                    Builders<TransactionData>.IndexKeys.Ascending(t => t.SubscriptionId),
                    new CreateIndexOptions { Name = "SubscriptionId_1" }
                ),
                new CreateIndexModel<TransactionData>(
                    Builders<TransactionData>.IndexKeys.Ascending(t => t.PaymentProviderId),
                    new CreateIndexOptions { Name = "PaymentProviderId_1" }
                )
            }
        )
        {
            _subscriptionService = subscriptionService;
            _assetService = assetService;
            _balanceService = balanceService;
            _paymentService = paymentService;
        }

        /// <summary>
        /// Gets transactions for a specific user
        /// </summary>
        /// <param name="userId">The user ID</param>
        /// <param name="page">Page number starting from 1</param>
        /// <param name="pageSize">Number of items per page</param>
        /// <returns>Paginated transaction results</returns>
        public async Task<ResultWrapper<PaginatedResult<TransactionData>>> GetUserTransactionsAsync(
            Guid userId,
            int page = 1,
            int pageSize = 20)
        {
            try
            {
                var filter = Builders<TransactionData>.Filter.Eq(t => t.UserId, userId);
                var paginatedResult = await GetPaginatedAsync(
                    filter,
                    page,
                    pageSize,
                    "CreatedAt",
                    false); // Sort by creation date descending

                return ResultWrapper<PaginatedResult<TransactionData>>.Success(paginatedResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get transactions for user {UserId}", userId);
                return ResultWrapper<PaginatedResult<TransactionData>>.FromException(ex);
            }
        }

        /// <summary>
        /// Gets transactions by subscription ID
        /// </summary>
        /// <param name="subscriptionId">The subscription ID</param>
        /// <returns>List of transactions for the subscription</returns>
        public async Task<ResultWrapper<IEnumerable<TransactionDto>>> GetBySubscriptionIdAsync(Guid subscriptionId)
        {
            try
            {
                // Use caching for subscription transactions
                string cacheKey = $"subscription:transactions:{subscriptionId}";

                return await GetOrCreateCachedItemAsync(
                    cacheKey,
                    async () =>
                    {
                        var filter = Builders<TransactionData>.Filter.Eq(t => t.SubscriptionId, subscriptionId);
                        var transactions = await GetAllAsync(filter);
                        var transactionsDto = new List<TransactionDto>();
                        foreach (var transaction in transactions)
                        {
                            var balance = await _balanceService.GetByIdAsync(transaction.BalanceId);
                            var payment = await _paymentService.GetOneAsync(new FilterDefinitionBuilder<PaymentData>().Eq(t => t.PaymentProviderId, transaction.PaymentProviderId));

                            if (balance == null || payment == null)
                            {
                                _logger.LogError($"Failed to fetch balance and payment data. Skipping transaction {transaction.Id}");
                                continue;
                            }
                            var asset = await _assetService.GetByIdAsync(balance.AssetId);
                            transactionsDto.Add(new TransactionDto
                            {
                                Action = transaction.Action,
                                AssetName = asset.Name,
                                AssetTicker = asset.Ticker,
                                Quantity = transaction.Quantity,
                                CreatedAt = transaction.CreatedAt,
                                QuoteQuantity = payment.NetAmount,
                                QuoteCurrency = payment.Currency
                            });
                        }
                        return ResultWrapper<IEnumerable<TransactionDto>>.Success(transactionsDto);
                    },
                    TRANSACTION_CACHE_DURATION);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get transactions for subscription {SubscriptionId}", subscriptionId);
                return ResultWrapper<IEnumerable<TransactionDto>>.FromException(ex);
            }
        }

        /// <summary>
        /// Gets transactions by payment provider ID
        /// </summary>
        /// <param name="paymentProviderId">The payment provider ID</param>
        /// <returns>List of transactions for the payment</returns>
        public async Task<ResultWrapper<IEnumerable<TransactionData>>> GetByPaymentProviderIdAsync(string paymentProviderId)
        {
            try
            {
                if (string.IsNullOrEmpty(paymentProviderId))
                {
                    throw new ArgumentException("Payment provider ID cannot be null or empty", nameof(paymentProviderId));
                }

                var filter = Builders<TransactionData>.Filter.Eq(t => t.PaymentProviderId, paymentProviderId);
                var transactions = await GetAllAsync(filter);

                return ResultWrapper<IEnumerable<TransactionData>>.Success(transactions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get transactions for payment provider ID {PaymentProviderId}", paymentProviderId);
                return ResultWrapper<IEnumerable<TransactionData>>.FromException(ex);
            }
        }

        /// <summary>
        /// Creates a transaction and ensures proper cache invalidation
        /// </summary>
        /// <param name="transaction">The transaction to create</param>
        /// <returns>Result with the created transaction ID</returns>
        public async Task<ResultWrapper<Guid>> CreateTransactionAsync(TransactionData transaction)
        {
            try
            {
                if (transaction == null)
                {
                    throw new ArgumentNullException(nameof(transaction));
                }

                if (transaction.Id == Guid.Empty)
                {
                    transaction.Id = Guid.NewGuid();
                }

                var result = await InsertOneAsync(transaction);

                if (!result.IsAcknowledged || result.InsertedId == null)
                {
                    return ResultWrapper<Guid>.Failure(
                        Domain.Constants.FailureReason.DatabaseError,
                        "Failed to create transaction record");
                }

                // Return the transaction ID
                return ResultWrapper<Guid>.Success(result.InsertedId.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create transaction");
                return ResultWrapper<Guid>.FromException(ex);
            }
        }
    }
}