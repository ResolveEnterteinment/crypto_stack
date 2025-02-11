using Application.Interfaces;
using BinanceLibrary;
using Domain.DTOs;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services
{
    public class ExchangeService : IExchangeService
    {
        private readonly IBinanceService _binanceService;
        public ExchangeService(IOptions<BinanceSettings> binanceSettings)
        {
            this._binanceService = new BinanceService(binanceSettings);
        }
    }
}
