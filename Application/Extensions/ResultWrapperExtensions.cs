using Domain.DTOs;
using Domain.DTOs.Error;
using Microsoft.AspNetCore.Mvc;
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
                return result.Data != null
                    ? controller.Ok(result.Data)
                    : controller.Ok();
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
        /// Processes a ResultWrapper and returns an appropriate HTTP response
        /// </summary>
        public static async Task<IActionResult> ProcessResult<T>(
            this Task<ResultWrapper<T>> resultTask,
            ControllerBase controller)
        {
            var result = await resultTask;
            return result.ToActionResult(controller);
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

                return ResultWrapper<IEnumerable<T>>.Failure(
                    firstFailure.Reason,
                    combinedMessage,
                    firstFailure.ErrorCode,
                    firstFailure.ValidationErrors,
                    firstFailure.DebugInformation
                );
            }

            // All results were successful
            return ResultWrapper<IEnumerable<T>>.Success(resultsList.Select(r => r.Data));
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
    }
}