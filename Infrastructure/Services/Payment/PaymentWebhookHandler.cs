using Application.Interfaces;
using Application.Interfaces.Logging;
using Application.Interfaces.Payment;
using Application.Interfaces.Subscription;
using Domain.Constants;
using Domain.Constants.Logging;
using Domain.DTOs;
using Domain.DTOs.Payment;
using Domain.Exceptions;
using MongoDB.Driver;

namespace Infrastructure.Services.Payment
{
    /// <summary>
    /// Enhanced service for handling Stripe webhook events
    /// </summary>
    public class StripeWebhookHandler : IPaymentWebhookHandler
    {
        private readonly IPaymentService _paymentService;
        private readonly ISubscriptionService _subscriptionService;
        private readonly IIdempotencyService _idempotencyService;
        private readonly ILoggingService _logger;

        public StripeWebhookHandler(
            IPaymentService paymentService,
            ISubscriptionService subscriptionService,
            IIdempotencyService idempotencyService,
            ILoggingService logger)
        {
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Handles a Stripe event
        /// </summary>
        public async Task<ResultWrapper> HandleStripeEventAsync(object stripeEventObject)
        {
            using var Scope = _logger.BeginScope("StripeWebhookHandler => HandleStripeEventAsync");
            var stripeEvent = stripeEventObject as Stripe.Event;

            if (stripeEvent == null)
            {
                return ResultWrapper.Failure(FailureReason.ValidationError, "Stripe event is required.");
            }

            try
            {
                _logger.LogInformation("Processing Stripe webhook event {EventId} of type {EventType}",
                    stripeEvent.Id, stripeEvent.Type);

                bool handled = false;

                // Handle different event types
                switch (stripeEvent.Type)
                {
                    case "checkout.session.completed":
                        handled = true;
                        return await HandleCheckoutSessionCompletedAsync(stripeEvent);
                    case "invoice.paid":
                        handled = true;
                        return await HandleInvoicePaidAsync(stripeEvent);
                    case "customer.subscription.deleted":
                        handled = await HandleSubscriptionDeletedAsync(stripeEvent);
                        break;
                    default:
                        _logger.LogInformation("Unhandled Stripe event type: {EventType}", stripeEvent.Type);
                        handled = true; // Mark as handled since we're choosing not to process it
                        break;
                }

                // Mark the event as processed for idempotency
                if (!handled)
                {
                    throw new PaymentEventException($"Failed to process {stripeEvent.Type} event", stripeEvent.Type, "Stripe", stripeEvent.Id);
                }

                return ResultWrapper.Success("Event processed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error processing Stripe webhook event {EventId} of type {EventType}: {ErrorMessage}",
                    stripeEvent.Id, stripeEvent.Type, ex.Message);
                return ResultWrapper.FromException(ex);
            }
        }

        /// <summary>
        /// Handles the checkout.session.completed event
        /// </summary>
        private async Task<ResultWrapper> HandleCheckoutSessionCompletedAsync(Stripe.Event stripeEvent)
        {
            using var Scope = _logger.BeginScope("StripeWebhookHandler => HandleCheckoutSessionCompletedAsync");

            var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
            if (session == null)
            {
                _logger.LogWarning("Invalid event data: Expected Session object");
                return ResultWrapper.Failure(FailureReason.ValidationError, "Invalid event data: Expected Session object");
            }

            using var SessionScope = _logger.EnrichScope(
                ("SessionId", session.Id)
                );

            var metadata = session.Metadata;
            // Extract subscription ID from metadata
            if (metadata == null ||
                !metadata.TryGetValue("subscriptionId", out var subscriptionId) ||
                string.IsNullOrEmpty(subscriptionId) ||
                !Guid.TryParse(subscriptionId, out var parsedSubscriptionId))
            {
                await _logger.LogTraceAsync("Missing or invalid subscriptionId in Invoice metadata", "Extract subscription ID from metadata", LogLevel.Error, true);
                return ResultWrapper.Failure(FailureReason.ValidationError, "Missing or invalid subscriptionId in Invoice metadata");
            }

            // Extract user ID from metadata
            if (!metadata.TryGetValue("userId", out var userId) ||
                string.IsNullOrEmpty(userId))
            {
                await _logger.LogTraceAsync("Missing or invalid userId in Invoice metadata", "Extract subscription ID from metadata", LogLevel.Error, true);
                return ResultWrapper.Failure(FailureReason.ValidationError, "Missing userId in Invoice metadata");
            }

            using var MetadataScope = _logger.EnrichScope(
                ("SubscriptionId", subscriptionId),
                ("UserId", userId)
                );

            try
            {
                var processResult = await _paymentService.ProcessCheckoutSessionCompletedAsync(new SessionDto()
                {
                    Id = session.Id,
                    Provider = "Stripe",
                    ClientSecret = session.ClientSecret,
                    Url = session.Url,
                    SubscriptionId = session.SubscriptionId,
                    InvoiceId = session.InvoiceId,
                    Metadata = session.Metadata,
                    Status = session.Status
                });

                if (processResult == null || !processResult.IsSuccess)
                {
                    throw processResult == null ? new ServiceUnavailableException("PaymentService") : new PaymentApiException(processResult?.ErrorMessage ?? "", "Stripe");
                }
                return processResult;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error handling checkout.session.completed for subscription {SubscriptionId}: {ErrorMessage}",
                    subscriptionId, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Handles the payment_intent.succeeded event
        /// </summary>
        private async Task<ResultWrapper> HandleInvoicePaidAsync(Stripe.Event stripeEvent)
        {
            using var Scope = _logger.BeginScope("StripeWebhookHandler => HandleInvoicePaidAsync");

            _logger.LogInformation($"Handling stripe event invoice.paid");
            var invoice = stripeEvent.Data.Object as Stripe.Invoice;
            if (invoice == null)
            {
                _logger.LogWarning("Invalid event data: Expected Invoice object");
                return ResultWrapper.Failure(FailureReason.ValidationError, "Invalid event data: Expected Invoice object");
            }

            using var InvoiceScope = _logger.EnrichScope(("InvoiceId", invoice.Id));

            var metadata = invoice.SubscriptionDetails.Metadata;
            // Extract subscription ID from metadata
            if (metadata == null ||
                !metadata.TryGetValue("subscriptionId", out var subscriptionId) ||
                string.IsNullOrEmpty(subscriptionId) ||
                !Guid.TryParse(subscriptionId, out var parsedSubscriptionId))
            {
                await _logger.LogTraceAsync("Missing or invalid subscriptionId in Invoice metadata", "Extract subscription ID from metadata", LogLevel.Error, true);
                return ResultWrapper.Failure(FailureReason.ValidationError, "Missing or invalid subscriptionId in Invoice metadata");
            }

            // Extract user ID from metadata
            if (!metadata.TryGetValue("userId", out var userId) ||
                string.IsNullOrEmpty(userId))
            {
                await _logger.LogTraceAsync("Missing or invalid userId in Invoice metadata", "Extract user ID from metadata", LogLevel.Error, true);
                return ResultWrapper.Failure(FailureReason.ValidationError, "Missing userId in Invoice metadata");
            }

            using var MetadataScope = _logger.EnrichScope(
                ("SubscriptionId", subscriptionId),
                ("UserId", userId)
                );

            try
            {
                var processResult = await _paymentService.ProcessInvoicePaidEvent(new()
                {
                    Id = invoice.Id,
                    Provider = "Stripe",
                    ChargeId = invoice.ChargeId,
                    PaymentIntentId = invoice.PaymentIntentId,
                    UserId = userId,
                    SubscriptionId = subscriptionId,
                    Amount = invoice.AmountPaid,
                    Currency = invoice.Currency,
                    Status = invoice.Status
                });
                if (processResult == null || !processResult.IsSuccess)
                {
                    throw processResult == null ? new ServiceUnavailableException("PaymentService") : new PaymentApiException(processResult?.ErrorMessage ?? "", "Stripe");
                }
                return processResult;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error handling invoice.paid for subscription {SubscriptionId}: {ErrorMessage}",
                    subscriptionId, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Handles the customer.subscription.deleted event
        /// </summary>
        private async Task<bool> HandleSubscriptionDeletedAsync(Stripe.Event stripeEvent)
        {
            using var Scope = _logger.BeginScope("StripeWebhookHandler => HandleSubscriptionDeletedAsync");

            var stripeSubscription = stripeEvent.Data.Object as Stripe.Subscription;
            if (stripeSubscription == null)
            {
                _logger.LogWarning("Invalid event data: Expected Subscription object");
                return false;
            }

            using var StripeSubscriptionScope = _logger.EnrichScope(
                ("StripeSubscriptionId", stripeSubscription.Id)
                );

            // Find our subscription
            var filter = Builders<Domain.Models.Subscription.SubscriptionData>.Filter.Eq(
                s => s.ProviderSubscriptionId, stripeSubscription.Id);

            var subscriptionResult = await _subscriptionService.GetOneAsync(filter);

            if (subscriptionResult == null || !subscriptionResult.IsSuccess)
            {
                _logger.LogWarning("No subscription found with provider subscription ID: {ProviderSubscriptionId}",
                    stripeSubscription.Id);
                return false;
            }

            var subscription = subscriptionResult.Data;

            using var SubscriptionScope = _logger.EnrichScope(
                ("SubscriptionId", subscription.Id)
                );

            try
            {
                // Mark subscription as cancelled
                var updateFields = new Dictionary<string, object>
                {
                    ["Status"] = SubscriptionStatus.Canceled,
                    ["IsCancelled"] = true
                };

                var updateResult = await _subscriptionService.UpdateAsync(subscription.Id, updateFields);

                if (updateResult == null || !updateResult.IsSuccess)
                {
                    _logger.LogWarning("Failed to cancel subscription {SubscriptionId}: No documents modified",
                        subscription.Id);
                    return false;
                }

                _logger.LogInformation("Successfully cancelled subscription {SubscriptionId} due to Stripe deletion",
                    subscription.Id);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error handling subscription deleted for {SubscriptionId}: {ErrorMessage}",
                    subscription.Id, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Maps a Stripe subscription status to our internal status
        /// </summary>
        private SubscriptionStatus StripeSubscriptionStatusToInternal(string stripeStatus)
        {
            return stripeStatus switch
            {
                "active" => SubscriptionStatus.Active,
                "canceled" => SubscriptionStatus.Canceled,
                "unpaid" => SubscriptionStatus.Active,
                "past_due" => SubscriptionStatus.Active,
                "trialing" => SubscriptionStatus.Active,
                "incomplete" => SubscriptionStatus.Pending,
                "incomplete_expired" => SubscriptionStatus.Canceled,
                _ => SubscriptionStatus.Pending
            };
        }
        /// <summary>
        /// Maps a Stripe subscription status to our internal status
        /// </summary>
        private string StripeSubscriptionIntervalToInternal(string stripeInterval)
        {
            return stripeInterval switch
            {
                "day" => SubscriptionInterval.Daily,
                "week" => SubscriptionInterval.Weekly,
                "month" => SubscriptionInterval.Monthly,
                "year" => SubscriptionInterval.Yearly,
                _ => SubscriptionInterval.Monthly
            };
        }
    }
}