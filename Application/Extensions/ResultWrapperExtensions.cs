using Domain.DTOs;
using Domain.DTOs.Error;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace Application.Extensions
{
    /// <summary>
    /// Extension methods for working with ResultWrapper in controllers
    /// </summary>
    public static class ResultWrapperExtensions
    {
        /// <summary>
        /// Converts a ResultWrapper to an appropriate IActionResult
        /// </summary>
        public static IActionResult ToActionResult<T>(this ResultWrapper<T> result, ControllerBase controller)
        {
            if (result.IsSuccess)
            {
                if (result.Data == null && string.IsNullOrEmpty(result.DataMessage))
                {
                    return controller.NoContent(); // 204 if no content to return
                }
                else if (result.Data != null)
                {
                    return controller.Ok(result.Data);
                }
                else
                {
                    return controller.Ok(new { message = result.DataMessage });
                }
            }

            // Create error response
            var errorResponse = new ErrorResponse
            {
                Message = result.ErrorMessage,
                Code = result.ErrorCode ?? result.Reason.ToString(),
                ValidationErrors = result.ValidationErrors,
                TraceId = controller.HttpContext.TraceIdentifier
            };

            // Return appropriate status code based on failure reason
            return controller.StatusCode(result.StatusCode, errorResponse);
        }

        /// <summary>
        /// Converts a ResultWrapper to an IActionResult with a custom status code
        /// </summary>
        public static IActionResult ToActionResultWithStatusCode<T>(
            this ResultWrapper<T> result,
            ControllerBase controller,
            int statusCode)
        {
            if (result.IsSuccess)
            {
                if (result.Data == null && string.IsNullOrEmpty(result.DataMessage))
                {
                    return controller.NoContent();
                }
                else if (result.Data != null)
                {
                    return controller.Ok(result.Data);
                }
                else
                {
                    return controller.Ok(new { message = result.DataMessage });
                }
            }

            // Create error response
            var errorResponse = new ErrorResponse
            {
                Message = result.ErrorMessage,
                Code = result.ErrorCode ?? result.Reason.ToString(),
                ValidationErrors = result.ValidationErrors,
                TraceId = controller.HttpContext.TraceIdentifier
            };

            // Use the provided custom status code
            return controller.StatusCode(statusCode, errorResponse);
        }

        /// <summary>
        /// Processes a ResultWrapper and returns an appropriate HTTP response
        /// </summary>
        public static async Task<IActionResult> ProcessResult<T>(
            this Task<ResultWrapper<T>> resultTask,
            ControllerBase controller,
            ILogger logger = null)
        {
            var stopwatch = Stopwatch.StartNew();
            var traceId = Activity.Current?.Id ?? controller.HttpContext.TraceIdentifier;

            try
            {
                var result = await resultTask;

                if (logger != null)
                {
                    if (result.IsSuccess)
                    {
                        logger.LogInformation(
                            "Request {TraceId} processed successfully in {ElapsedMs}ms",
                            traceId, stopwatch.ElapsedMilliseconds);
                    }
                    else
                    {
                        logger.LogWarning(
                            "Request {TraceId} completed with error {ErrorCode}: {ErrorMessage} in {ElapsedMs}ms",
                            traceId, result.ErrorCode, result.ErrorMessage, stopwatch.ElapsedMilliseconds);
                    }
                }

                return result.ToActionResult(controller);
            }
            catch (Exception ex)
            {
                if (logger != null)
                {
                    logger.LogError(ex,
                        "Unhandled exception in request {TraceId} after {ElapsedMs}ms",
                        traceId, stopwatch.ElapsedMilliseconds);
                }

                var errorResult = ResultWrapper<T>.FromException(ex);
                return errorResult.ToActionResult(controller);
            }
        }

        /// <summary>
        /// Enhances a ResultWrapper with additional error details if it's a failure
        /// </summary>
        public static ResultWrapper<T> EnhanceError<T>(
            this ResultWrapper<T> result,
            string additionalContext = null)
        {
            if (result.IsSuccess)
                return result;

            var enhancedMessage = string.IsNullOrEmpty(additionalContext)
                ? result.ErrorMessage
                : $"{additionalContext}: {result.ErrorMessage}";

            return ResultWrapper<T>.Failure(
                result.Reason,
                enhancedMessage,
                result.ErrorCode,
                result.ValidationErrors,
                result.DebugInformation
            );
        }

        /// <summary>
        /// Combines multiple results into a single aggregated result
        /// </summary>
        public static ResultWrapper<IEnumerable<T>> Combine<T>(this IEnumerable<ResultWrapper<T>> results)
        {
            var resultsList = results.ToList();
            var failedResults = resultsList.Where(r => !r.IsSuccess).ToList();

            if (failedResults.Any())
            {
                // Combine error messages from all failed results
                var combinedMessage = string.Join("; ", failedResults.Select(r => r.ErrorMessage));
                var firstFailure = failedResults.First();

                // Combine all validation errors if present
                Dictionary<string, string[]> combinedValidationErrors = null;

                if (failedResults.Any(r => r.ValidationErrors != null && r.ValidationErrors.Count > 0))
                {
                    combinedValidationErrors = new Dictionary<string, string[]>();

                    foreach (var failure in failedResults.Where(r => r.ValidationErrors != null))
                    {
                        foreach (var error in failure.ValidationErrors)
                        {
                            if (combinedValidationErrors.ContainsKey(error.Key))
                            {
                                combinedValidationErrors[error.Key] =
                                    combinedValidationErrors[error.Key]
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

        /// <summary>
        /// Transforms a successful result using the provided function
        /// </summary>
        public static ResultWrapper<TResult> Transform<TSource, TResult>(
            this ResultWrapper<TSource> result,
            Func<TSource, TResult> transform)
        {
            if (!result.IsSuccess)
            {
                return ResultWrapper<TResult>.Failure(
                    result.Reason,
                    result.ErrorMessage,
                    result.ErrorCode,
                    result.ValidationErrors,
                    result.DebugInformation
                );
            }

            try
            {
                var transformedData = result.Data != null
                    ? transform(result.Data)
                    : default;

                return ResultWrapper<TResult>.Success(transformedData, result.DataMessage);
            }
            catch (Exception ex)
            {
                return ResultWrapper<TResult>.Failure(
                    Domain.Constants.FailureReason.Unknown,
                    $"Error during transformation: {ex.Message}",
                    "TRANSFORM_ERROR",
                    null,
                    ex.ToString()
                );
            }
        }

        /// <summary>
        /// Asynchronously transforms a successful result using the provided function
        /// </summary>
        public static async Task<ResultWrapper<TResult>> TransformAsync<TSource, TResult>(
            this ResultWrapper<TSource> result,
            Func<TSource, Task<TResult>> transformAsync)
        {
            if (!result.IsSuccess)
            {
                return ResultWrapper<TResult>.Failure(
                    result.Reason,
                    result.ErrorMessage,
                    result.ErrorCode,
                    result.ValidationErrors,
                    result.DebugInformation
                );
            }

            try
            {
                var transformedData = result.Data != null
                    ? await transformAsync(result.Data)
                    : default;

                return ResultWrapper<TResult>.Success(transformedData, result.DataMessage);
            }
            catch (Exception ex)
            {
                return ResultWrapper<TResult>.Failure(
                    Domain.Constants.FailureReason.Unknown,
                    $"Error during asynchronous transformation: {ex.Message}",
                    "ASYNC_TRANSFORM_ERROR",
                    null,
                    ex.ToString()
                );
            }
        }

        /// <summary>
        /// Serializes a ResultWrapper to JSON
        /// </summary>
        public static string ToJson<T>(this ResultWrapper<T> result)
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };

            return JsonSerializer.Serialize(result, options);
        }

        /// <summary>
        /// Executes an action only if the result is successful
        /// </summary>
        public static ResultWrapper<T> OnSuccess<T>(
            this ResultWrapper<T> result,
            Action<T> action)
        {
            if (result.IsSuccess && action != null)
            {
                try
                {
                    action(result.Data);
                }
                catch (Exception ex)
                {
                    return ResultWrapper<T>.Failure(
                        Domain.Constants.FailureReason.Unknown,
                        $"Error in OnSuccess callback: {ex.Message}",
                        "CALLBACK_ERROR",
                        null,
                        ex.ToString());
                }
            }

            return result;
        }

        /// <summary>
        /// Executes an asynchronous action only if the result is successful
        /// </summary>
        public static async Task<ResultWrapper<T>> OnSuccessAsync<T>(
            this ResultWrapper<T> result,
            Func<T, Task> actionAsync)
        {
            if (result.IsSuccess && actionAsync != null)
            {
                try
                {
                    await actionAsync(result.Data);
                }
                catch (Exception ex)
                {
                    return ResultWrapper<T>.Failure(
                        Domain.Constants.FailureReason.Unknown,
                        $"Error in OnSuccessAsync callback: {ex.Message}",
                        "ASYNC_CALLBACK_ERROR",
                        null,
                        ex.ToString());
                }
            }

            return result;
        }
    }
}