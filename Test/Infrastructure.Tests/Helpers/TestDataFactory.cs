// File: TestDataFactory.cs
using Binance.Net.Objects.Models.Spot;
using Domain.Models.Crypto;
using Domain.Models.Subscription;
using Domain.Models.Transaction;
using MongoDB.Bson;

namespace Infrastructure.Tests.Helpers
{
    public static class TestDataFactory
    {
        public static TransactionData CreateDefaultTransactionData(decimal netAmount = 100m)
        {
            return new TransactionData
            {
                _id = ObjectId.GenerateNewId(),
                CreateTime = DateTime.UtcNow,
                UserId = ObjectId.GenerateNewId(),
                SubscriptionId = ObjectId.GenerateNewId(),
                PaymentProviderId = "pp1",
                TotalAmount = 103,
                PaymentProviderFee = 2m,
                PlatformFee = 1,
                NetAmount = netAmount,
                Status = "Pending"
            };
        }

        public static CoinAllocation CreateDefaultCoinAllocation(uint percentAmount = 100, ObjectId? coinId = null)
        {
            return new CoinAllocation
            {
                CoinId = coinId ?? ObjectId.GenerateNewId(),
                PercentAmount = percentAmount
            };
        }

        public static CoinData CreateDefaultCoinData(ObjectId coinId)
        {
            return new CoinData
            {
                _id = coinId,
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
                QuoteQuantity = 70m,  // Assume 70 is the expected quote quantity for a successful order.
                CreateTime = DateTime.UtcNow,
                QuantityFilled = 0.001m,
                Status = Binance.Net.Enums.OrderStatus.Filled,
            };
        }
    }
}
