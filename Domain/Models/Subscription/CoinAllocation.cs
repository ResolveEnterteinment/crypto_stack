using MongoDB.Bson;

namespace Domain.Models.Subscription
{
    public class CoinAllocation
    {
        public required ObjectId CoinId { get; set; }
        public required int Allocation { get; set; } // 0-100
    }
}
