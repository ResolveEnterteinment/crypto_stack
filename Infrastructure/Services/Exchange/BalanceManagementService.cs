using Application.Interfaces.Base;
using Application.Interfaces.Exchange;
using Application.Interfaces.Logging;
using Domain.Constants;
using Domain.Constants.Logging;
using Domain.DTOs;
using Domain.DTOs.Base;
using Domain.DTOs.Exchange;
using Domain.DTOs.Logging;
using Domain.Events;
using Domain.Events.Exchange;
using Domain.Exceptions;
using Domain.Models.Event;
using Infrastructure.Services.Base;
using Microsoft.Extensions.Caching.Memory;
using MongoDB.Driver;
using Stripe.V2;
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
        private readonly ICacheService<ExchangeBalance> _cacheService;
        private readonly ILoggingService _logger;
        private readonly IResilienceService<ExchangeBalance> _resilienceService;
        private readonly IMemoryCache _cache;
        private const string BALANCE_CHECK_CACHE_FORMAT = "balance_check:{0}:{1}:{2}";
        private const string BALANCE_CACHE_FORMAT = "balance:{0}:{1}";
        private const string FUNDING_REQUEST_CACHE_FORMAT = "funding_request:{0}";
        private static readonly TimeSpan BALANCE_CHECK_CACHE_DURATION = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan BALANCE_CACHE_DURATION = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan FUNDING_REQUEST_CACHE_DURATION = TimeSpan.FromMinutes(15);

        /// <summary>
        /// Initializes a new instance of the <see cref="BalanceManagementService"/> class.
        /// </summary>
        public BalanceManagementService(
            IExchangeService exchangeService,
            IEventService eventService,
            ICacheService<ExchangeBalance> cacheService, // Add this parameter
            ILoggingService logger,
            IResilienceService<ExchangeBalance> resilienceService,
            IMemoryCache cache)
        {
            _exchangeService = exchangeService ?? throw new ArgumentNullException(nameof(exchangeService));
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService)); // Add this assignment
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
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

            // Check cache first to avoid excessive balance checks for the same parameters
            string cacheKey = string.Format(BALANCE_CHECK_CACHE_FORMAT, exchangeName, ticker, Math.Round(amount, 2));

            if (_cache.TryGetValue(cacheKey, out bool cachedResult))
            {
                _logger.LogInformation("Using cached balance check for {Exchange}:{Ticker}", exchangeName, ticker);
                return cachedResult;
            }

            var scope = new Scope
            {
                NameSpace = "Infrastructure.Services.Exchange",
                FileName = "BalanceManagementService",
                OperationName = "CheckExchangeBalanceAsync(string exchangeName, string ticker, decimal amount)",
                State = {
                    ["Exchange"] = exchangeName,
                    ["Ticker"] = ticker,
                    ["Amount"] = amount,
                }
            };

            // Using the fluent builder
            return await _resilienceService.CreateBuilder<bool>(scope, async () =>
                {
                    _logger.LogInformation("Checking balance for {Exchange}:{Ticker}", exchangeName, ticker);

                    if (!_exchangeService.Exchanges.TryGetValue(exchangeName, out var exchange))
                    {
                        throw new ExchangeApiException($"Exchange '{exchangeName}' not found", exchangeName);
                    }

                    // Get the reserve asset (typically a stablecoin)
                    var reserveAssetTicker = exchange.QuoteAssetTicker;

                    // Use the cached balance method from exchange service
                    var balanceResult = await GetCachedExchangeBalanceAsync(exchangeName, reserveAssetTicker);

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
                        await _logger.LogTraceAsync(
                            $"Insufficient balance for {exchangeName}:{reserveAssetTicker}. Available: {fiatBalance}, Required: {amount}",
                            level: LogLevel.Critical, requiresResolution: true);

                        // Request more funds if balance is below required amount
                        await RequestFunding(amount - fiatBalance + threshold);

                        var insufficientResult = ResultWrapper<bool>.Failure(
                            FailureReason.InsufficientBalance,
                            $"Insufficient balance. Available: {fiatBalance}, Required: {amount}",
                            "INSUFFICIENT_FUNDS");

                        // Cache the negative result for a short period
                        _cache.Set(cacheKey, insufficientResult, TimeSpan.FromSeconds(30));

                        return false;
                    }

                    // Check if balance is getting low (less than 20% over required amount)
                    if (fiatBalance < amount * 1.2m)
                    {
                        await _logger.LogTraceAsync(
                            $"Balance for {exchangeName}:{reserveAssetTicker} is getting low. Available: {fiatBalance}, Required: {amount}", level:LogLevel.Warning, requiresResolution: true);

                        // Preemptively request more funds in the background
                        _ = Task.Run(() => RequestFunding(amount));
                    }

                    var successResult = ResultWrapper<bool>.Success(true);

                    // Cache the successful result
                    _cache.Set(cacheKey, successResult, BALANCE_CHECK_CACHE_DURATION);

                    return true;
                })
                .WithQuickOperationResilience(TimeSpan.FromSeconds(3))
                .OnError(async (ex) =>
                {
                    _logger.LogError(
                        "Error checking exchange balance for {Exchange}:{Ticker}",
                        exchangeName, ticker);

                    var errorResult = ResultWrapper<bool>.FromException(ex);

                    // Cache error results for a very short time to prevent hammering the service
                    _cache.Set(cacheKey, errorResult, TimeSpan.FromSeconds(15));
                })
                .ExecuteAsync();
        }

        public async Task<ResultWrapper<ExchangeBalance>> GetCachedExchangeBalanceAsync(string exch, string ticker)
        {
            return await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Exchange",
                    FileName = "ExchangeService",
                    OperationName = "GetCachedExchangeBalanceAsync(string exch, string ticker)",
                    State = {
                        ["Ticker"] = ticker,
                    },
                    LogLevel = LogLevel.Critical
                },
                async () =>
                {
                    if (string.IsNullOrEmpty(exch) || string.IsNullOrEmpty(ticker))
                        throw new ValidationException("Exchange and ticker required", new()
                        {
                            ["exchange"] = [exch],
                            ["ticker"] = [ticker],
                        });
                    if(!_exchangeService.Exchanges.TryGetValue(exch, out var exchange))
                        throw new ValidationException($"Unknown exchange {exch}", new() { ["exchange"] = new[] { exch } });

                    var cachedBalance = await _cacheService.GetCachedEntityAsync(
                        string.Format(BALANCE_CACHE_FORMAT, exch, ticker),
                        async () =>
                        {
                            var balanceWrapper = await exchange.GetBalanceAsync(ticker);
                            if (balanceWrapper == null || !balanceWrapper.IsSuccess)
                                throw new ExchangeApiException(balanceWrapper?.ErrorMessage ?? "Exchange balance returned null", exch);
                            return balanceWrapper.Data;
                        },
                        BALANCE_CACHE_DURATION);

                    if (cachedBalance == null)
                        throw new ExchangeApiException("Balance fetch result returned null", exch);

                    return cachedBalance;
                })
                .WithExchangeOperationResilience()
                .WithPerformanceThreshold(TimeSpan.FromSeconds(3))
                .WithPerformanceMonitoring(
                    TimeSpan.FromSeconds(3),
                    TimeSpan.FromSeconds(5))
                .ExecuteAsync();
        }

        /// <summary>
        /// Requests additional funding by publishing a funding request event.
        /// </summary>
        public async Task RequestFunding(decimal amount)
        {
            var correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString();

            await _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Exchange",
                    FileName = "BalanceManagementService",
                    OperationName = "RequestFunding(decimal amount)",
                    State = new()
                    {
                        ["RequestedAmount"] = amount,
                        ["Operation"] = "RequestFunding",
                        ["CorrelationId"] = correlationId
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    if (amount <= 0)
                    {
                        _logger.LogWarning("Invalid funding request amount: {Amount}", amount);
                        return Task.FromException(new ArgumentException(nameof(amount)));
                    }

                    // Check if we've recently requested funding for a similar amount
                    string cacheKey = string.Format(FUNDING_REQUEST_CACHE_FORMAT, Math.Round(amount, 0));

                    if (_cacheService.TryGetValue(cacheKey, out bool _))
                    {
                        _logger.LogInformation(
                            "Skipping duplicate funding request for {Amount} - already requested recently",
                            amount);
                        return Task.CompletedTask;
                    }

                    _logger.LogInformation("Requesting additional funding of {Amount}", amount);

                    // Publish the event to be handled by relevant services
                    await _eventService.PublishAsync(new RequestFundingEvent(amount, Guid.Empty, _logger.Context));

                    // Cache the request to prevent duplicates
                    _cacheService.Set(cacheKey, true, FUNDING_REQUEST_CACHE_DURATION);

                    _logger.LogInformation(
                        "Successfully published funding request event. Amount: {Amount}",
                        amount);

                    return Task.CompletedTask;
                })
                .WithQuickOperationResilience(TimeSpan.FromSeconds(2))
                .ExecuteAsync();
        }
    }
}