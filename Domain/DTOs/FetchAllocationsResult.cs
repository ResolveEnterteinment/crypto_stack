using Domain.Models.Subscription;

namespace Domain.DTOs
{
    public class FetchAllocationsResult
    {
        public bool AllFilled { get; }                // Overall success (true if all orders succeeded)
        public IReadOnlyList<CoinAllocationData> Allocations { get; }  // Detailed results for each order
        public string? ErrorSummary { get; }           // High-level error message if not fully successful
        public string? FailureReason { get; }  // Null if successful
        public string? ErrorMessage { get; }           // Detailed error if failed

        public FetchAllocationsResult(bool allFilled, IReadOnlyList<CoinAllocationData> allocations, string? failureReason = null, string? errorMessage = null)
        {
            AllFilled = allFilled;
            Allocations = allocations ?? throw new ArgumentNullException(nameof(allocations));
            FailureReason = failureReason;
            ErrorMessage = errorMessage;
        }

        public static FetchAllocationsResult Success(IReadOnlyList<CoinAllocationData> allocations)
        {
            if (!allocations.Any())
            {
                return Failure(Constants.FailureReason.ValidationError, "Allocations can not be empty.");
            }
            return new FetchAllocationsResult(true, allocations);
        }

        public static FetchAllocationsResult Failure(string reason, string errorMessage)
        {
            return new FetchAllocationsResult(false, new List<CoinAllocationData>().AsReadOnly(), reason, errorMessage);
        }
    }
}
