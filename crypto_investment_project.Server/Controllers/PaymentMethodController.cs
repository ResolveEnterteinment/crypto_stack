using Application.Contracts.Responses.Payment;
using Application.Extensions;
using Application.Interfaces.Payment;
using Application.Interfaces.Subscription;
using Domain.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace crypto_investment_project.Server.Controllers
{
    [ApiController]
    [Route("api/payment-methods")]
    [Authorize]
    public class PaymentMethodController : ControllerBase
    {
        private readonly IPaymentService _paymentService;
        private readonly ISubscriptionService _subscriptionService;
        private readonly ILogger<PaymentMethodController> _logger;

        public PaymentMethodController(
            IPaymentService paymentService,
            ISubscriptionService subscriptionService,
            ILogger<PaymentMethodController> logger)
        {
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Creates a checkout session for updating a payment method
        /// </summary>
        /// <param name="subscriptionId">ID of the subscription to update payment method for</param>
        /// <returns>URL to redirect the user to for payment method update</returns>
        [HttpPost("update/{subscriptionId}")]
        public async Task<IActionResult> CreateUpdatePaymentMethodSession(string subscriptionId)
        {
            try
            {
                // Get the current user ID from claims
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                {
                    return ResultWrapper.Unauthorized()
                        .ToActionResult(this);
                }

                // Verify that the subscription belongs to the user
                var subscription = await _subscriptionService.GetByIdAsync(Guid.Parse(subscriptionId));
                if (!subscription.IsSuccess || subscription.Data == null)
                {
                    return ResultWrapper.NotFound("Subscription", subscriptionId)
                        .ToActionResult(this);
                }

                if (subscription.Data.UserId.ToString() != userId && !User.IsInRole("ADMIN"))
                {
                    return ResultWrapper.Unauthorized()
                        .ToActionResult(this);
                }

                // Create the update payment method session
                var sessionResult = await _paymentService.CreateUpdatePaymentMethodSessionAsync(userId, subscriptionId);
                if (!sessionResult.IsSuccess)
                {
                    return ResultWrapper.Failure(sessionResult.Reason, "Failed to create payment method session")
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

                return ResultWrapper.Success(response, "Payment method session created successfully")
                        .ToActionResult(this);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating update payment method session for subscription {SubscriptionId}", subscriptionId);
                
                return ResultWrapper.InternalServerError()
                        .ToActionResult(this);
            }
        }
    }
}