using Domain.Constants;
using Domain.DTOs.Error;
using Domain.Exceptions;
using Microsoft.AspNetCore.Antiforgery;
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
        private readonly IWebHostEnvironment _environment;

        public GlobalExceptionHandlingMiddleware(
            RequestDelegate next,
            ILogger<GlobalExceptionHandlingMiddleware> logger,
            IWebHostEnvironment environment)
        {
            _next = next;
            _logger = logger;
            _environment = environment;
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
            // Get request details for logging
            var request = context.Request;
            var correlationId = Activity.Current?.Id ?? context.TraceIdentifier;

            // Log the exception with enhanced context
            _logger.LogError(exception,
                "Unhandled exception occurred. Method: {Method}, Path: {Path}, QueryString: {QueryString}, CorrelationId: {CorrelationId}",
                request.Method, request.Path, request.QueryString, correlationId);

            // Create error response object
            var errorResponse = new ErrorResponse
            {
                TraceId = correlationId
            };

            // Determine failure reason and status code
            var failureReason = FailureReasonExtensions.FromException(exception);
            context.Response.StatusCode = failureReason.ToStatusCode();

            // Handle antiforgery token validation failures specifically
            if (exception is AntiforgeryValidationException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                errorResponse.Code = "INVALID_ANTIFORGERY_TOKEN";
                errorResponse.Message = "Invalid antiforgery token or token missing from request.";

                // Add detailed error info in development
                if (_environment.IsDevelopment())
                {
                    errorResponse.Message += " " + exception.Message;
                }
            }
            else
            {
                // Handle different exception types
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
                        case PaymentApiException paymentEx:
                            _logger.LogError("Payment processing failed. Provider: {Provider}, PaymentId: {PaymentId}",
                                paymentEx.PaymentProvider, paymentEx.PaymentId ?? "Unknown");
                            break;
                    }
                }
                else
                {
                    // Generic exception handling
                    errorResponse.Code = failureReason.ToString();

                    // Only return detailed errors in development
                    errorResponse.Message = _environment.IsDevelopment()
                        ? exception.Message
                        : "An unexpected error occurred. Please try again later.";
                }
            }

            // Set response content type
            context.Response.ContentType = "application/json";

            // Serialize and return the error response
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