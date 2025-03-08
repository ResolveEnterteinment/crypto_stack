namespace Domain.DTOs
{
    public class AllocationOrdersResult
    {
        public bool IsSuccess { get; }                // Overall success (true if all orders succeeded)
        public int TotalOrders { get; }               // Number of orders attempted
        public int SuccessfulOrders { get; }          // Number of orders that succeeded
        public IReadOnlyList<OrderResult> Orders { get; }  // Detailed results for each order
        public string? ErrorSummary { get; }           // High-level error message if not fully successful

        public AllocationOrdersResult(IReadOnlyList<OrderResult> orders)
        {
            Orders = orders;
            TotalOrders = orders.Count;
            SuccessfulOrders = orders.Count(o => o.IsSuccess);
            IsSuccess = Orders.Any() && SuccessfulOrders == TotalOrders;
            ErrorSummary = IsSuccess ? null : $"Failed to process {TotalOrders - SuccessfulOrders} out of {TotalOrders} orders.";
        }
    }
}
