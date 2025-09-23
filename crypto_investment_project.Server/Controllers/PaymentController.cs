using Application.Contracts.Responses.Payment;
using Application.Extensions;
using Application.Interfaces;
using Application.Interfaces.Logging;
using Application.Interfaces.Payment;
using Application.Interfaces.Subscription;
using Domain.Constants;
using Domain.Constants.Payment;
using Domain.DTOs;
using Domain.DTOs.Payment;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Diagnostics;
using System.Security.Claims;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [Produces("application/json")]
    public class PaymentController : ControllerBase
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly IPaymentService _paymentService;
        private readonly IIdempotencyService _idempotencyService;
        private readonly IConfiguration _configuration;
        private readonly ILoggingService _logger;
        private readonly IValidator<CheckoutSessionRequest> _checkoutSessionValidator;

        public PaymentController(
            ISubscriptionService subscriptionService,
            IPaymentService paymentService,
            IIdempotencyService idempotencyService,
            IConfiguration configuration,
            ILoggingService logger,
            IValidator<CheckoutSessionRequest> checkoutSessionValidator)
        {
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _checkoutSessionValidator = checkoutSessionValidator ?? throw new ArgumentNullException(nameof(checkoutSessionValidator));
        }

        /// <summary>
        /// Creates a Stripe checkout session for subscription payment
        /// </summary>
        /// <param name="request">Checkout session request</param>
        /// <returns>Checkout session URL</returns>
        /// <response code="200">Returns the checkout session details</response>
        /// <response code="400">If the request is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="429">If too many requests are made</response>
        /// <response code="500">If an internal error occurs</response>
        [HttpPost("create-checkout-session")]
        [EnableRateLimiting("standard")]
        [ProducesResponseType(typeof(CheckoutSessionResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CreateCheckoutSession([FromBody] CheckoutSessionRequest request)
        {
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["Operation"] = "CreateCheckoutSession",
                ["SubscriptionId"] = request?.SubscriptionId,
                ["CorrelationId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            }))
            {
                try
                {
                    await _logger.LogTraceAsync($"Payment request received: {request}");

                    // Validate request using FluentValidation
                    if (request == null)
                    {
                        return ResultWrapper.Failure(FailureReason.ValidationError,
                            "Request body is required",
                            "INVALID_REQUEST")
                            .ToActionResult(this);
                    }

                    var validationResult = await _checkoutSessionValidator.ValidateAsync(request);
                    if (!validationResult.IsValid)
                    {
                        var errors = validationResult.Errors
                            .GroupBy(e => e.PropertyName)
                            .ToDictionary(
                                g => g.Key,
                                g => g.Select(e => e.ErrorMessage).ToArray()
                            );

                        return ResultWrapper.ValidationError(errors)
                            .ToActionResult(this);
                    }

                    var subscriptionId = Guid.Parse(request.SubscriptionId);

                    // Check if subscription exists and belongs to the user
                    var subscriptionResult = await _subscriptionService.GetByIdAsync(subscriptionId);
                    if (subscriptionResult == null || !subscriptionResult.IsSuccess || subscriptionResult.Data == null)
                    {
                        await _logger.LogTraceAsync($"Subscription not found: {subscriptionId}");

                        return ResultWrapper.NotFound("Subscription", subscriptionId.ToString())
                            .ToActionResult(this);
                    }

                    var subscription = subscriptionResult.Data;

                    if (subscription.UserId.ToString() != request.UserId)
                    {
                        _logger.LogWarning("Unauthorized access attempt to subscription {SubscriptionId} by user {UserId}",
                            subscriptionId, request.UserId);

                        return ResultWrapper.Unauthorized()
                            .ToActionResult(this);
                    }

                    // Check for idempotency
                    string idempotencyKey = request.IdempotencyKey ?? Guid.NewGuid().ToString();

                    var (hasExisting, existingResult) = await _idempotencyService.GetResultAsync<CheckoutSessionResponse>(idempotencyKey);

                    if (hasExisting)
                    {
                        await _logger.LogTraceAsync($"Returning cached checkout session for idempotency key {idempotencyKey}");

                        return ResultWrapper.Success(existingResult, "Checkout session already processed")
                            .ToActionResult(this);
                    }

                    // Set default URLs if not provided
                    var appBaseUrl = _configuration["PaymentService:BaseUrl"] ?? "https://localhost:7144";
                    string returnUrl = request.ReturnUrl ?? $"{appBaseUrl}/payment/success";
                    string cancelUrl = request.CancelUrl ?? $"{appBaseUrl}/payment/cancel";

                    // Create Stripe checkout session
                    var sessionResult = await _paymentService.CreateCheckoutSessionAsync(new CreateCheckoutSessionDto
                    {
                        SubscriptionId = subscriptionId.ToString(),
                        UserId = request.UserId,
                        UserEmail = User.Identity?.Name,
                        Amount = request.Amount,
                        Currency = request.Currency ?? "USD",
                        IsRecurring = request.IsRecurring,
                        Interval = request.Interval,
                        ReturnUrl = returnUrl,
                        CancelUrl = cancelUrl,
                        Metadata = new Dictionary<string, string>
                        {
                            ["correlationId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                            ["userId"] = request.UserId,
                            ["subscriptionId"] = subscriptionId.ToString()
                        }
                    });

                    if (!sessionResult.IsSuccess || sessionResult.Data == null)
                    {
                        return ResultWrapper.Failure(sessionResult.Reason,
                            sessionResult.ErrorMessage ?? "Failed to create checkout session",
                            "CHECKOUT_SESSION_FAILED")
                            .ToActionResult(this);
                    }

                    var checkoutSession = sessionResult.Data;

                    // Create response
                    var response = new CheckoutSessionResponse
                    {
                        CheckoutUrl = checkoutSession.Url,
                        ClientSecret = checkoutSession.ClientSecret,
                        SessionId = checkoutSession.Id
                    };

                    // Store response for idempotency
                    await _idempotencyService.StoreResultAsync(idempotencyKey, response, TimeSpan.FromHours(1));

                    await _logger.LogTraceAsync($"Checkout session created successfully for subscription {subscriptionId}");

                    return ResultWrapper.Success(response, "Checkout session created successfully")
                        .ToActionResult(this);
                }
                catch (Exception ex)
                {
                    await _logger.LogTraceAsync($"Error creating checkout session: {ex.Message}",
                        requiresResolution: true,
                        level: Domain.Constants.Logging.LogLevel.Error);

                    return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
                }
            }
        }

        /// <summary>
        /// Cancels a pending payment
        /// </summary>
        /// <param name="paymentId">Payment ID</param>
        /// <returns>Cancellation result</returns>
        /// <response code="200">Returns the cancellation result</response>
        /// <response code="400">If the request is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="403">If the user is not authorized to cancel this payment</response>
        /// <response code="404">If the payment is not found</response>
        /// <response code="500">If an internal error occurs</response>
        [HttpPost("cancel/{paymentId}")]
        [EnableRateLimiting("standard")]
        [ProducesResponseType(typeof(PaymentCancelResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CancelPayment(string paymentId)
        {
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["Operation"] = "CancelPayment",
                ["PaymentId"] = paymentId,
                ["CorrelationId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            }))
            {
                try
                {
                    if (string.IsNullOrEmpty(paymentId))
                    {
                        return ResultWrapper.Failure(FailureReason.ValidationError,
                            "Payment ID is required",
                            "VALIDATION_ERROR")
                            .ToActionResult(this);
                    }

                    // Get the payment to check if it belongs to the current user
                    var paymentResult = await _paymentService.GetPaymentDetailsAsync(paymentId);

                    if (!paymentResult.IsSuccess || paymentResult.Data == null)
                    {
                        _logger.LogWarning("Payment not found: {PaymentId}", paymentId);

                        return ResultWrapper.NotFound("Payment", paymentId)
                            .ToActionResult(this);
                    }

                    var payment = paymentResult.Data;

                    // Ensure the payment belongs to the current user
                    string? currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                                         User.FindFirst("sub")?.Value ??
                                         User.FindFirst("uid")?.Value;

                    if (string.IsNullOrEmpty(currentUserId))
                    {
                        return ResultWrapper.Unauthorized()
                            .ToActionResult(this);
                    }

                    if (payment.UserId != currentUserId)
                    {
                        _logger.LogWarning("Unauthorized cancellation attempt for payment {PaymentId} by user {UserId}",
                            paymentId, currentUserId);

                        return ResultWrapper.Unauthorized("You are not authorized to cancel this payment")
                            .ToActionResult(this);
                    }

                    // Only allow cancellation of pending payments
                    if (payment.Status != PaymentStatus.Pending)
                    {
                        return ResultWrapper.Failure(FailureReason.ValidationError,
                            "Only pending payments can be cancelled",
                            "INVALID_PAYMENT_STATUS")
                            .ToActionResult(this);
                    }

                    // Cancel the payment
                    var result = await _paymentService.CancelPaymentAsync(paymentId);
                    if (!result.IsSuccess)
                    {
                        return ResultWrapper.Failure(result.Reason,
                            result.ErrorMessage ?? "Failed to cancel payment",
                            "CANCELLATION_FAILED")
                            .ToActionResult(this);
                    }

                    await _logger.LogTraceAsync($"Payment cancelled successfully: {paymentId}");

                    var response = new PaymentCancelResponse
                    {
                        PaymentId = paymentId,
                        Status = PaymentStatus.Cancelled,
                        CancelledAt = DateTime.UtcNow
                    };

                    return ResultWrapper.Success(response, "Payment cancelled successfully")
                        .ToActionResult(this);
                }
                catch (Exception ex)
                {
                    await _logger.LogTraceAsync($"Error cancelling payment {paymentId}: {ex.Message}",
                        requiresResolution: true,
                        level: Domain.Constants.Logging.LogLevel.Error);

                    return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
                }
            }
        }

        /// <summary>
        /// Manually retry a failed payment
        /// </summary>
        /// <param name="paymentId">ID of the failed payment to retry</param>
        /// <returns>Success or error result</returns>
        [HttpPost("retry/{paymentId}")]
        [Authorize]
        public async Task<IActionResult> RetryPayment(string paymentId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(paymentId) || !Guid.TryParse(paymentId, out var parsedPaymentId))
                {
                    return ResultWrapper.ValidationError(new()
                    {
                        ["paymentId"] = ["Invalid payment ID format"]
                    }).ToActionResult(this);
                }

                // Verify that the payment belongs to the user
                var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(currentUserId) || !Guid.TryParse(currentUserId, out var userId) || userId == Guid.Empty)
                {
                    return ResultWrapper.Unauthorized()
                        .ToActionResult(this);
                }

                var paymentResult = await _paymentService.GetByIdAsync(parsedPaymentId);

                if (paymentResult == null || !paymentResult.IsSuccess || paymentResult.Data == null)
                    return ResultWrapper.NotFound("Payment", paymentId)
                        .ToActionResult(this);

                var payment = paymentResult.Data;

                if (payment.UserId != userId)
                {
                    return ResultWrapper.Unauthorized()
                        .ToActionResult(this);
                }

                var retryResult = await _paymentService.RetryPaymentAsync(parsedPaymentId);
                if (!retryResult.IsSuccess)
                {
                    return ResultWrapper.Failure(retryResult.Reason,
                        retryResult.ErrorMessage,
                        "RETRY_PAYMENT_ERROR")
                        .ToActionResult(this);
                }

                return ResultWrapper.Success("Payment retry initiated successfully")
                    .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error retrying payment {paymentId}: {ex.Message}");

                return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
            }
        }

        /// <summary>
        /// Gets payment history for a subscription
        /// </summary>
        /// <param name="subscriptionId">The subscription ID</param>
        /// <returns>List of payments for the subscription</returns>
        /// <response code="200">Returns the payment history</response>
        /// <response code="400">If the subscription ID is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="403">If the user is not authorized to view this subscription's payments</response>
        /// <response code="404">If the subscription is not found</response>
        /// <response code="500">If an internal error occurs</response>
        [HttpGet("subscription/{subscriptionId}")]
        [ProducesResponseType(typeof(IEnumerable<PaymentDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetSubscriptionPayments(string subscriptionId)
        {
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["Operation"] = "GetSubscriptionPayments",
                ["SubscriptionId"] = subscriptionId,
                ["CorrelationId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            }))
            {
                try
                {
                    if (!Guid.TryParse(subscriptionId, out var parsedSubscriptionId))
                    {
                        return ResultWrapper.Failure(FailureReason.ValidationError,
                            "Invalid subscription ID format",
                            "INVALID_SUBSCRIPTION_ID")
                            .ToActionResult(this);
                    }

                    // Get the current user ID from claims
                    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    if (string.IsNullOrEmpty(userId))
                    {
                        return ResultWrapper.Failure(FailureReason.Unauthorized,
                            "User ID not found in claims",
                            "UNAUTHORIZED")
                            .ToActionResult(this);
                    }

                    // Verify that the subscription belongs to the user
                    var subscription = await _subscriptionService.GetByIdAsync(parsedSubscriptionId);
                    if (!subscription.IsSuccess || subscription.Data == null)
                    {
                        return ResultWrapper.NotFound("Subscription", subscriptionId)
                            .ToActionResult(this);
                    }

                    if (subscription.Data.UserId.ToString() != userId && !User.IsInRole("ADMIN"))
                    {
                        _logger.LogWarning("Unauthorized access attempt to subscription {SubscriptionId} payments by user {UserId}",
                            subscriptionId, userId);
                        return ResultWrapper.Failure(FailureReason.Unauthorized,
                            "You don't have permission to view this subscription's payments",
                            "UNAUTHORIZED_ACCESS")
                            .ToActionResult(this);
                    }

                    // Get payments for the subscription
                    var paymentsResult = await _paymentService.GetPaymentsForSubscriptionAsync(parsedSubscriptionId);
                    if (!paymentsResult.IsSuccess)
                    {
                        return ResultWrapper.Failure(FailureReason.Unknown,
                            paymentsResult.ErrorMessage ?? "Failed to retrieve payments",
                            "FETCH_PAYMENTS_FAILED")
                            .ToActionResult(this);
                    }

                    var payments = paymentsResult.Data?.Select(p => new PaymentDto(p)) ?? Enumerable.Empty<PaymentDto>();

                    await _logger.LogTraceAsync($"Retrieved {payments.Count()} payments for subscription {subscriptionId}");

                    return ResultWrapper.Success(payments, "Payments retrieved successfully")
                        .ToActionResult(this);
                }
                catch (Exception ex)
                {
                    await _logger.LogTraceAsync($"Error getting payments for subscription {subscriptionId}: {ex.Message}",
                        requiresResolution: true,
                        level: Domain.Constants.Logging.LogLevel.Error);
                    return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
                }
            }
        }

        /// <summary>
        /// Gets the latest payment status data for a subscription
        /// </summary>
        /// <param name="subscriptionId">The subscription ID</param>
        /// <returns>Details of the latest payment for the subscription</returns>
        /// <response code="200">Returns the latest payment details</response>
        /// <response code="400">If the subscription ID is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="403">If the user is not authorized to view this subscription's payments</response>
        /// <response code="404">If the subscription or payment is not found</response>
        /// <response code="500">If an internal error occurs</response>
        [HttpGet("status/subscription/{subscriptionId}")]
        [ProducesResponseType(typeof(PaymentDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetSubscriptionPaymentStatus(string subscriptionId)
        {
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["Operation"] = "GetSubscriptionPaymentStatus",
                ["SubscriptionId"] = subscriptionId,
                ["CorrelationId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            }))
            {
                try
                {
                    if (!Guid.TryParse(subscriptionId, out var parsedSubscriptionId))
                    {
                        return ResultWrapper.ValidationError(new(){
                            ["subscriptionId"] = [ "Invalid subscription ID format"]
                        })
                            .ToActionResult(this);
                    }

                    // Get the current user ID from claims
                    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    if (string.IsNullOrEmpty(userId))
                    {
                        return ResultWrapper.Unauthorized()
                            .ToActionResult(this);
                    }

                    // Verify that the subscription belongs to the user
                    var subscription = await _subscriptionService.GetByIdAsync(parsedSubscriptionId);
                    if (!subscription.IsSuccess || subscription.Data == null)
                    {
                        return ResultWrapper.NotFound("Subscription", subscriptionId)
                            .ToActionResult(this);
                    }

                    if (subscription.Data.UserId.ToString() != userId && !User.IsInRole("ADMIN"))
                    {
                        _logger.LogWarning("Unauthorized access attempt to subscription {SubscriptionId} payment status by user {UserId}",
                            subscriptionId, userId);

                        return ResultWrapper.Unauthorized()
                            .ToActionResult(this);
                    }

                    var paymentResult = await _paymentService.GetLatestPaymentAsync(parsedSubscriptionId);

                    if (!paymentResult.IsSuccess || paymentResult.Data == null)
                    {
                        return ResultWrapper.NotFound("Payment", $"latest for subscription {subscriptionId}")
                            .ToActionResult(this);
                    }

                    var paymentDto = new PaymentDto(paymentResult.Data);

                    await _logger.LogTraceAsync($"Retrieved latest payment status for subscription {subscriptionId}");

                    return ResultWrapper.Success(paymentDto, "Latest payment status retrieved successfully")
                        .ToActionResult(this);
                }
                catch (Exception ex)
                {
                    await _logger.LogTraceAsync($"Error getting payment status for subscription {subscriptionId}: {ex.Message}",
                        requiresResolution: true,
                        level: Domain.Constants.Logging.LogLevel.Error);

                    return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
                }
            }
        }

        /// <summary>
        /// Fetches and updates missing payment records from Stripe for a subscription
        /// </summary>
        /// <param name="subscriptionId">The subscription ID</param>
        /// <returns>Result of the update operation</returns>
        /// <response code="200">Returns the number of updated payment records</response>
        /// <response code="400">If the subscription ID is invalid</response>
        /// <response code="401">If the user is not authenticated</response>
        /// <response code="403">If the user is not authorized to access this subscription</response>
        /// <response code="404">If the subscription is not found</response>
        /// <response code="500">If an internal error occurs</response>
        [HttpGet("fetch-update/subscription/{subscriptionId}")]
        [EnableRateLimiting("standard")]
        [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> FetchUpdatePayments(string subscriptionId)
        {
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["Operation"] = "FetchUpdatePayments",
                ["SubscriptionId"] = subscriptionId,
                ["CorrelationId"] = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            }))
            {
                try
                {
                    if (!Guid.TryParse(subscriptionId, out var parsedSubscriptionId))
                    {
                        return ResultWrapper.ValidationError(new()
                        {
                            ["subscriptionId"] = ["Invalid subscription ID format"]
                        }).ToActionResult(this);
                    }

                    // Get the current user ID from claims
                    var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                    if (string.IsNullOrEmpty(userId))
                    {
                        return ResultWrapper.Unauthorized()
                            .ToActionResult(this);
                    }

                    // Verify that the subscription belongs to the user
                    var subscriptionResult = await _subscriptionService.GetByIdAsync(parsedSubscriptionId);

                    if (subscriptionResult == null || !subscriptionResult.IsSuccess || subscriptionResult.Data == null)
                    {
                        return ResultWrapper.NotFound("Subscription", subscriptionId)
                            .ToActionResult(this);
                    }

                    var subscription = subscriptionResult.Data;

                    if (subscription.UserId.ToString() != userId && !User.IsInRole("ADMIN"))
                    {
                        _logger.LogWarning("Unauthorized access attempt to subscription {SubscriptionId} fetch-update by user {UserId}",
                            subscriptionId, userId);

                        return ResultWrapper.Unauthorized()
                            .ToActionResult(this);
                    }

                    string stripeSubscriptionId = subscription.ProviderSubscriptionId;

                    // If no Stripe subscription ID is found, try to find it using metadata
                    if (string.IsNullOrEmpty(stripeSubscriptionId))
                    {
                        await _logger.LogTraceAsync($"No Stripe subscription ID found for subscription {subscriptionId}, searching Stripe by metadata");

                        try
                        {
                            // Search for Stripe subscriptions with our domain subscription ID in metadata
                            var searchResult = await _paymentService.SearchStripeSubscriptionByMetadataAsync("subscriptionId", subscriptionId);

                            if (searchResult.IsSuccess && !string.IsNullOrEmpty(searchResult.Data))
                            {
                                stripeSubscriptionId = searchResult.Data;
                                await _logger.LogTraceAsync($"Found Stripe subscription {stripeSubscriptionId} for domain subscription {subscriptionId}");

                                // Update the domain subscription record with the found Stripe subscription ID
                                var updateFields = new Dictionary<string, object>
                                {
                                    ["ProviderSubscriptionId"] = stripeSubscriptionId
                                };

                                var updateResult = await _subscriptionService.UpdateAsync(parsedSubscriptionId, updateFields);

                                if (updateResult.IsSuccess)
                                {
                                    await _logger.LogTraceAsync($"Updated domain subscription {subscriptionId} with Stripe subscription ID {stripeSubscriptionId}");
                                }
                                else
                                {
                                    _logger.LogWarning("Failed to update domain subscription {SubscriptionId} with Stripe subscription ID {StripeSubscriptionId}: {Error}",
                                        subscriptionId, stripeSubscriptionId, updateResult.ErrorMessage);
                                }
                            }
                            else
                            {
                                await _logger.LogTraceAsync($"No Stripe subscription found with metadata subscriptionId={subscriptionId}: {searchResult.ErrorMessage}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning("Error searching for Stripe subscription by metadata for subscription {SubscriptionId}: {Error}",
                                subscriptionId, ex.Message);
                            // Continue with the original logic - this is a fallback attempt
                        }
                    }

                    // If we still don't have a Stripe subscription ID, return an informative error
                    if (string.IsNullOrEmpty(stripeSubscriptionId))
                    {
                        return ResultWrapper.NotFound("Subscription")
                            .ToActionResult(this);
                    }

                    await _logger.LogTraceAsync($"Fetching payment updates for subscription {subscriptionId} (Stripe: {stripeSubscriptionId})");

                    // Call the payment service to fetch and process missing payments
                    var fetchResult = await _paymentService.FetchPaymentsBySubscriptionAsync(stripeSubscriptionId);

                    if (!fetchResult.IsSuccess)
                    {
                        return ResultWrapper.Failure(fetchResult.Reason,
                            fetchResult.ErrorMessage ?? "Failed to fetch payment updates",
                            "FETCH_PAYMENTS_FAILED")
                            .ToActionResult(this);
                    }

                    await _logger.LogTraceAsync($"Successfully processed {fetchResult.Data} missing payment records for subscription {subscriptionId}");

                    return ResultWrapper.Success(fetchResult.Data, "Payment records updated successfully")
                        .ToActionResult(this);
                }
                catch (Exception ex)
                {
                    await _logger.LogTraceAsync($"Error fetching payment updates for subscription {subscriptionId}: {ex.Message}",
                        requiresResolution: true,
                        level: Domain.Constants.Logging.LogLevel.Error);

                    return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
                }
            }
        }
    }
}