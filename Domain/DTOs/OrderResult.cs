namespace Domain.DTOs
{
    public class OrderResult
    {
        public bool IsSuccess { get; }
        public long? OrderId { get; }             // Nullable to indicate no ID on failure. Id of the exchange order.
        public bool? IsInsertSuccess { get; }         // Nullable to indicate no ID on failure. Id of the database insert operation.
        public bool? IsUpdateSuccess { get; }         // Nullable to indicate no ID on failure. Id of the database insert operation.
        public string? CoinId { get; }                 // e.g., "BTCUSDT" for context
        public decimal? QuoteQuantity { get; }      // Nullable to indicate no quote quantity on failure. Amount of fiat to make the crypto purchase.
        public decimal? OrderQuantity { get; }      // Nullable to indicate no quantity on failure. Amount of crypto purchased.
        public string? Status { get; }
        public string? FailureReason { get; }  // Null if successful
        public string? ErrorMessage { get; }           // Detailed error if failed

        private OrderResult(bool isSuccess, long? orderId, bool? isInsertSuccess, bool? isUpdatesuccess, string? coinId, decimal? quoteQuantity, decimal? orderQuantity, string? status, string? failureReason = null, string? errorMessage = null)
        {
            IsSuccess = isSuccess;
            OrderId = orderId;
            IsInsertSuccess = isInsertSuccess;
            IsUpdateSuccess = isUpdatesuccess;
            CoinId = coinId;
            QuoteQuantity = quoteQuantity;
            OrderQuantity = orderQuantity;
            Status = status;
            FailureReason = failureReason;
            ErrorMessage = errorMessage;
        }

        public static OrderResult Success(long orderId, bool isInsertSuccess, bool isUpdateSuccess, string coinId, decimal? quoteQuantity, decimal? orderQuantity, string? status) =>
            new OrderResult(true, orderId, isInsertSuccess, isUpdateSuccess, coinId, quoteQuantity, orderQuantity, status);

        public static OrderResult Failure(string? coinId, string reason, string errorMessage) =>
            new OrderResult(false, null, null, null, coinId, null, null, null, reason, errorMessage);
    }
}
