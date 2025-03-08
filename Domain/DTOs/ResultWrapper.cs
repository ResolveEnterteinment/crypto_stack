namespace Domain.DTOs
{
    public class ResultWrapper<T>
    {
        public bool IsSuccess { get; }
        public T? Data { get; }
        public string? FailureReason { get; }   // Null if successful
        public string? ErrorMessage { get; }    // Detailed error if failed

        private ResultWrapper(bool isSuccess, T? data, string? failureReason = null, string? errorMessage = null)
        {
            IsSuccess = isSuccess;
            Data = data;
            FailureReason = failureReason;
            ErrorMessage = errorMessage;
        }

        public static ResultWrapper<T> Success(T data)
        {
            return new ResultWrapper<T>(true, data);
        }

        public static ResultWrapper<T> Failure(string reason, string errorMessage) =>
            new ResultWrapper<T>(false, default, reason, errorMessage);
    }
}
