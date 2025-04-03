using Application.Interfaces;
using Application.Interfaces.Payment;
using Domain.Constants;
using Domain.DTOs.Error;
using Domain.DTOs.Payment;
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
        private readonly IConfiguration _configuration;
        private readonly ILogger<PaymentController> _logger;

        public PaymentController(
            ISubscriptionService subscriptionService,
            IPaymentService paymentService,
            IIdempotencyService idempotencyService,
            IConfiguration configuration,
            ILogger<PaymentController> logger)
        {
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Creates a Stripe checkout session for subscription payment
        /// </summary>
        /// <param name="request">Checkout session request</param>
        /// <returns>Checkout session URL</returns>
        [HttpPost("create-checkout-session")]
        [Authorize]
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("standard")]
        [ProducesResponseType(typeof(CheckoutSessionResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CreateCheckoutSession([FromBody] CheckoutSessionRequest request)
        {
            var correlationId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = correlationId,
                ["Operation"] = "CreateCheckoutSession",
                ["UserId"] = request?.UserId,
                ["SubscriptionId"] = request?.SubscriptionId
            }))
            {
                try
                {
                    // Validate request
                    if (request is null)
                    {
                        return BadRequest(new ErrorResponse
                        {
                            Message = "Invalid request",
                            Code = "INVALID_REQUEST",
                            TraceId = correlationId
                        });
                    }

                    // Validate subscription ID
                    if (string.IsNullOrEmpty(request.SubscriptionId) || !Guid.TryParse(request.SubscriptionId, out var subscriptionId))
                    {
                        return BadRequest(new ErrorResponse
                        {
                            Message = "Invalid subscription ID",
                            Code = "INVALID_SUBSCRIPTION_ID",
                            TraceId = correlationId
                        });
                    }

                    // Validate user ID
                    if (string.IsNullOrEmpty(request.UserId))
                    {
                        return BadRequest(new ErrorResponse
                        {
                            Message = "Invalid user ID",
                            Code = "INVALID_USER_ID",
                            TraceId = correlationId
                        });
                    }

                    // Validate amount
                    if (request.Amount <= 0)
                    {
                        return BadRequest(new ErrorResponse
                        {
                            Message = "Amount must be greater than zero",
                            Code = "INVALID_AMOUNT",
                            TraceId = correlationId
                        });
                    }

                    // Check if subscription exists and belongs to the user
                    var subscription = await _subscriptionService.GetByIdAsync(subscriptionId);
                    if (subscription == null)
                    {
                        return BadRequest(new ErrorResponse
                        {
                            Message = "Subscription not found",
                            Code = "SUBSCRIPTION_NOT_FOUND",
                            TraceId = correlationId
                        });
                    }

                    if (subscription.UserId.ToString() != request.UserId)
                    {
                        return BadRequest(new ErrorResponse
                        {
                            Message = "Subscription does not belong to the user",
                            Code = "UNAUTHORIZED_ACCESS",
                            TraceId = correlationId
                        });
                    }

                    // Check for idempotency
                    string idempotencyKey = request.IdempotencyKey ?? Guid.NewGuid().ToString();
                    if (await _idempotencyService.HasKeyAsync(idempotencyKey))
                    {
                        _logger.LogInformation("Duplicate checkout session request {IdempotencyKey} received and skipped",
                        request.IdempotencyKey);
                        return Ok(new { status = "Already processed" });
                    }

                    // Update subscription status to pending
                    await _subscriptionService.UpdateSubscriptionStatusAsync(subscriptionId, SubscriptionStatus.Pending);

                    // Set default return and cancel URLs if not provided
                    var appBaseUrl = _configuration["AppSettings:BaseUrl"] ?? "https://example.com";
                    string returnUrl = request.ReturnUrl ?? $"{appBaseUrl}/payment/success?subscription_id={subscriptionId}&amount={request.Amount}&currency={request.Currency ?? "USD"}";
                    string cancelUrl = request.CancelUrl ?? $"{appBaseUrl}/payment/cancel?subscription_id={subscriptionId}";

                    // Create Stripe checkout session
                    var checkoutSession = await _paymentService.CreateCheckoutSessionAsync(new CreateCheckoutSessionDto
                    {
                        SubscriptionId = subscriptionId.ToString(),
                        UserId = request.UserId,
                        Amount = request.Amount,
                        Currency = request.Currency ?? "USD",
                        IsRecurring = request.IsRecurring,
                        ReturnUrl = returnUrl,
                        CancelUrl = cancelUrl,
                        Metadata = new Dictionary<string, string>
                        {
                            ["subscription_id"] = subscriptionId.ToString(),
                            ["interval"] = subscription.Interval,
                            ["user_id"] = request.UserId,
                            ["correlation_id"] = correlationId
                        }
                    });

                    // Create response
                    var response = new CheckoutSessionResponse
                    {
                        Success = true,
                        Message = "Checkout session created successfully",
                        CheckoutUrl = checkoutSession.CheckoutUrl,
                        ClientSecret = checkoutSession.ClientSecret
                    };

                    // Store response for idempotency
                    await _idempotencyService.StoreResultAsync(idempotencyKey, response, TimeSpan.FromHours(1));

                    return Ok(response);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error creating checkout session");
                    return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
                    {
                        Message = "An error occurred while processing your request",
                        Code = "SERVER_ERROR",
                        TraceId = correlationId
                    });
                }
            }
        }

        /// <summary>
        /// Gets the status of a payment
        /// </summary>
        /// <param name="paymentId">Payment ID</param>
        /// <returns>Payment status</returns>
        [HttpGet("status/{paymentId}")]
        [Authorize]
        [EnableRateLimiting("standard")]
        [ProducesResponseType(typeof(PaymentStatusResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetPaymentStatus(string paymentId)
        {
            var correlationId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = correlationId,
                ["Operation"] = "GetPaymentStatus",
                ["PaymentId"] = paymentId
            }))
            {
                try
                {
                    if (string.IsNullOrEmpty(paymentId))
                    {
                        return BadRequest(new ErrorResponse
                        {
                            Message = "Payment ID is required",
                            Code = "INVALID_PAYMENT_ID",
                            TraceId = correlationId
                        });
                    }

                    var status = await _paymentService.GetPaymentStatusAsync(paymentId);
                    if (status == null)
                    {
                        return NotFound(new ErrorResponse
                        {
                            Message = "Payment not found",
                            Code = "PAYMENT_NOT_FOUND",
                            TraceId = correlationId
                        });
                    }

                    return Ok(status);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting payment status");
                    return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
                    {
                        Message = "An error occurred while retrieving the payment status",
                        Code = "SERVER_ERROR",
                        TraceId = correlationId
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
        [ValidateAntiForgeryToken]
        [EnableRateLimiting("standard")]
        [ProducesResponseType(typeof(PaymentCancelResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CancelPayment(string paymentId)
        {
            var correlationId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = correlationId,
                ["Operation"] = "CancelPayment",
                ["PaymentId"] = paymentId
            }))
            {
                try
                {
                    if (string.IsNullOrEmpty(paymentId))
                    {
                        return BadRequest(new ErrorResponse
                        {
                            Message = "Payment ID is required",
                            Code = "INVALID_PAYMENT_ID",
                            TraceId = correlationId
                        });
                    }

                    // Get the payment to check if it belongs to the current user
                    var payment = await _paymentService.GetPaymentDetailsAsync(paymentId);
                    if (payment == null)
                    {
                        return NotFound(new ErrorResponse
                        {
                            Message = "Payment not found",
                            Code = "PAYMENT_NOT_FOUND",
                            TraceId = correlationId
                        });
                    }

                    // Ensure the payment belongs to the current user
                    string currentUserId = User.FindFirst("sub")?.Value ?? User.FindFirst("uid")?.Value;
                    if (payment.UserId != currentUserId)
                    {
                        return Forbid();
                    }

                    // Only allow cancellation of pending payments
                    if (payment.Status != PaymentStatus.Pending)
                    {
                        return BadRequest(new ErrorResponse
                        {
                            Message = "Only pending payments can be cancelled",
                            Code = "INVALID_PAYMENT_STATUS",
                            TraceId = correlationId
                        });
                    }

                    // Cancel the payment
                    var result = await _paymentService.CancelPaymentAsync(paymentId);
                    if (!result.IsSuccess)
                    {
                        return BadRequest(new ErrorResponse
                        {
                            Message = result.ErrorMessage,
                            Code = "CANCELLATION_FAILED",
                            TraceId = correlationId
                        });
                    }

                    // If payment is associated with a subscription, update its status
                    if (!string.IsNullOrEmpty(payment.SubscriptionId))
                    {
                        await _subscriptionService.UpdateSubscriptionStatusAsync(Guid.Parse(payment.SubscriptionId), SubscriptionStatus.Cancelled);
                    }

                    return Ok(result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error cancelling payment");
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
}