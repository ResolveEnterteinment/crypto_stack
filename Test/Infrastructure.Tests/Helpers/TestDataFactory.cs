// File: TestDataFactory.cs
using Application.Contracts.Requests.Exchange;
using Binance.Net.Objects.Models.Spot;
using Domain.Models.Crypto;
using Domain.Models.Subscription;
using Domain.Models.Transaction;
using MongoDB.Bson;

namespace Infrastructure.Tests.Helpers
{
    public static class TestDataFactory
    {
        public static ExchangeRequest CreateDefaultExchangeRequest(decimal netAmount = 100m)
        {
            return new ExchangeRequest
            {
                Id = ObjectId.GenerateNewId().ToString(),
                CreateTime = DateTime.UtcNow,
                UserId = ObjectId.GenerateNewId().ToString(),
                SubscriptionId = ObjectId.GenerateNewId().ToString(),
                PaymentProviderId = "pp1",
                TotalAmount = 103,
                PaymentProviderFee = 2m,
                PlatformFee = 1,
                NetAmount = netAmount,
                Status = "Pending"
            };
        }

        public static TransactionData CreateDefaultTransactionData(ExchangeRequest request)
        {
            TransactionData transactionData = new()
            {
                UserId = new ObjectId(request.UserId),
                SubscriptionId = new ObjectId(request.SubscriptionId),
                TransactionId = ObjectId.Parse(request.Id),
                PaymentProviderId = request.PaymentProviderId,
                PaymentProviderFee = request.PaymentProviderFee,
                TotalAmount = request.TotalAmount,
                PlatformFee = request.PlatformFee,
                NetAmount = request.NetAmount,
                Status = request.Status,
            };
            return transactionData;
        }

        public static CoinAllocationData CreateDefaultCoinAllocation(uint percentAmount = 100, ObjectId? coinId = null)
        {
            return new CoinAllocationData
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
