// File: TestDataFactory.cs in your Test project
using Binance.Net.Objects.Models.Spot;

using Domain.Models.Crypto;
using Domain.Models.Subscription;
using Domain.Models.Transaction;
using MongoDB.Bson;

namespace Infrastructure.Tests.Helpers
{
    public static class TestDataFactory
    {
        public static TransactionData CreateDefaultTransactionData(
            decimal netAmount = 96.7m,
            ObjectId? subscriptionId = null,
            ObjectId? userId = null)
        {
            return new TransactionData
            {
                _id = ObjectId.GenerateNewId(),
                CreateTime = DateTime.UtcNow,
                UserId = userId ?? ObjectId.GenerateNewId(),
                SubscriptionId = subscriptionId ?? ObjectId.GenerateNewId(),
                PaymentProviderId = "pp1",
                TotalAmount = 100,
                PaymentProviderFee = 2.3m,
                PlatformFee = 1,
                NetAmount = netAmount,
                Status = "Pending"
            };
        }

        public static CoinAllocation CreateDefaultCoinAllocation(
            uint allocation = 100,
            ObjectId? coinId = null)
        {
            return new CoinAllocation
            {
                CoinId = coinId ?? ObjectId.GenerateNewId(),
                PercentAmount = allocation
            };
        }

        public static CoinData CreateDefaultCoinData(ObjectId? coinId = null)
        {
            return new CoinData
            {
                _id = coinId ?? ObjectId.GenerateNewId(),
                CreateTime = DateTime.UtcNow,
                Name = "Bitcoin",
                Symbol = "₿",
                Ticker = "BTC",
                Precision = 8,
                SubunitName = "Satoshi"
            };
        }

        public static BinancePlacedOrder CreateDefaultOrder()
        {
            return new BinancePlacedOrder
            {
                Id = 12345,
                ClientOrderId = "clientOrder123",
                Symbol = "BTCUSDT",
                Side = Binance.Net.Enums.OrderSide.Buy,
                Price = 97000m,
                QuoteQuantity = 96.7m,
                CreateTime = DateTime.UtcNow,
                QuantityFilled = 0.01m,
                Status = Binance.Net.Enums.OrderStatus.Filled
            };
        }
    }
}
