using Application.Interfaces;
using Application.Interfaces.Exchange;
using Domain.DTOs;
using Domain.Events;
using Domain.Exceptions;
using Domain.Models.Event;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Infrastructure.Services.Exchange
{
    public class BalanceManagementService : IBalanceManagementService
    {
        private readonly IExchangeService _exchangeService;
        private readonly IEventService _eventService;
        private readonly ILogger<BalanceManagementService> _logger;
        public BalanceManagementService(
            IExchangeService exchangeService,
            IEventService eventService,
            ILogger<BalanceManagementService> logger
            )
        {
            _exchangeService = exchangeService ?? throw new ArgumentNullException(nameof(exchangeService));
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<ResultWrapper<decimal>> CheckExchangeBalanceAsync(string exchangeName, string ticker, decimal amount)
        {
            try
            {
                var exchange = _exchangeService.Exchanges[exchangeName];
                var balanceData = await exchange.GetBalanceAsync(exchange.ReserveAssetTicker);
                if (!balanceData.IsSuccess)
                {
                    throw new BalanceFetchException($"Failed to fetch balance for {exchange}:{ticker}");
                }

                decimal fiatBalance = balanceData.Data?.Available ?? 0m;

                if (fiatBalance < amount)
                {
                    _logger.LogWarning("Insufficient balance for {Required}", amount);
                    await RequestFunding(amount);
                    throw new InsufficientBalanceException($"Insufficient balance.");
                }

                return ResultWrapper<decimal>.Success(fiatBalance);
            }
            catch (Exception ex)
            {
                return ResultWrapper<decimal>.FromException(ex);
            }
        }

        public async Task RequestFunding(decimal amount)
        {
            try
            {
                var storedEvent = new EventData
                {
                    EventType = typeof(RequestfundingEvent).Name,
                    Payload = amount.ToString()
                };
                var storedEventResult = await _eventService.InsertOneAsync(storedEvent);
                if (!storedEventResult.IsAcknowledged || storedEventResult.InsertedId is null)
                {
                    throw new MongoException($"Failed to store {storedEvent.EventType} event data with payload {storedEvent.Payload}.");
                }
                await _eventService.Publish(new RequestfundingEvent(amount, storedEventResult.InsertedId.Value));
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}
