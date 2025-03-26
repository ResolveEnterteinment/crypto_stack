using Domain.Constants;
using Domain.Exceptions;

namespace Domain.DTOs
{
    /// <summary>
    /// Enhanced wrapper for operation results with improved error handling
    /// </summary>
    /// <typeparam name="T">The type of data returned by the operation</typeparam>
    public class ResultWrapper<T>
    {
        /// <summary>
        /// Indicates whether the operation was successful
        /// </summary>
        public bool IsSuccess { get; private set; }

        /// <summary>
        /// The data returned by the operation (if successful)
        /// </summary>
        public T Data { get; private set; }

        /// <summary>
        /// Optional success message for the operation
        /// </summary>
        public string DataMessage { get; private set; }

        /// <summary>
        /// Human-readable error message (if operation failed)
        /// </summary>
        public string ErrorMessage { get; private set; }

        /// <summary>
        /// Machine-readable error code (if operation failed)
        /// </summary>
        public string ErrorCode { get; private set; }

        /// <summary>
        /// Categorized failure reason (if operation failed)
        /// </summary>
        public FailureReason Reason { get; private set; }

        /// <summary>
        /// Detailed validation errors (if applicable)
        /// </summary>
        public Dictionary<string, string[]> ValidationErrors { get; private set; }

        /// <summary>
        /// Technical details for debugging (not exposed to end users)
        /// </summary>
        public string DebugInformation { get; private set; }

        /// <summary>
        /// HTTP status code associated with this result
        /// </summary>
        public int StatusCode => IsSuccess ? 200 : Reason.ToStatusCode();

        // Protected constructor to enforce factory methods
        protected ResultWrapper() { }

        /// <summary>
        /// Creates a successful result with optional data and message
        /// </summary>
        public static ResultWrapper<T> Success(T data = default, string message = null)
        {
            return new ResultWrapper<T>
            {
                IsSuccess = true,
                Data = data,
                DataMessage = message
            };
        }

        /// <summary>
        /// Creates a failure result with detailed error information
        /// </summary>
        public static ResultWrapper<T> Failure(
            FailureReason reason,
            string errorMessage,
            string errorCode = null,
            Dictionary<string, string[]> validationErrors = null,
            string debugInformation = null)
        {
            return new ResultWrapper<T>
            {
                IsSuccess = false,
                ErrorMessage = errorMessage,
                ErrorCode = errorCode ?? reason.ToString(),
                Reason = reason,
                ValidationErrors = validationErrors,
                DebugInformation = debugInformation
            };
        }

        /// <summary>
        /// Creates a failure result from an exception
        /// </summary>
        public static ResultWrapper<T> FromException(Exception exception, bool includeStackTrace = false)
        {
            var reason = FailureReasonExtensions.FromException(exception);
            string errorCode = null;
            Dictionary<string, string[]> validationErrors = null;
            string debugInfo = includeStackTrace ? exception.StackTrace : null;

            if (exception is DomainException domainEx)
            {
                errorCode = domainEx.ErrorCode;

                if (exception is ValidationException validationEx)
                {
                    validationErrors = validationEx.ValidationErrors;
                }
            }

            return Failure(
                reason,
                exception.Message,
                errorCode,
                validationErrors,
                debugInfo
            );
        }

        /// <summary>
        /// Attempts to extract the data from a result, following the TryParse pattern
        /// </summary>
        /// <param name="result">The result to extract data from</param>
        /// <param name="data">When this method returns, contains the data from the result if successful, or the default value if unsuccessful</param>
        /// <returns>True if the result is successful and data was extracted; otherwise, false</returns>
        public static bool TryParse(ResultWrapper<T> result, out T data)
        {
            if (result != null && result.IsSuccess)
            {
                data = result.Data;
                return true;
            }

            data = default;
            return false;
        }

        /// <summary>
        /// Attempts to extract the data from a result, including error information if failed
        /// </summary>
        /// <param name="result">The result to extract data from</param>
        /// <param name="data">When this method returns, contains the data from the result if successful, or the default value if unsuccessful</param>
        /// <param name="errorMessage">When this method returns and the result is unsuccessful, contains the error message</param>
        /// <returns>True if the result is successful and data was extracted; otherwise, false</returns>
        public static bool TryParse(ResultWrapper<T> result, out T data, out string errorMessage)
        {
            if (result != null && result.IsSuccess)
            {
                data = result.Data;
                errorMessage = null;
                return true;
            }

            data = default;
            errorMessage = result?.ErrorMessage ?? "Unknown error";
            return false;
        }

