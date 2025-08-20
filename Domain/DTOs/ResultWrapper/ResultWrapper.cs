using Domain.Constants;
using Domain.Exceptions;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace Domain.DTOs
{
    /// <summary>
    /// Enhanced wrapper for operation results with improved error handling, performance, and utility methods.
    /// </summary>
    /// <typeparam name="T">The type of data returned by the operation</typeparam>
    public class ResultWrapper<T>
    {
        // Static cache for empty validation errors to reduce allocations
        protected static readonly Dictionary<string, string[]> EmptyValidationErrors = new();

        // Thread-safe cache for common result types
        private static readonly ConcurrentDictionary<string, ResultWrapper<T>> CommonResults = new();

        /// <summary>
        /// Indicates whether the operation was successful
        /// </summary>
        public bool IsSuccess { get; protected set; }

        /// <summary>
        /// The data returned by the operation (if successful)
        /// </summary>
        public T Data { get; protected set; }

        /// <summary>
        /// Optional success message for the operation
        /// </summary>
        public string DataMessage { get; protected set; }

        /// <summary>
        /// Human-readable error message (if operation failed)
        /// </summary>
        public string ErrorMessage { get; protected set; }

        /// <summary>
        /// Machine-readable error code (if operation failed)
        /// </summary>
        public string ErrorCode { get; protected set; }

        /// <summary>
        /// Categorized failure reason (if operation failed)
        /// </summary>
        public FailureReason Reason { get; protected set; }

        /// <summary>
        /// Detailed validation errors (if applicable)
        /// </summary>
        public Dictionary<string, string[]> ValidationErrors { get; protected set; }

        /// <summary>
        /// Technical details for debugging (not exposed to end users)
        /// </summary>
        public string DebugInformation { get; protected set; }

        /// <summary>
        /// A correlation ID for tracing the request through the system
        /// </summary>
        public string CorrelationId { get; protected set; }

        /// <summary>
        /// HTTP status code associated with this result
        /// </summary>
        public int StatusCode => IsSuccess ? 200 : Reason.ToStatusCode();

        /// <summary>
        /// Timestamp when this result was created
        /// </summary>
        public DateTime Timestamp { get; protected set; }

        // Protected constructor to enforce factory methods
        protected ResultWrapper()
        {
            Timestamp = DateTime.UtcNow;
            CorrelationId = Activity.Current?.Id ?? Guid.NewGuid().ToString();
        }

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
        /// Gets a cached instance of an empty successful result
        /// </summary>
        public static ResultWrapper<T> SuccessEmpty =>
            CommonResults.GetOrAdd("empty_success", _ => Success(default, null));

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
                ValidationErrors = validationErrors ?? EmptyValidationErrors,
                DebugInformation = debugInformation
            };
        }

        /// <summary>
        /// Creates a failure result from an exception with optional stack trace inclusion
        /// </summary>
        public static ResultWrapper<T> FromException(Exception exception, bool includeStackTrace = false)
        {
            var reason = FailureReasonExtensions.FromException(exception);
            string errorCode = null;
            Dictionary<string, string[]> validationErrors = null;

            // Capture inner exception details to provide more context
            string errorMessage = exception.Message;
            if (exception.InnerException != null)
            {
                errorMessage = $"{errorMessage} → {exception.InnerException.Message}";
            }

            string debugInfo = includeStackTrace ? exception.StackTrace : null;

            // Extract domain-specific error information when available
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
                errorMessage,
                errorCode,
                validationErrors,
                debugInfo
            );
        }

        /// <summary>
        /// Creates a not found result with a standardized message
        /// </summary>
        public static ResultWrapper<T> NotFound(string entityName, string id = null)
        {
            string message = string.IsNullOrEmpty(id)
                ? $"{entityName} not found"
                : $"{entityName} with id '{id}' not found";

            return Failure(
                FailureReason.NotFound,
                message,
                "RESOURCE_NOT_FOUND"
            );
        }

        /// <summary>
        /// Creates an unauthorized result with a standardized message
        /// </summary>
        public static ResultWrapper<T> Unauthorized(string message = "You are not authorized to perform this action")
        {
            return Failure(
                FailureReason.Unauthorized,
                message,
                "UNAUTHORIZED_ACCESS"
            );
        }

        /// <summary>
        /// Creates a validation error result with a standardized message and validation details
        /// </summary>
        public static ResultWrapper<T> ValidationError(
            Dictionary<string, string[]> errors,
            string message = "Validation failed")
        {
            return Failure(
                FailureReason.ValidationError,
                message,
                "VALIDATION_ERROR",
                errors
            );
        }

        /// <summary>
        /// Creates an internal server error result with a standardized message
        /// </summary>
        public static ResultWrapper<T> InternalServerError(string message = "An error occured while processing your request")
        {
            return new ResultWrapper<T>
            {
                IsSuccess = false,
                ErrorMessage = message,
                ErrorCode = "INTERNAL_SERVER_ERROR",
                Reason = FailureReason.Unknown
            };
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
                    DebugInformation = DebugInformation,
                    CorrelationId = CorrelationId
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
                    DebugInformation = DebugInformation,
                    CorrelationId = CorrelationId
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
        /// Transforms the result to a new type using provided success and failure mappers
        /// </summary>
        public ResultWrapper<TNew> MapBoth<TNew>(
            Func<T, TNew> successMapper,
            Func<ResultWrapper<T>, ResultWrapper<TNew>> failureMapper)
        {
            return IsSuccess
                ? Map(successMapper)
                : failureMapper(this);
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
        /// Performs an async action if the result is successful
        /// </summary>
        public async Task<ResultWrapper<T>> OnSuccessAsync(Func<T, Task> asyncAction)
        {
            if (IsSuccess && asyncAction != null)
            {
                try
                {
                    await asyncAction(Data);
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
        /// Performs an async action if the result is a failure
        /// </summary>
        public async Task<ResultWrapper<T>> OnFailureAsync(Func<string, FailureReason, Task> asyncAction)
        {
            if (!IsSuccess && asyncAction != null)
            {
                await asyncAction(ErrorMessage, Reason);
            }

            return this;
        }

        /// <summary>
        /// Execute a function based on the result success/failure status
        /// </summary>
        public TResult Match<TResult>(
            Func<T, TResult> onSuccess,
            Func<string, FailureReason, TResult> onFailure)
        {
            return IsSuccess
                ? onSuccess(Data)
                : onFailure(ErrorMessage, Reason);
        }

        /// <summary>
        /// Execute an async function based on the result success/failure status
        /// </summary>
        public async Task<TResult> MatchAsync<TResult>(
            Func<T, Task<TResult>> onSuccessAsync,
            Func<string, FailureReason, Task<TResult>> onFailureAsync)
        {
            return IsSuccess
                ? await onSuccessAsync(Data)
                : await onFailureAsync(ErrorMessage, Reason);
        }

        /// <summary>
        /// Ensures that a condition is met, or returns a failure result
        /// </summary>
        public ResultWrapper<T> Ensure(
            Func<T, bool> predicate,
            string errorMessage,
            FailureReason reason = FailureReason.ValidationError)
        {
            if (!IsSuccess)
                return this;

            return predicate(Data)
                ? this
                : Failure(reason, errorMessage);
        }

        /// <summary>
        /// Combines this result with another, succeeding only if both succeed
        /// </summary>
        public ResultWrapper<(T First, TOther Second)> Combine<TOther>(ResultWrapper<TOther> other)
        {
            if (!IsSuccess)
                return ResultWrapper<(T, TOther)>.Failure(Reason, ErrorMessage, ErrorCode, ValidationErrors, DebugInformation);

            if (!other.IsSuccess)
                return ResultWrapper<(T, TOther)>.Failure(other.Reason, other.ErrorMessage, other.ErrorCode, other.ValidationErrors, other.DebugInformation);

            return ResultWrapper<(T, TOther)>.Success((Data, other.Data));
        }

        /// <summary>
        /// Serializes the result to JSON with sensitive data handling options
        /// </summary>
        public string ToJson(bool includeDebugInfo = false, bool indented = true)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = indented
            };

            // Create a clean version for serialization if needed
            if (!includeDebugInfo)
            {
                var cleanResult = new
                {
                    IsSuccess,
                    Data,
                    DataMessage,
                    ErrorMessage,
                    ErrorCode,
                    Reason,
                    ValidationErrors,
                    StatusCode,
                    CorrelationId,
                    Timestamp
                };

                return JsonSerializer.Serialize(cleanResult, options);
            }

            return JsonSerializer.Serialize(this, options);
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

        /// <summary>
        /// Aggregates multiple results into a single result with a collection of values
        /// </summary>
        public static ResultWrapper<IEnumerable<T>> Aggregate(IEnumerable<ResultWrapper<T>> results)
        {
            var resultsList = results.ToList();
            var failedResults = resultsList.Where(r => !r.IsSuccess).ToList();

            if (failedResults.Any())
            {
                var firstFailure = failedResults.First();

                // Combine validation errors if present
                Dictionary<string, string[]> combinedValidationErrors = null;
                if (failedResults.Any(r => r.ValidationErrors != null && r.ValidationErrors.Any()))
                {
                    combinedValidationErrors = new Dictionary<string, string[]>();
                    foreach (var result in failedResults.Where(r => r.ValidationErrors != null))
                    {
                        foreach (var error in result.ValidationErrors)
                        {
                            if (combinedValidationErrors.ContainsKey(error.Key))
                            {
                                // Combine error messages for the same property
                                combinedValidationErrors[error.Key] = combinedValidationErrors[error.Key]
                                    .Concat(error.Value)
                                    .Distinct()
                                    .ToArray();
                            }
                            else
                            {
                                combinedValidationErrors[error.Key] = error.Value;
                            }
                        }
                    }
                }

                // Combine error messages
                var combinedMessage = string.Join("; ", failedResults.Select(r => r.ErrorMessage));

                return ResultWrapper<IEnumerable<T>>.Failure(
                    firstFailure.Reason,
                    combinedMessage,
                    firstFailure.ErrorCode,
                    combinedValidationErrors,
                    firstFailure.DebugInformation
                );
            }

            // All results were successful
            return ResultWrapper<IEnumerable<T>>.Success(resultsList.Select(r => r.Data));
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
            return new ResultWrapper
            {
                IsSuccess = true,
                DataMessage = message
            };
        }

        /// <summary>
        /// Creates a failure result with detailed error information
        /// </summary>
        public static new ResultWrapper Failure(
            FailureReason reason,
            string errorMessage,
            string errorCode = null,
            Dictionary<string, string[]> validationErrors = null,
            string debugInformation = null)
        {
            return new ResultWrapper
            {
                IsSuccess = false,
                ErrorMessage = errorMessage,
                ErrorCode = errorCode ?? reason.ToString(),
                Reason = reason,
                ValidationErrors = validationErrors ?? EmptyValidationErrors,
                DebugInformation = debugInformation
            };
        }

        /// <summary>
        /// Creates a failure result from an exception
        /// </summary>
        public static new ResultWrapper FromException(Exception exception, bool includeStackTrace = false)
        {
            var reason = FailureReasonExtensions.FromException(exception);
            string errorCode = null;
            Dictionary<string, string[]> validationErrors = null;

            // Capture inner exception details to provide more context
            string errorMessage = exception.Message;
            if (exception.InnerException != null)
            {
                errorMessage = $"{errorMessage} → {exception.InnerException.Message}";
            }

            string debugInfo = includeStackTrace ? exception.StackTrace : null;

            // Extract domain-specific error information when available
            if (exception is DomainException domainEx)
            {
                errorCode = domainEx.ErrorCode;

                if (exception is ValidationException validationEx)
                {
                    validationErrors = validationEx.ValidationErrors;
                }
            }

            return new ResultWrapper
            {
                IsSuccess = false,
                ErrorMessage = errorMessage,
                ErrorCode = errorCode ?? reason.ToString(),
                Reason = reason,
                ValidationErrors = validationErrors ?? EmptyValidationErrors,
                DebugInformation = debugInfo,
                CorrelationId = Activity.Current?.Id ?? Guid.NewGuid().ToString()
            };
        }

        /// <summary>
        /// Creates a not found result with a standardized message
        /// </summary>
        public static new ResultWrapper NotFound(string entityName, string id = null)
        {
            string message = string.IsNullOrEmpty(id)
                ? $"{entityName} not found"
                : $"{entityName} with id '{id}' not found";

            return new ResultWrapper
            {
                IsSuccess = false,
                ErrorMessage = message,
                ErrorCode = "RESOURCE_NOT_FOUND",
                Reason = FailureReason.NotFound
            };
        }

        /// <summary>
        /// Creates an unauthorized result with a standardized message
        /// </summary>
        public static new ResultWrapper Unauthorized(string message = "You are not authorized to perform this action")
        {
            return new ResultWrapper
            {
                IsSuccess = false,
                ErrorMessage = message,
                ErrorCode = "UNAUTHORIZED_ACCESS",
                Reason = FailureReason.Unauthorized
            };
        }

        /// <summary>
        /// Creates a validation error result with a standardized message and validation details
        /// </summary>
        public static new ResultWrapper ValidationError(
            Dictionary<string, string[]> errors,
            string message = "Validation failed")
        {
            return new ResultWrapper
            {
                IsSuccess = false,
                ErrorMessage = message,
                ErrorCode = "VALIDATION_ERROR",
                Reason = FailureReason.ValidationError,
                ValidationErrors = errors ?? EmptyValidationErrors
            };
        }

        /// <summary>
        /// Creates an internal server error result with a standardized message
        /// </summary>
        public static new ResultWrapper InternalServerError(string message = "An error occured while processing your request")
        {
            return new ResultWrapper
            {
                IsSuccess = false,
                ErrorMessage = message,
                ErrorCode = "INTERNAL_SERVER_ERROR",
                Reason = FailureReason.Unknown
            };
        }
    }
}