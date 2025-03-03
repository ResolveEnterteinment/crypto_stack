// File: TestDataFactory.cs
using Application.Contracts.Requests.Payment;
using Binance.Net.Objects.Models.Spot;
using Domain.Models.Crypto;
using Domain.Models.Subscription;
using MongoDB.Bson;

namespace Infrastructure.Tests.Helpers
{
    public static class TestDataFactory
    {
        public static PaymentRequest CreateDefaultExchangeRequest(long netAmount = 100)
        {
            return new PaymentRequest
            {
                UserId = ObjectId.GenerateNewId().ToString(),
                SubscriptionId = ObjectId.GenerateNewId().ToString(),
                PaymentId = "pp1",
                TotalAmount = 103,
                PaymentProviderFee = 2,
                PlatformFee = 1,
                NetAmount = netAmount,
                Currency = "USD",
                Status = "Pending"
            };
        }

        public static Domain.Models.Payment.PaymentData CreateDefaultTransactionData(PaymentRequest request)
        {
            Domain.Models.Payment.PaymentData transactionData = new()
            {
                UserId = new ObjectId(request.UserId),
                SubscriptionId = new ObjectId(request.SubscriptionId),
                PaymentProviderId = request.PaymentId,
                PaymentProviderFee = request.PaymentProviderFee,
                TotalAmount = request.TotalAmount,
                PlatformFee = request.PlatformFee,
                NetAmount = request.NetAmount,
                Status = request.Status,
            };
            return transactionData;
        }

        public static AllocationData CreateDefaultCoinAllocation(uint percentAmount = 100, ObjectId? coinId = null)
        {
            return new AllocationData
            {
                AssetId = coinId ?? ObjectId.GenerateNewId(),
                PercentAmount = percentAmount
            };
        }

        public static AssetData CreateDefaultCoinData(ObjectId coinId)
        {
            return new AssetData
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
        public static Domain.Models.Subscription.SubscriptionData CreateDefaultSubscription()
        {
            return new Domain.Models.Subscription.SubscriptionData
            {
                UserId = ObjectId.GenerateNewId(),
                Interval = "Monthly",
                Amount = 100,
                Allocations = new List<AllocationData>(){
                    CreateDefaultCoinAllocation(60),
                    CreateDefaultCoinAllocation(40)
                }
            };
        }
    }
}
