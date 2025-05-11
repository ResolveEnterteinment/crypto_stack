using Application.Contracts.Requests.Payment;
using Application.Interfaces;
using Application.Interfaces.Logging;
using Application.Interfaces.Payment;
using Application.Interfaces.Subscription;
using Domain.Constants;
using Domain.Constants.Logging;
using Domain.DTOs;
using Domain.DTOs.Payment;
using Domain.Exceptions;
using Domain.Models.Subscription;
using MongoDB.Driver;

namespace Infrastructure.Services.Payment
{
    /// <summary>
    /// Enhanced service for handling Stripe webhook events
    /// </summary>
    public class StripeWebhookHandler : IPaymentWebhookHandler
    {
        private readonly IPaymentService _paymentService;
        private readonly IEventService _eventService;
        private readonly ISubscriptionService _subscriptionService;
        private readonly IIdempotencyService _idempotencyService;
        private readonly ILoggingService _logger;

        public StripeWebhookHandler(
            IPaymentService paymentService,
            IEventService eventService,
            ISubscriptionService subscriptionService,
            IIdempotencyService idempotencyService,
            ILoggingService logger)
        {
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
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
                    case "setup_intent.succeeded":
                        handled = true;
                        return await HandleSetupIntentSucceededAsync(stripeEvent);
                    case "payment_intent.payment_failed":
                        handled = true;
                        return await HandlePaymentFailedAsync(stripeEvent);
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
                    ProviderSubscripitonId = invoice.SubscriptionId,
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
            var filter = Builders<SubscriptionData>.Filter.Eq(
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

        private async Task<ResultWrapper> HandlePaymentFailedAsync(Stripe.Event stripeEvent)
        {
            var paymentIntent = stripeEvent.Data.Object as Stripe.PaymentIntent;
            if (paymentIntent == null)
            {
                _logger.LogWarning("Invalid event data: Expected PaymentIntent object");
                return ResultWrapper.Failure(FailureReason.ValidationError, "Invalid event data: Expected PaymentIntent object");
            }

            using var Scope = _logger.BeginScope("StripeWebhookHandler::HandlePaymentFailed", new
            {
                PaymentProviderId = paymentIntent.Id,
            });


            using var PaymentIntentScope = _logger.EnrichScope(
                ("PaymentIntentId", paymentIntent.Id)
            );

            var metadata = paymentIntent.Metadata;

            // Extract subscription ID from metadata
            if (metadata == null ||
                !metadata.TryGetValue("subscriptionId", out var subscriptionId) ||
                string.IsNullOrEmpty(subscriptionId) ||
                !Guid.TryParse(subscriptionId, out var parsedSubscriptionId) ||
                parsedSubscriptionId == Guid.Empty)
            {
                await _logger.LogTraceAsync("Missing or invalid subscriptionId in PaymentIntent metadata", "Extract subscription ID from metadata", LogLevel.Error, true);
                return ResultWrapper.Failure(FailureReason.ValidationError, "Missing or invalid subscriptionId in PaymentIntent metadata");
            }

            // Extract user ID from metadata
            if (!metadata.TryGetValue("userId", out var userId) ||
                string.IsNullOrEmpty(userId) ||
                !Guid.TryParse(userId, out var parsedUserId) ||
                parsedUserId == Guid.Empty)
            {
                await _logger.LogTraceAsync("Missing or invalid userId in PaymentIntent metadata", "Extract user ID from metadata", LogLevel.Error, true);
                return ResultWrapper.Failure(FailureReason.ValidationError, "Missing userId in PaymentIntent metadata");
            }

            using var MetadataScope = _logger.EnrichScope(
                ("SubscriptionId", subscriptionId),
                ("UserId", userId)
            );

            _logger.EnrichScope(new
                ("UserId", userId),
                ("SubsriptionId", subscriptionId)
            );

            try
            {
                var processResult = await _paymentService.ProcessPaymentFailedAsync(new PaymentIntentRequest
                {
                    UserId = parsedUserId.ToString(),
                    SubscriptionId = parsedSubscriptionId.ToString(),
                    Provider = "Stripe",
                    InvoiceId = paymentIntent.InvoiceId,
                    PaymentId = paymentIntent.Id,
                    Currency = paymentIntent.Currency,
                    Amount = paymentIntent.Amount,
                    Status = paymentIntent.Status,
                    LastPaymentError = paymentIntent.LastPaymentError.Message,
                });

                if (processResult == null || !processResult.IsSuccess)
                {
                    throw new PaymentApiException($"Failed to process payment.failed event: {processResult?.ErrorMessage ?? "Process result returned null"}",
                        "Stripe",
                        paymentIntent.Id);
                }

                _logger.LogInformation("Successfully processed subscription {SubscriptionId} due to Stripe deletion",
                    subscriptionId);

                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError("Error handling payment.failed event: {ErrorMessage}", ex.Message);
                return ResultWrapper.FromException(ex);
            }
        }
        private async Task<ResultWrapper> HandleSetupIntentSucceededAsync(Stripe.Event stripeEvent)
        {
            using var Scope = _logger.BeginScope("StripeWebhookHandler => HandleSetupIntentSucceededAsync");

            var setupIntent = stripeEvent.Data.Object as Stripe.SetupIntent;
            if (setupIntent == null)
            {
                _logger.LogWarning("Invalid event data: Expected SetupIntent object");
                return ResultWrapper.Failure(FailureReason.ValidationError, "Invalid event data: Expected SetupIntent object");
            }

            using var SetupIntentScope = _logger.EnrichScope(
                ("SetupIntentId", setupIntent.Id)
            );

            var metadata = setupIntent.Metadata;

            // Extract subscription ID from metadata
            if (metadata == null ||
                !metadata.TryGetValue("subscriptionId", out var subscriptionId) ||
                string.IsNullOrEmpty(subscriptionId) ||
                !Guid.TryParse(subscriptionId, out var parsedSubscriptionId))
            {
                await _logger.LogTraceAsync("Missing or invalid subscriptionId in SetupIntent metadata", "Extract subscription ID from metadata", LogLevel.Error, true);
                return ResultWrapper.Failure(FailureReason.ValidationError, "Missing or invalid subscriptionId in SetupIntent metadata");
            }

            // Extract user ID from metadata
            if (!metadata.TryGetValue("userId", out var userId) ||
                string.IsNullOrEmpty(userId))
            {
                await _logger.LogTraceAsync("Missing or invalid userId in SetupIntent metadata", "Extract user ID from metadata", LogLevel.Error, true);
                return ResultWrapper.Failure(FailureReason.ValidationError, "Missing userId in SetupIntent metadata");
            }

            using var MetadataScope = _logger.EnrichScope(
                ("SubscriptionId", subscriptionId),
                ("UserId", userId)
            );

            try
            {
                var processResult = await _paymentService.ProcessSetupIntentSucceededAsync(parsedSubscriptionId);

                if (processResult == null || !processResult.IsSuccess)
                {
                    throw new PaymentApiException($"Failed to process setup_intent.succeeded event: {processResult?.ErrorMessage ?? "Process result returned null"}",
                        "Stripe",
                        setupIntent.Id);
                }

                return ResultWrapper.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError("Error handling setup_intent.succeeded event: {ErrorMessage}", ex.Message);
                return ResultWrapper.FromException(ex);
            }
        }
    }
}