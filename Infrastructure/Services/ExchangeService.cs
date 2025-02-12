using Application.Interfaces;
using Binance.Net.Objects.Models.Spot;
using BinanceLibrary;
using Domain.DTOs;
using Domain.Models;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services
{
    public class ExchangeService : IExchangeService
    {
        private readonly IBinanceService _binanceService;

        public ExchangeService(IOptions<BinanceSettings> binanceSettings)
        {
            _binanceService = new BinanceService(binanceSettings);
        }

        /// <summary>
        /// Creates an exchange order.
        /// </summary>
        /// <param name="symbol">The trading pair (e.g., BTCUSDT).</param>
        /// <param name="quantity">The amount of the quote asset (e.g., USDT) to spend.</param>
        /// <param name="side">Order side: BUY or SELL. Default value is BUY.</param>
        /// <param name="type">Order type. Default value is MARKET.</param>
        /// <returns>The order details if successful; otherwise, null.</returns>
        public async Task<ExchangeOrderData?> CreateOrder(string symbol, decimal quantity, string side = "BUY", string type = "MARKET")
        {
            BinancePlacedOrder? order = null;

            // Normalize the side value and handle only BUY or SELL.
            if (side.Equals("BUY", StringComparison.OrdinalIgnoreCase))
            {
                order = await _binanceService.PlaceSpotMarketBuyOrder(symbol, quantity);
            }
            else if (side.Equals("SELL", StringComparison.OrdinalIgnoreCase))
            {
                order = await _binanceService.PlaceSpotMarketSellOrder(symbol, quantity);
            }
            else
            {
                throw new ArgumentException("Invalid order side. Allowed values are BUY or SELL.", nameof(side));
            }

            if (order == null)
            {
                return null;
            }

            var exchangeOrder = new ExchangeOrderData
            {
                Id = Guid.NewGuid(), // Generates a new unique identifier.
                CreateTime = order.CreateTime,
                UserId = "1",          // TO-DO: Replace with the actual user id.
                TranscationId = "4",   // TO-DO: Replace with the actual transaction id.
                CryptoId = order.Symbol,
                Quantity = order.QuoteQuantity,
                OrderId = order.Id,
                Status = order.Status.ToString(),
            };

            return exchangeOrder;
        }

        /// <summary>
        /// Displays order details in a readable format.
        /// </summary>
        /// <param name="order">The Binance order details.</param>
        public static void DisplayOrderDetails(BinancePlacedOrder order)
        {
            Console.WriteLine(new string('-', 40));
            Console.WriteLine($"Order ID:                   {order.Id}");
            // Assuming BinancePlacedOrder has a ClientOrderId property.
            Console.WriteLine($"Client Order ID:            {order.ClientOrderId}");
            Console.WriteLine($"Symbol:                     {order.Symbol}");
            Console.WriteLine($"Side:                       {order.Side}");
            Console.WriteLine($"Price:                      {order.Price}");
            Console.WriteLine($"Executed Quantity:          {order.QuoteQuantity}");
            // If available, use the cumulative quote quantity property.
            //Console.WriteLine($"Cumulative Quote Quantity:  {order.CummulativeQuoteQty}");
            Console.WriteLine($"Order Create Time:          {order.CreateTime}");
            Console.WriteLine($"Status:                     {order.Status}");
            Console.WriteLine(new string('-', 40));
        }

        /// <summary>
        /// Gets crypto data for a given symbol.
        /// </summary>
        /// <param name="symbol">The trading pair symbol (e.g., BTCUSDT).</param>
        /// <returns>A CryptoData object with information about the cryptocurrency.</returns>
        public static CryptoData GetCryptoFromSymbol(string symbol)
        {
            // In a production scenario, use a proper mapping or database lookup.
            if (symbol.Equals("BTCUSDT", StringComparison.OrdinalIgnoreCase))
            {
                return new CryptoData
                {
                    Id = Guid.NewGuid(),
                    CreateTime = DateTime.Now,
                    Name = "Bitcoin",
                    Ticker = "BTC",
                    Symbol = "₿",
                    Precision = 8,
                    SubunitName = "Satoshi",
                };
            }
            // Fallback for unknown symbols.
            return new CryptoData
            {
                Id = Guid.NewGuid(),
                CreateTime = DateTime.Now,
                Name = "Unknown",
                Ticker = "Unknown",
                Symbol = "Unknown",
                Precision = 0,
                SubunitName = "Unknown",
            };
        }
    }
}
