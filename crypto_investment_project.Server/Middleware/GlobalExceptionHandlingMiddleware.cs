using Domain.Constants;
using Domain.DTOs.Error;
using Domain.Exceptions;
using System.Diagnostics;
using System.Text.Json;

namespace crypto_investment_project.Server.Middleware
{
    /// <summary>
    /// Middleware for global exception handling
    /// </summary>
    public class GlobalExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger;

        public GlobalExceptionHandlingMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            _logger.LogError(exception, "Unhandled exception occurred");

            var errorResponse = new ErrorResponse
            {
                TraceId = Activity.Current?.Id ?? context.TraceIdentifier
            };

            var failureReason = FailureReasonExtensions.FromException(exception);
            context.Response.StatusCode = failureReason.ToStatusCode();

            if (exception is DomainException domainException)
            {
                errorResponse.Code = domainException.ErrorCode;
                errorResponse.Message = domainException.Message;

                // Add validation errors if present
                if (domainException is ValidationException validationException)
                {
                    errorResponse.ValidationErrors = validationException.ValidationErrors;
                }

                // For specific domain exceptions, add additional logging context
                switch (domainException)
                {
                    case OrderExecutionException orderEx:
                        _logger.LogError("Order execution failed. Exchange: {Exchange}, OrderId: {OrderId}",
                            orderEx.Exchange, orderEx.OrderId ?? "Unknown");
                        break;
                    case PaymentProcessingException paymentEx:
                        _logger.LogError("Payment processing failed. Provider: {Provider}, PaymentId: {PaymentId}",
                            paymentEx.PaymentProvider, paymentEx.PaymentId ?? "Unknown");
                        break;
                }
            }
            else
            {
                // Generic exception handling
                errorResponse.Code = failureReason.ToString();

                // Sanitize messages for non-domain exceptions in production
                errorResponse.Message = context.Request.Host.Host.Contains("localhost") ||
                                       context.Request.Host.Host.Contains("127.0.0.1")
                    ? exception.Message
                    : "An unexpected error occurred. Please try again later.";
            }

            context.Response.ContentType = "application/json";

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };

            var jsonResponse = JsonSerializer.Serialize(errorResponse, jsonOptions);
            await context.Response.WriteAsync(jsonResponse);
        }
    }

    // Extension method to add the middleware to the HTTP request pipeline
    public static class GlobalExceptionHandlingMiddlewareExtensions
    {
        public static IApplicationBuilder UseGlobalExceptionHandling(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<GlobalExceptionHandlingMiddleware>();
        }
    }
}