// Refactored BalanceManagementService 
using Application.Interfaces;
using Application.Interfaces.Exchange;
using Domain.Constants;
using Domain.DTOs;
using Domain.Events;
using Domain.Exceptions;
using Domain.Models.Event;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Polly;
using Polly.Retry;
using System.Diagnostics;

namespace Infrastructure.Services.Exchange
{
    /// <summary>
    /// Service for managing exchange balances, including checking balance levels and requesting funding.
    /// </summary>
    public class BalanceManagementService : IBalanceManagementService
    {
        private readonly IExchangeService _exchangeService;
        private readonly IEventService _eventService;
        private readonly ILogger<BalanceManagementService> _logger;
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly IMemoryCache _cache;
        private const string BALANCE_CHECK_CACHE_PREFIX = "balance_check:";
        private static readonly TimeSpan BALANCE_CHECK_CACHE_DURATION = TimeSpan.FromMinutes(1);

        /// <summary>
        /// Initializes a new instance of the <see cref="BalanceManagementService"/> class.
        /// </summary>
        public BalanceManagementService(
            IExchangeService exchangeService,
            IEventService eventService,
            ILogger<BalanceManagementService> logger,
            IMemoryCache cache)
        {
            _exchangeService = exchangeService ?? throw new ArgumentNullException(nameof(exchangeService));
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));

