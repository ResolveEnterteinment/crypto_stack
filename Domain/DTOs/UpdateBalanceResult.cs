using Domain.Models.Balance;

namespace Domain.DTOs
{
    public class UpdateBalanceResult
    {
        public bool IsSuccess { get; }                                  // Overall success (true if all allocations are fetched)
        public IReadOnlyList<BalanceData> Balances { get; }             // Detailed results for each order
        public string? ErrorSummary { get; }                            // High-level error message if not fully successful
        public string? FailureReason { get; }                           // Null if successful
        public string? ErrorMessage { get; }                            // Detailed error if failed

        public UpdateBalanceResult(bool isSuccess, IReadOnlyList<BalanceData> balances, string? failureReason = null, string? errorMessage = null)
        {
            IsSuccess = isSuccess;
            Balances = balances ?? throw new ArgumentNullException(nameof(balances));
            FailureReason = failureReason;
            ErrorMessage = errorMessage;
        }

        public static UpdateBalanceResult Success(IReadOnlyList<BalanceData> balances)
        {
            if (!balances.Any())
            {
                return Failure(Constants.FailureReason.ValidationError, "Balances can not be empty.");
            }
            return new UpdateBalanceResult(true, balances);
        }

        public static UpdateBalanceResult Failure(string reason, string errorMessage)
        {
            return new UpdateBalanceResult(false, new List<BalanceData>().AsReadOnly(), reason, errorMessage);
        }
    }
}
