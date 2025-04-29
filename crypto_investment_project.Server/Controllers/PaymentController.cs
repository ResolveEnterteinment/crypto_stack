using Application.Interfaces;
using Application.Interfaces.Logging;
using Application.Interfaces.Payment;
using Application.Interfaces.Subscription;
using Domain.Constants;
using Domain.Constants.Payment;
using Domain.DTOs.Error;
using Domain.DTOs.Payment;
using Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Diagnostics;

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
        private readonly IEventService _eventService;
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
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
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
        //[IgnoreAntiforgeryToken]
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
                    var appBaseUrl = _configuration["AppSettings:BaseUrl"] ?? "https://example.com";
                    string returnUrl = request.ReturnUrl ?? $"{appBaseUrl}/payment/success?subscription_id={subscriptionId}&amount={request.Amount}&currency={request.Currency ?? "USD"}";
                    string cancelUrl = request.CancelUrl ?? $"{appBaseUrl}/payment/cancel?subscription_id={subscriptionId}";

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
                        throw new PaymentApiException("Failed to retrieve payment from checkout session.", sessionResult?.Data?.Provider ?? "Session result returned null.");
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
                var payment = await _paymentService.GetPaymentDetailsAsync(paymentId);
                if (payment == null)
                {
                    Logger.LogError("Failed to cancel payment {PaymentId}: Payment not found", paymentId);
                    throw new ResourceNotFoundException(paymentId.GetType().Name, paymentId);
                }

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

                // If payment is associated with a subscription, update its status
                if (!string.IsNullOrEmpty(payment.SubscriptionId))
                {
                    await _subscriptionService.UpdateSubscriptionStatusAsync(Guid.Parse(payment.SubscriptionId), SubscriptionStatus.Canceled);
                }

                return Ok(result);
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
    }
}