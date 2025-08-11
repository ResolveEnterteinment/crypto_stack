using Domain.Constants;

namespace Domain.DTOs.Exchange
{
    public class OrderResult
    {
        public bool IsSuccess { get; }
        public string Exchange { get; }
        public long? OrderId { get; }             // Nullable to indicate no ID on failure. Id of the exchange order.
        public string? AssetId { get; }                 // e.g., "BTCUSDT" for context
        public decimal? QuoteQuantity { get; }      // Nullable to indicate no quote quantity on failure. Amount of fiat to make the crypto purchase.
        public decimal? OrderQuantity { get; }      // Nullable to indicate no quantity on failure. Amount of crypto purchased.
        public string? Status { get; }
        public FailureReason? FailureReason { get; }  // Null if successful
        public string? ErrorMessage { get; }           // Detailed error if failed

        private OrderResult(bool isSuccess, string exchange, long? orderId, string? assetId, decimal? quoteQuantity, decimal? orderQuantity, string? status, FailureReason? failureReason = null, string? errorMessage = null)
        {
            IsSuccess = isSuccess;
            Exchange = exchange;
            OrderId = orderId;
            AssetId = assetId;
            QuoteQuantity = quoteQuantity;
            OrderQuantity = orderQuantity;
            Status = status;
            FailureReason = failureReason;
            ErrorMessage = errorMessage;
        }

        public static OrderResult Success(string exchange, long orderId, string coinId, decimal? quoteQuantity, decimal? orderQuantity, string? status) =>
            new OrderResult(
                true,
                exchange,
                orderId,
                coinId,
                quoteQuantity,
                orderQuantity,
                status);

        public static OrderResult Failure(string exchange, string? coinId, FailureReason reason, string errorMessage) =>
            new OrderResult(false, exchange, null, coinId, null, null, null, reason, errorMessage);
    }
}