        /// <summary>
        /// Attempts to extract the data from a result with comprehensive error information
        /// </summary>
        /// <param name="result">The result to extract data from</param>
        /// <param name="data">When this method returns, contains the data from the result if successful, or the default value if unsuccessful</param>
        /// <param name="errorMessage">When this method returns and the result is unsuccessful, contains the error message</param>
        /// <param name="failureReason">When this method returns and the result is unsuccessful, contains the failure reason</param>
        /// <param name="validationErrors">When this method returns and the result is unsuccessful, contains any validation errors</param>
        /// <returns>True if the result is successful and data was extracted; otherwise, false</returns>
        public static bool TryParse(
            ResultWrapper<T> result,
            out T data,
            out string errorMessage,
            out FailureReason failureReason,
            out Dictionary<string, string[]> validationErrors)
        {
            if (result != null && result.IsSuccess)
            {
                data = result.Data;
                errorMessage = null;
                failureReason = FailureReason.Unknown;
                validationErrors = null;
                return true;
            }

            data = default;

            if (result != null)
            {
                errorMessage = result.ErrorMessage;
                failureReason = result.Reason;
                validationErrors = result.ValidationErrors;
            }
            else
            {
                errorMessage = "Result was null";
                failureReason = FailureReason.Unknown;
                validationErrors = null;
            }

            return false;
        }

        /// <summary>
        /// Implicitly converts a successful value to a ResultWrapper
        /// </summary>
        public static implicit operator ResultWrapper<T>(T value)
        {
            return Success(value);
        }

        /// <summary>
        /// Maps the result to a new type using the provided mapper function
        /// </summary>
        public ResultWrapper<TNew> Map<TNew>(Func<T, TNew> mapper)
        {
            if (!IsSuccess)
            {
                return new ResultWrapper<TNew>
                {
                    IsSuccess = false,
                    ErrorMessage = ErrorMessage,
                    ErrorCode = ErrorCode,
                    Reason = Reason,
                    ValidationErrors = ValidationErrors,
                    DebugInformation = DebugInformation
                };
            }

            try
            {
                var newData = Data != null ? mapper(Data) : default;
                return ResultWrapper<TNew>.Success(newData, DataMessage);
            }
            catch (Exception ex)
            {
                return ResultWrapper<TNew>.FromException(ex);
            }
        }

        /// <summary>
        /// Maps the result to a new type using an async mapper function
        /// </summary>
        public async Task<ResultWrapper<TNew>> MapAsync<TNew>(Func<T, Task<TNew>> asyncMapper)
        {
            if (!IsSuccess)
            {
                return new ResultWrapper<TNew>
                {
                    IsSuccess = false,
                    ErrorMessage = ErrorMessage,
                    ErrorCode = ErrorCode,
                    Reason = Reason,
                    ValidationErrors = ValidationErrors,
                    DebugInformation = DebugInformation
                };
            }

            try
            {
                var newData = Data != null ? await asyncMapper(Data) : default;
                return ResultWrapper<TNew>.Success(newData, DataMessage);
            }
            catch (Exception ex)
            {
                return ResultWrapper<TNew>.FromException(ex);
            }
        }

        /// <summary>
        /// Performs an action if the result is successful
        /// </summary>
        public ResultWrapper<T> OnSuccess(Action<T> action)
        {
            if (IsSuccess && action != null)
            {
                try
                {
                    action(Data);
                }
                catch (Exception ex)
                {
                    return FromException(ex);
                }
            }

            return this;
        }

        /// <summary>
        /// Performs an action if the result is a failure
        /// </summary>
        public ResultWrapper<T> OnFailure(Action<string, FailureReason> action)
        {
            if (!IsSuccess && action != null)
            {
                action(ErrorMessage, Reason);
            }

            return this;
        }

        /// <summary>
        /// Execute a function based on the result success/failure status
        /// </summary>
        public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<string, FailureReason, TResult> onFailure)
        {
            return IsSuccess
                ? onSuccess(Data)
                : onFailure(ErrorMessage, Reason);
        }

        /// <summary>
        /// Convert to a simple success/failure string representation
        /// </summary>
        public override string ToString()
        {
            return IsSuccess
                ? $"Success: {DataMessage ?? "Operation completed successfully"}"
                : $"Failure ({Reason}): {ErrorMessage}";
        }
    }

    /// <summary>
    /// Non-generic version of ResultWrapper for operations that don't return data
    /// </summary>
    public class ResultWrapper : ResultWrapper<object>
    {
        /// <summary>
        /// Creates a successful result with no data
        /// </summary>
        public static new ResultWrapper Success(string message = null)
        {
            return (ResultWrapper)ResultWrapper<object>.Success(null, message);
        }

        /// <summary>
        /// Creates a failure result with detailed error information
        /// </summary>
        public new static ResultWrapper Failure(
            FailureReason reason,
            string errorMessage,
            string errorCode = null,
            Dictionary<string, string[]> validationErrors = null,
            string debugInformation = null)
        {
            return (ResultWrapper)ResultWrapper<object>.Failure(
                reason,
                errorMessage,
                errorCode,
                validationErrors,
                debugInformation);
        }

        /// <summary>
        /// Creates a failure result from an exception
        /// </summary>
        public new static ResultWrapper FromException(Exception exception, bool includeStackTrace = false)
        {
            return (ResultWrapper)ResultWrapper<object>.FromException(exception, includeStackTrace);
        }
    }
}