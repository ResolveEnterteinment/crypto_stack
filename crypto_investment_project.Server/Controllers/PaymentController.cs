using Application.Extensions;
using Application.Interfaces;
using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Application.Interfaces.Payment;
using Application.Interfaces.Subscription;
using Domain.Constants.Payment;
using Domain.DTOs;
using Domain.DTOs.Error;
using Domain.DTOs.Payment;
using Domain.Exceptions;
using Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Diagnostics;
using System.Security.Claims;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class PaymentController : ControllerBase
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly IPaymentService _paymentService;
        private readonly IIdempotencyService _idempotencyService;
        private readonly IConfiguration _configuration;
        private readonly ILoggingService Logger;

        public PaymentController(
            ISubscriptionService subscriptionService,
            IPaymentService paymentService,
            IIdempotencyService idempotencyService,
            IEventService eventService,
            IConfiguration configuration,
            ILoggingService logger)
        {
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Creates a Stripe checkout session for subscription payment
        /// </summary>
        /// <param name="request">Checkout session request</param>
        /// <returns>Checkout session URL</returns>
        [HttpPost("create-checkout-session")]
        [Authorize]
        [EnableRateLimiting("standard")]
        public async Task<IActionResult> CreateCheckoutSession([FromBody] CheckoutSessionRequest request)
        {
            var correlationId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

            using (Logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = correlationId,
                ["Operation"] = "CreateCheckoutSession",
                ["UserId"] = request!.UserId,
                ["SubscriptionId"] = request!.SubscriptionId
            }))
            {
                try
                {
                    // Log the full request for debugging
                    await Logger.LogTraceAsync($"Payment request received: {request}",
                        "CreateCheckoutSession");

                    #region Validate
                    // Validate request
                    if (request is null)
                    {
                        return BadRequest(new { message = "Request body is required", code = "INVALID_REQUEST" });
                    }

                    // Log all validation checks
                    if (string.IsNullOrEmpty(request.SubscriptionId))
                    {
                        return BadRequest(new { message = "SubscriptionId is required", code = "MISSING_SUBSCRIPTION_ID" });
                    }

                    if (!Guid.TryParse(request.SubscriptionId, out var subscriptionId))
                    {
                        return BadRequest(new { message = "Invalid subscription ID format", code = "INVALID_SUBSCRIPTION_ID" });
                    }

                    if (string.IsNullOrEmpty(request.UserId))
                    {
                        return BadRequest(new { message = "UserId is required", code = "MISSING_USER_ID" });
                    }

                    if (request.Amount <= 0)
                    {
                        return BadRequest(new { message = "Amount must be greater than zero", code = "INVALID_AMOUNT" });
                    }

                    // Check if subscription exists and belongs to the user
                    var subscriptionResult = await _subscriptionService.GetByIdAsync(subscriptionId);
                    if (subscriptionResult == null || !subscriptionResult.IsSuccess)
                    {
                        await Logger.LogTraceAsync($"Subscription not found: {subscriptionId}", "CreateCheckoutSession");

                        return BadRequest(new { message = "Subscription not found", code = "SUBSCRIPTION_NOT_FOUND" });
                    }
                    var subscription = subscriptionResult.Data;

                    if (subscription.UserId.ToString() != request.UserId)
                    {
                        Logger.LogError($"Subscription not found: {subscriptionId}");
                        return Unauthorized(new { message = "Subscription does not belong to the user", code = "UNAUTHORIZED_ACCESS" });
                    }
                    #endregion

                    // Check for idempotency
                    string idempotencyKey = request.IdempotencyKey ?? Guid.NewGuid().ToString();
                    if (await _idempotencyService.HasKeyAsync(idempotencyKey))
                    {
                        return Ok(new { status = "Already processed" });
                    }

                    // Set default return and cancel URLs if not provided
                    var appBaseUrl = _configuration["PaymentService:BaseUrl"] ?? "https://localhost:7144";
                    string returnUrl = request.ReturnUrl;
                    string cancelUrl = request.CancelUrl;

                    // Create Stripe checkout session
                    var sessionResult = await _paymentService.CreateCheckoutSessionAsync(new CreateCheckoutSessionDto
                    {
                        SubscriptionId = subscriptionId.ToString(),
                        UserId = request.UserId,
                        UserEmail = User.Identity.Name,
                        Amount = request.Amount,
                        Currency = request.Currency ?? "USD",
                        IsRecurring = request.IsRecurring,
                        Interval = request.Interval,
                        ReturnUrl = returnUrl,
                        CancelUrl = cancelUrl,
                        Metadata = new Dictionary<string, string>
                        {
                            ["correlation_id"] = correlationId
                        }
                    });

                    if (sessionResult is null || !sessionResult.IsSuccess || sessionResult.Data is null)
                    {
                        throw new PaymentApiException($"Failed to retrieve payment from checkout session:  {sessionResult?.Data?.Provider ?? "Session result returned null."}", "Stripe");
                    }

                    var checkoutSession = sessionResult.Data;

                    // Create response
                    var response = new CheckoutSessionResponse
                    {
                        Success = true,
                        Message = "Checkout session created successfully",
                        CheckoutUrl = checkoutSession.Url,
                        ClientSecret = checkoutSession.ClientSecret,
                    };

                    // Store response for idempotency
                    await _idempotencyService.StoreResultAsync(idempotencyKey, response, TimeSpan.FromHours(1));

                    return Ok(response);
                }
                catch (Exception ex)
                {
                    await Logger.LogTraceAsync($"Error creating checkout session: {ex.Message}", requiresResolution: true);

                    return StatusCode(StatusCodes.Status500InternalServerError, new
                    {
                        message = "An error occurred while processing your request",
                        error = ex.Message,
                        code = "SERVER_ERROR",
                        traceId = correlationId
                    });
                }
            }
        }

        /// <summary>
        /// Cancels a pending payment
        /// </summary>
        /// <param name="paymentId">Payment ID</param>
        /// <returns>Cancellation result</returns>
        [HttpPost("cancel/{paymentId}")]
        [Authorize]
        [EnableRateLimiting("standard")]
        [ProducesResponseType(typeof(PaymentCancelResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CancelPayment(string paymentId)
        {
            var correlationId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

            using var scope = Logger.BeginScope(new Dictionary<string, object>
            {
                ["PaymentId"] = paymentId
            });

            try
            {
                if (string.IsNullOrEmpty(paymentId))
                {
                    throw new ArgumentException("Payment ID is required", "PAYMENT_ID");
                }

                // Get the payment to check if it belongs to the current user
                var paymentResult = await _paymentService.GetPaymentDetailsAsync(paymentId);
                if (paymentResult == null ||!paymentResult.IsSuccess)
                {
                    Logger.LogError("Failed to cancel payment {PaymentId}: Payment not found", paymentId);
                    throw new ResourceNotFoundException(paymentId.GetType().Name, paymentId);
                }
                var payment = paymentResult.Data;

                // Ensure the payment belongs to the current user
                string currentUserId = User.FindFirst("sub")?.Value ?? User.FindFirst("uid")?.Value;
                if (payment.UserId != currentUserId)
                {
                    throw new UnauthorizedAccessException();
                }

                // Only allow cancellation of pending payments
                if (payment.Status != PaymentStatus.Pending)
                {
                    throw new InvalidOperationException("Only pending payments can be cancelled");
                }

                // Cancel the payment
                var result = await _paymentService.CancelPaymentAsync(paymentId);
                if (!result.IsSuccess)
                {
                    throw new PaymentApiException(result.ErrorMessage, "Stripe", paymentId);
                }

                return Ok(result.IsSuccess);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new ErrorResponse
                {
                    Message = ex.Message,
                    Code = $"INVALID_{ex.ParamName}",
                    TraceId = correlationId
                });
            }
            catch (ResourceNotFoundException ex)
            {
                return BadRequest(new ErrorResponse
                {
                    Message = ex.Message,
                    Code = $"{ex.ResourceId}_NOT_FOUND",
                    TraceId = correlationId
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new ErrorResponse
                {
                    Message = ex.Message,
                    Code = $"INVALID_PAYMENT_STATUS",
                    TraceId = correlationId
                });
            }
            catch (UnauthorizedAccessException)
            {
                return Forbid();
            }

            catch (PaymentApiException ex)
            {
                await Logger.LogTraceAsync($"Error cancelling payment: {ex.Message}", "CancelPayment", requiresResolution: true);

                return BadRequest(new ErrorResponse
                {
                    Message = ex.Message,
                    Code = $"CANCELLATION_FAILED",
                    TraceId = correlationId
                });
            }
            catch (Exception ex)
            {
                await Logger.LogTraceAsync($"Error cancelling payment: {ex.Message}", "CancelPayment", requiresResolution: true);

                return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
                {
                    Message = "An error occurred while cancelling the payment",
                    Code = "SERVER_ERROR",
                    TraceId = correlationId
                });
            }
        }

        /// <summary>
        /// Gets payment history for a subscription
        /// </summary>
        /// <param name="subscriptionId">The subscription ID</param>
        /// <returns>List of payments for the subscription</returns>
        [HttpGet("subscription/{subscriptionId}")]
        [Authorize]
        public async Task<IActionResult> GetSubscriptionPayments(string subscriptionId)
        {
            try
            {
                if (!Guid.TryParse(subscriptionId, out var parsedSubscriptionId))
                {
                    return BadRequest("Invalid subscription ID format");
                }

                // Get the current user ID from claims
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("User ID not found in claims");
                }

                // Verify that the subscription belongs to the user
                var subscription = await _subscriptionService.GetByIdAsync(parsedSubscriptionId);
                if (!subscription.IsSuccess || subscription.Data == null)
                {
                    return NotFound($"Subscription {subscriptionId} not found");
                }

                if (subscription.Data.UserId.ToString() != userId && !User.IsInRole("ADMIN"))
                {
                    return Forbid("You don't have permission to view this subscription's payments");
                }

                // Get payments for the subscription
                // We need to add this method to the PaymentService
                var filter = MongoDB.Driver.Builders<Domain.Models.Payment.PaymentData>.Filter.Eq(p => p.SubscriptionId, parsedSubscriptionId);
                var paymentsResult = await _paymentService.GetManyAsync(filter);

                return !paymentsResult.IsSuccess ? StatusCode(500, paymentsResult.ErrorMessage) : (IActionResult)Ok(new { data = paymentsResult.Data });
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting payments for subscription {subscriptionId}: {ex.Message}");
                return StatusCode(500, "An error occurred while processing your request");
            }
        }

        /// <summary>
        /// Gets the latest payment status data for a subscription
        /// </summary>
        /// <param name="subscriptionId">The subscription ID</param>
        /// <returns>Details of the latest payment for the subscription</returns>
        [HttpGet("status/subscription/{subscriptionId}")]
        [Authorize]
        public async Task<IActionResult> GetSubscriptionPaymentStatus(string subscriptionId)
        {
            try
            {
                if (!Guid.TryParse(subscriptionId, out var parsedSubscriptionId))
                {
                    return BadRequest("Invalid subscription ID format");
                }

                // Get the current user ID from claims
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized("User ID not found in claims");
                }

                // Verify that the subscription belongs to the user
                var subscription = await _subscriptionService.GetByIdAsync(parsedSubscriptionId);
                if (!subscription.IsSuccess || subscription.Data == null)
                {
                    return NotFound($"Subscription {subscriptionId} not found");
                }

                if (subscription.Data.UserId.ToString() != userId)
                {
                    return Forbid("You don't have permission to view this subscription's payments");
                }

                var paymentResult = await _paymentService.GetLatestPaymentAsync(parsedSubscriptionId);

                return !paymentResult.IsSuccess ? StatusCode(500, paymentResult.ErrorMessage) : (IActionResult)Ok(new { data = new PaymentDto(paymentResult.Data) });
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error getting payments for subscription {subscriptionId}: {ex.Message}");
                return StatusCode(500, "An error occurred while processing your request");
            }
        }

        /// <summary>
        /// Fetches and updates missing payment records from Stripe for a subscription
        /// </summary>
        /// <param name="subscriptionId">The subscription ID</param>
        /// <returns>Result of the update operation</returns>
        [HttpGet("fetch-update/subscription/{subscriptionId}")]
        [Authorize]
        [EnableRateLimiting("standard")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> FetchUpdatePayments(string subscriptionId)
        {
            var correlationId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

            using var scope = Logger.BeginScope(new Dictionary<string, object>
            {
                ["SubscriptionId"] = subscriptionId,
                ["CorrelationId"] = correlationId,
                ["Operation"] = "FetchUpdatePayments"
            });

            try
            {
                if (!Guid.TryParse(subscriptionId, out var parsedSubscriptionId))
                {
                    return BadRequest(new ErrorResponse
                    {
                        Message = "Invalid subscription ID format",
                        Code = "INVALID_SUBSCRIPTION_ID",
                        TraceId = correlationId
                    });
                }

                // Get the current user ID from claims
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    return Unauthorized(new ErrorResponse
                    {
                        Message = "User ID not found in claims",
                        Code = "UNAUTHORIZED_ACCESS",
                        TraceId = correlationId
                    });
                }

                // Verify that the subscription belongs to the user
                var subscription = await _subscriptionService.GetByIdAsync(parsedSubscriptionId);
                if (!subscription.IsSuccess || subscription.Data == null)
                {
                    return NotFound(new ErrorResponse
                    {
                        Message = $"Subscription {subscriptionId} not found",
                        Code = "SUBSCRIPTION_NOT_FOUND",
                        TraceId = correlationId
                    });
                }

                if (subscription.Data.UserId.ToString() != userId && !User.IsInRole("ADMIN"))
                {
                    return Forbid();
                }

                var subscriptionData = subscription.Data;
                string stripeSubscriptionId = subscriptionData.ProviderSubscriptionId;

                // If no Stripe subscription ID is found, try to find it using metadata
                if (string.IsNullOrEmpty(stripeSubscriptionId))
                {
                    await Logger.LogTraceAsync($"No Stripe subscription ID found for subscription {subscriptionId}, searching Stripe by metadata", "FetchUpdatePayments");

                    try
                    {
                        // Search for Stripe subscriptions with our domain subscription ID in metadata
                        var searchResult = await _paymentService.SearchStripeSubscriptionByMetadataAsync("subscriptionId", subscriptionId);

                        if (searchResult.IsSuccess && !string.IsNullOrEmpty(searchResult.Data))
                        {
                            stripeSubscriptionId = searchResult.Data;
                            await Logger.LogTraceAsync($"Found Stripe subscription {stripeSubscriptionId} for domain subscription {subscriptionId}", "FetchUpdatePayments");

                            // Update the domain subscription record with the found Stripe subscription ID
                            var updateFields = new Dictionary<string, object>
                            {
                                ["ProviderSubscriptionId"] = stripeSubscriptionId
                            };

                            var updateResult = await _subscriptionService.UpdateAsync(parsedSubscriptionId, updateFields);
                            if (updateResult.IsSuccess)
                            {
                                await Logger.LogTraceAsync($"Updated domain subscription {subscriptionId} with Stripe subscription ID {stripeSubscriptionId}", "FetchUpdatePayments");
                            }
                            else
                            {
                                Logger.LogWarning("Failed to update domain subscription {SubscriptionId} with Stripe subscription ID {StripeSubscriptionId}: {Error}",
                                    subscriptionId, stripeSubscriptionId, updateResult.ErrorMessage);
                            }
                        }
                        else
                        {
                            await Logger.LogTraceAsync($"No Stripe subscription found with metadata subscriptionId={subscriptionId}: {searchResult.ErrorMessage}", "FetchUpdatePayments");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning("Error searching for Stripe subscription by metadata for subscription {SubscriptionId}: {Error}",
                            subscriptionId, ex.Message);
                        // Continue with the original logic - this is a fallback attempt
                    }
                }

                // If we still don't have a Stripe subscription ID, return an informative error
                if (string.IsNullOrEmpty(stripeSubscriptionId))
                {
                    return BadRequest(new ErrorResponse
                    {
                        Message = "No Stripe subscription found for this subscription. This may indicate the subscription was not properly created with Stripe, or the subscription ID metadata is missing.",
                        Code = "STRIPE_SUBSCRIPTION_NOT_FOUND",
                        TraceId = correlationId
                    });
                }

                await Logger.LogTraceAsync($"Fetching payment updates for subscription {subscriptionId} (Stripe: {stripeSubscriptionId})", "FetchUpdatePayments");

                // Call the payment service to fetch and process missing payments
                var fetchResult = await _paymentService.FetchPaymentsBySubscriptionAsync(stripeSubscriptionId);

                if (!fetchResult.IsSuccess)
                {
                    return BadRequest(new ErrorResponse
                    {
                        Message = fetchResult.ErrorMessage ?? "Failed to fetch payment updates",
                        Code = "FETCH_PAYMENTS_FAILED",
                        TraceId = correlationId
                    });
                }

                await Logger.LogTraceAsync($"Successfully processed {fetchResult.Data} missing payment records", "FetchUpdatePayments");

                return ResultWrapper<int>.Success(fetchResult.Data, "Payment records updated successfully").ToActionResult(this);
            }
            catch (Exception ex)
            {
                await Logger.LogTraceAsync($"Error fetching payment updates: {ex.Message}", "FetchUpdatePayments", requiresResolution: true);

                return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
                {
                    Message = "An error occurred while updating payment records",
                    Code = "SERVER_ERROR",
                    TraceId = correlationId
                });
            }
        }
    }
}