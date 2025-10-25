using Domain.Constants.Transaction;
using Domain.Models.Transaction;

namespace Application.Contracts.Responses.Transaction
{
    public class TransactionResponse : BaseResponse
    {
        public string Action { get; set; } = string.Empty;
        public string AssetName { get; set; } = string.Empty;
        public string AssetTicker { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public DateTime CreatedAt { get; set; }
        public decimal QuoteQuantity { get; set; }
        public string QuoteCurrency { get; set; } = string.Empty;
    }
}