            // Configure retry policy for exchange operations
            _retryPolicy = Policy
                .Handle<ExchangeApiException>()
                .Or<MongoException>()
                .WaitAndRetryAsync(
                    3,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    (ex, timeSpan, retryCount, context) =>
                    {
                        _logger.LogWarning(ex,
                            "Error checking exchange balance. Retrying {RetryCount}/3 after {RetryInterval}ms",
                            retryCount, timeSpan.TotalMilliseconds);
                    });
        }

        /// <summary>
        /// Checks the exchange balance for a specific ticker and ensures it has sufficient funds, with caching.
        /// </summary>
        public async Task<ResultWrapper<bool>> CheckExchangeBalanceAsync(string exchangeName, string ticker, decimal amount)
        {
            #region Validate
            if (string.IsNullOrEmpty(exchangeName))
            {
                return ResultWrapper<bool>.Failure(
                    FailureReason.ValidationError,
                    "Exchange name cannot be null or empty");
            }

            if (string.IsNullOrEmpty(ticker))
            {
                return ResultWrapper<bool>.Failure(
                    FailureReason.ValidationError,
                    "Ticker cannot be null or empty");
            }

            if (amount <= 0)
            {
                return ResultWrapper<bool>.Failure(
                    FailureReason.ValidationError,
                    "Amount must be greater than zero");
            }
            #endregion

            var correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString();

            // Check cache first to avoid excessive balance checks for the same parameters
            string cacheKey = $"{BALANCE_CHECK_CACHE_PREFIX}{exchangeName}:{ticker}:{Math.Round(amount, 2)}";

            if (_cache.TryGetValue(cacheKey, out ResultWrapper<bool> cachedResult))
            {
                _logger.LogInformation("Using cached balance check for {Exchange}:{Ticker}", exchangeName, ticker);
                return cachedResult;
            }

            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["Exchange"] = exchangeName,
                ["Ticker"] = ticker,
                ["RequiredAmount"] = amount,
                ["Operation"] = "CheckExchangeBalance",
                ["CorrelationId"] = correlationId
            }))
            {
                try
                {
                    _logger.LogInformation("Checking balance for {Exchange}:{Ticker}", exchangeName, ticker);

                    // Apply retry policy for balance check
                    var result = await _retryPolicy.ExecuteAsync(async () =>
                    {
                        if (!_exchangeService.Exchanges.TryGetValue(exchangeName, out var exchange))
                        {
                            throw new ExchangeApiException($"Exchange '{exchangeName}' not found", exchangeName);
                        }

                        // Get the reserve asset (typically a stablecoin)
                        var reserveAssetTicker = exchange.ReserveAssetTicker;

                        // Use the cached balance method from exchange service
                        var balanceResult = await _exchangeService.GetCachedExchangeBalanceAsync(exchangeName, reserveAssetTicker);

                        if (!balanceResult.IsSuccess)
                        {
                            throw new ExchangeApiException(
                                $"Failed to fetch exchange balance {exchange.Name}:{reserveAssetTicker}: {balanceResult.ErrorMessage}",
                                exchangeName
                            );
                        }

                        decimal fiatBalance = balanceResult.Data?.Available ?? 0m;
                        decimal threshold = amount * 0.05m; // 5% buffer

                        _logger.LogInformation(
                            "Balance for {Exchange}:{Ticker}: Available={Available}, Required={Required}",
                            exchangeName, reserveAssetTicker, fiatBalance, amount);

                        if (fiatBalance < amount)
                        {
                            _logger.LogWarning(
                                "Insufficient balance for {Exchange}:{Ticker}. Available: {Available}, Required: {Required}",
                                exchangeName, reserveAssetTicker, fiatBalance, amount);

                            // Request more funds if balance is below required amount
                            await RequestFunding(amount - fiatBalance + threshold);

                            var insufficientResult = ResultWrapper<bool>.Failure(
                                FailureReason.InsufficientBalance,
                                $"Insufficient balance. Available: {fiatBalance}, Required: {amount}",
                                "INSUFFICIENT_FUNDS");

                            // Cache the negative result for a short period
                            _cache.Set(cacheKey, insufficientResult, TimeSpan.FromSeconds(30));

                            return insufficientResult;
                        }

                        // Check if balance is getting low (less than 20% over required amount)
                        if (fiatBalance < amount * 1.2m)
                        {
                            _logger.LogInformation(
                                "Balance for {Exchange}:{Ticker} is getting low. Available: {Available}, Required: {Required}",
                                exchangeName, reserveAssetTicker, fiatBalance, amount);

                            // Preemptively request more funds in the background
                            _ = Task.Run(() => RequestFunding(amount));
                        }

                        var successResult = ResultWrapper<bool>.Success(true);

                        // Cache the successful result
                        _cache.Set(cacheKey, successResult, BALANCE_CHECK_CACHE_DURATION);

                        return successResult;
                    });

                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error checking exchange balance for {Exchange}:{Ticker}",
                        exchangeName, ticker);

                    var errorResult = ResultWrapper<bool>.FromException(ex);

                    // Cache error results for a very short time to prevent hammering the service
                    _cache.Set(cacheKey, errorResult, TimeSpan.FromSeconds(15));

                    return errorResult;
                }
            }
        }

        /// <summary>
        /// Requests additional funding by publishing a funding request event.
        /// </summary>
        public async Task RequestFunding(decimal amount)
        {
            if (amount <= 0)
            {
                _logger.LogWarning("Invalid funding request amount: {Amount}", amount);
                return;
            }

            var correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString();

            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["RequestedAmount"] = amount,
                ["Operation"] = "RequestFunding",
                ["CorrelationId"] = correlationId
            }))
            {
                try
                {
                    // Check if we've recently requested funding for a similar amount
                    string cacheKey = $"funding_request:{Math.Round(amount, 0)}";

                    if (_cache.TryGetValue(cacheKey, out bool _))
                    {
                        _logger.LogInformation(
                            "Skipping duplicate funding request for {Amount} - already requested recently",
                            amount);
                        return;
                    }

                    _logger.LogInformation("Requesting additional funding of {Amount}", amount);

                    // Create an event record
                    var storedEvent = new EventData
                    {
                        Name = typeof(RequestfundingEvent).Name,
                        Payload = new
                        {
                            Amount = amount,
                            Timestamp = DateTime.UtcNow,
                            CorrelationId = correlationId
                        }
                    };

                    // Store the event
                    var storedEventResult = await _eventService.InsertOneAsync(storedEvent);

                    if (!storedEventResult.IsAcknowledged || storedEventResult.InsertedId is null)
                    {
                        throw new DatabaseException(
                            $"Failed to store {storedEvent.GetType().Name} event: {storedEventResult?.ErrorMessage ?? "Unknown error"}");
                    }

                    // Publish the event to be handled by relevant services
                    await _eventService.Publish(new RequestfundingEvent(amount, storedEventResult.InsertedId.Value));

                    // Cache the request to prevent duplicates
                    _cache.Set(cacheKey, true, TimeSpan.FromMinutes(15));

                    _logger.LogInformation(
                        "Successfully published funding request event. Amount: {Amount}, EventId: {EventId}",
                        amount, storedEventResult.InsertedId.Value);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to request funding of {Amount}", amount);
                    throw;
                }
            }
        }
    }
}