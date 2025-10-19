using Application.Contracts.Requests.Payment;
using Application.Interfaces;
using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Application.Interfaces.Payment;
using Application.Interfaces.Subscription;
using Domain.Constants;
using Domain.Constants.Logging;
using Domain.Constants.Subscription;
using Domain.DTOs;
using Domain.Exceptions;
using Domain.Models.Subscription;
using Infrastructure.Flows.Payment;
using Infrastructure.Flows.Subscription;
using Infrastructure.Services.FlowEngine.Core.Interfaces;
using Infrastructure.Services.FlowEngine.Engine;
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
        private readonly IFlowEngineService _flowEngineService;
        private readonly ILoggingService _logger;
        private readonly IResilienceService _resilienceService;
        private readonly IServiceProvider _serviceProvider;
        private readonly IUserService _userService;

        public StripeWebhookHandler(
            IPaymentService paymentService,
            IEventService eventService,
            IFlowEngineService flowEngineService,
            ISubscriptionService subscriptionService,
            IIdempotencyService idempotencyService,
            ILoggingService logger,
            IResilienceService resilienceService,
            IServiceProvider serviceProvider,
            IUserService userService)
        {
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _flowEngineService = flowEngineService ?? throw new ArgumentNullException(nameof(flowEngineService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        }

        /// <summary>
        /// Handles a Stripe event
        /// </summary>
        public async Task<ResultWrapper> HandleStripeEventAsync(object stripeEventObject, string correlationId)
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

                // Handle different event types
                switch (stripeEvent.Type)
                {
                    case "checkout.session.completed":
                        return await HandleCheckoutSessionCompletedAsync(stripeEvent, correlationId);
                    case "invoice.paid":
                        return await HandleInvoicePaidAsync(stripeEvent, correlationId);
                    case "setup_intent.succeeded":
                        return await HandleSetupIntentSucceededAsync(stripeEvent, correlationId);
                    case "payment_intent.payment_failed":
                        return await HandlePaymentFailedAsync(stripeEvent, correlationId);
                    case "customer.subscription.deleted":
                        return await HandleSubscriptionDeletedAsync(stripeEvent, correlationId);
                    case "customer.subscription.paused":
                        return await HandleSubscriptionPausedAsync(stripeEvent, correlationId);
                    case "customer.subscription.resumed":
                        return await HandleSubscriptionResumedAsync(stripeEvent, correlationId);
                    default:
                        _logger.LogInformation("Unhandled Stripe event type: {EventType}", stripeEvent.Type);
                        break;
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
        /// Handles the checkout.session.completed event with resilience patterns
        /// </summary>
        private async Task<ResultWrapper> HandleCheckoutSessionCompletedAsync(Stripe.Event stripeEvent, string correlationId)
        {
            #region Validation
            if (stripeEvent.Data.Object is not Stripe.Checkout.Session session)
            {
                _logger.LogWarning("Invalid event data: Expected Session object");
                return ResultWrapper.Failure(FailureReason.ValidationError, "Invalid event data: Expected Session object");
            }

            // Validation logic remains the same...
            using var SessionScope = _logger.EnrichScope(("SessionId", session.Id));

            var metadata = session.Metadata;
            if (metadata == null ||
                !metadata.TryGetValue("subscriptionId", out var subscriptionId) ||
                string.IsNullOrEmpty(subscriptionId) ||
                !Guid.TryParse(subscriptionId, out var parsedSubscriptionId))
            {
                await _logger.LogTraceAsync("Missing or invalid subscriptionId in Session metadata", "Extract subscription ID from metadata", LogLevel.Error, true);
                return ResultWrapper.Failure(FailureReason.ValidationError, "Missing or invalid subscriptionId in Session metadata");
            }

            if (!metadata.TryGetValue("userId", out var userId) ||
                string.IsNullOrEmpty(userId))
            {
                await _logger.LogTraceAsync("Missing or invalid userId in Session metadata", "Extract user ID from metadata", LogLevel.Error, true);
                return ResultWrapper.Failure(FailureReason.ValidationError, "Missing userId in Session metadata");
            }

            if (!metadata.TryGetValue("correlationId", out var parentCorrelationId) ||
                string.IsNullOrEmpty(parentCorrelationId))
            {
                await _logger.LogTraceAsync("Missing or invalid correlationId in Session metadata", "Extract correlation ID from metadata", LogLevel.Warning, false);
            }
            #endregion

            // Use the resilience wrapper
            return await ExecuteWithResilienceAsync(
                "HandleCheckoutSessionCompletedAsync(Stripe.Event stripeEvent, string? correlationId = null)",
                new Dictionary<string, object>
                {
                    ["SessionId"] = session.Id,
                    ["SubscriptionId"] = subscriptionId,
                    ["UserId"] = userId,
                    ["EventId"] = stripeEvent.Id,
                    ["EventType"] = stripeEvent.Type
                },
                async () =>
                {
                    await _flowEngineService.PublishEventAsync("CheckoutSessionCompleted", session, parentCorrelationId ?? correlationId);
                });
        }

        /// <summary>
        /// Handles the payment_intent.succeeded event
        /// </summary>
        private async Task<ResultWrapper> HandleInvoicePaidAsync(Stripe.Event stripeEvent, string correlationId)
        {
            #region Validations
            _logger.LogInformation($"Handling stripe event invoice.paid");

            if (stripeEvent.Data.Object is not Stripe.Invoice invoice)
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

            var subscriptionResult = await _subscriptionService.GetByIdAsync(parsedSubscriptionId);

            if (subscriptionResult == null || !subscriptionResult.IsSuccess)
            {
                return ResultWrapper.NotFound("Subscription", subscriptionId);
            }

            // Extract user ID from metadata
            if (!metadata.TryGetValue("userId", out var userId) ||
                string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out Guid parsedUserId))
            {
                await _logger.LogTraceAsync("Missing or invalid userId in Invoice metadata", "Extract user ID from metadata", LogLevel.Error, true);
                return ResultWrapper.Failure(FailureReason.ValidationError, "Missing userId in Invoice metadata");
            }

            if (subscriptionResult.Data!.UserId != parsedUserId)
            {
                return ResultWrapper.Unauthorized("User ID in metadata does not match subscription user ID.");
            }
            #endregion

            // Use the resilience wrapper
            return await ExecuteWithResilienceAsync(
                "HandleInvoicePaidAsync(Stripe.Event stripeEvent)",
                new Dictionary<string, object>
                {
                    ["InvoiceId"] = invoice.Id,
                    ["SubscriptionId"] = subscriptionResult.Data,
                    ["UserId"] = userId,
                    ["EventId"] = stripeEvent.Id,
                    ["EventType"] = stripeEvent.Type,
                    ["ChargeId"] = invoice.ChargeId,
                    ["PaymentIntentId"] = invoice.PaymentIntentId,
                    ["Amount"] = invoice.AmountPaid,
                    ["Currency"] = invoice.Currency
                },
                async () =>
                {
                    var user = await _userService.GetByIdAsync(parsedUserId);

                    if(user == null)
                    {
                        throw new ResourceNotFoundException("User", parsedUserId.ToString());
                    }

                    var invoiceDto = new InvoiceRequest
                    {
                        Id = invoice.Id,
                        Provider = "Stripe",
                        ChargeId = invoice.ChargeId,
                        PaymentIntentId = invoice.PaymentIntentId,
                        UserId = invoice.SubscriptionDetails.Metadata["userId"],
                        SubscriptionId = invoice.SubscriptionDetails.Metadata["subscriptionId"],
                        ProviderSubscripitonId = invoice.SubscriptionId,
                        Amount = invoice.AmountPaid,
                        Currency = invoice.Currency,
                        Status = invoice.Status,
                        Metadata = invoice.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value)
                    };

                    // First check if there's already a paused SubscriptionCreationFlow pending for this invoice
                    var existingFlow = _flowEngineService.GetPausedFlows<SubscriptionCreationFlow>()
                    .Find( f => 
                        f.Context.Definition.ActiveResumeConfig != null &&
                        f.Definition.ActiveResumeConfig.EventTriggers != null &&
                        f.Definition.ActiveResumeConfig.EventTriggers.Any(et => et.EventType == "InvoicePaid") &&
                        f.Context.HasData("SubscriptionId") &&
                        f.Context.GetData<Guid>("SubscriptionId") == parsedSubscriptionId);

                    string ? parentCorrelationId = null;

                    if (existingFlow != null)
                    {
                        parentCorrelationId = existingFlow.State.CorrelationId;
                    }

                    Flow.Builder(_serviceProvider)
                    .ForUser(userId, user.Email)
                    .WithData("InvoiceRequest", invoiceDto)
                    .WithData("Subscription", subscriptionResult.Data)
                    .WithCorrelation(parentCorrelationId ?? correlationId)
                    .Build<PaymentProcessingFlow>()
                    .ExecuteAsync();

                    await _flowEngineService.PublishEventAsync("InvoicePaid", invoice, parentCorrelationId ?? correlationId);
                });
        }

        /// <summary>
        /// Handles the customer.subscription.deleted event
        /// </summary>
        private async Task<ResultWrapper> HandleSubscriptionDeletedAsync(Stripe.Event stripeEvent, string correlationId)
        {
            var stripeSubscription = stripeEvent.Data.Object as Stripe.Subscription;
            if (stripeSubscription == null)
            {
                _logger.LogWarning("Invalid event data: Expected Subscription object");
                return ResultWrapper.Failure(FailureReason.ValidationError, "Invalid event data: Expected Subscription object");
            }

            // Use the resilience wrapper
            return await ExecuteWithResilienceAsync(
                "HandleSubscriptionDeletedAsync(Stripe.Event stripeEvent)",
                new Dictionary<string, object>
                {
                    ["StripeSubscriptionId"] = stripeSubscription.Id,
                    ["EventId"] = stripeEvent.Id,
                    ["EventType"] = stripeEvent.Type,
                },
                async () =>
                {
                    // Find our subscription
                    var filter = Builders<SubscriptionData>.Filter.Eq(
                        s => s.ProviderSubscriptionId, stripeSubscription.Id);

                    var subscriptionResult = await _subscriptionService.GetOneAsync(filter);

                    if (subscriptionResult == null || !subscriptionResult.IsSuccess)
                    {
                        _logger.LogWarning("No subscription found with provider subscription ID: {StripeSubscriptionId}",
                            stripeSubscription.Id);
                        throw new ResourceNotFoundException($"No subscription found with Stripe subscription ID: {stripeSubscription.Id}", stripeSubscription.Id);
                    }

                    var subscription = subscriptionResult.Data;

                    // Mark subscription as cancelled
                    var updateFields = new Dictionary<string, object>
                    {
                        ["Status"] = SubscriptionStatus.Canceled,
                        ["IsCancelled"] = true
                    };

                    var updateResult = await _subscriptionService.UpdateAsync(subscription.Id, updateFields);

                    if (updateResult == null || !updateResult.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to cancel subscription: {updateResult?.ErrorMessage ?? "No documents modified"}");
                    }

                    _logger.LogInformation("Successfully cancelled subscription {SubscriptionId} due to Stripe deletion",
                        subscription.Id);
                });
        }

        /// <summary>
        /// Handles the customer.subscription.paused event
        /// </summary>
        private async Task<ResultWrapper> HandleSubscriptionPausedAsync(Stripe.Event stripeEvent, string correlationId)
        {
            var stripeSubscription = stripeEvent.Data.Object as Stripe.Subscription;
            if (stripeSubscription == null)
            {
                _logger.LogWarning("Invalid event data: Expected Subscription object");
                return ResultWrapper.Failure(FailureReason.ValidationError, "Invalid event data: Expected Subscription object");
            }

            // Use the resilience wrapper
            return await ExecuteWithResilienceAsync(
                "HandleSubscriptionPausedAsync(Stripe.Event stripeEvent)",
                new Dictionary<string, object>
                {
                    ["StripeSubscriptionId"] = stripeSubscription.Id,
                    ["EventId"] = stripeEvent.Id,
                    ["EventType"] = stripeEvent.Type,
                },
                async () =>
                {
                    // Extract subscription ID from metadata
                    var metadata = stripeSubscription.Metadata;
                    if (metadata == null ||
                        !metadata.TryGetValue("subscriptionId", out var subscriptionId) ||
                        string.IsNullOrEmpty(subscriptionId) ||
                        !Guid.TryParse(subscriptionId, out var parsedSubscriptionId))
                    {
                        _logger.LogWarning("Missing or invalid subscriptionId in Stripe subscription metadata for subscription {StripeSubscriptionId}",
                            stripeSubscription.Id);

                        // Try to find subscription by provider ID as fallback
                        var filter = Builders<SubscriptionData>.Filter.Eq(
                            s => s.ProviderSubscriptionId, stripeSubscription.Id);

                        var subscriptionResult = await _subscriptionService.GetOneAsync(filter);

                        if (subscriptionResult == null || !subscriptionResult.IsSuccess || subscriptionResult.Data == null)
                        {
                            throw new ResourceNotFoundException($"No subscription found with Stripe subscription ID: {stripeSubscription.Id}", stripeSubscription.Id);
                        }

                        parsedSubscriptionId = subscriptionResult.Data.Id;
                    }

                    _logger.LogInformation("Processing Stripe subscription paused event for subscription {SubscriptionId}",
                        parsedSubscriptionId);

                    // Call the OnPauseAsync method that only updates domain records
                    var pauseResult = await _subscriptionService.OnPauseAsync(parsedSubscriptionId);

                    if (!pauseResult.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to pause subscription {parsedSubscriptionId}: {pauseResult.ErrorMessage}");
                    }

                    _logger.LogInformation("Successfully paused subscription {SubscriptionId} due to Stripe event",
                        parsedSubscriptionId);
                });
        }

        /// <summary>
        /// Handles the customer.subscription.resumed event
        /// </summary>
        private async Task<ResultWrapper> HandleSubscriptionResumedAsync(Stripe.Event stripeEvent, string correlationId)
        {
            var stripeSubscription = stripeEvent.Data.Object as Stripe.Subscription;
            if (stripeSubscription == null)
            {
                _logger.LogWarning("Invalid event data: Expected Subscription object");
                return ResultWrapper.Failure(FailureReason.ValidationError, "Invalid event data: Expected Subscription object");
            }

            // Use the resilience wrapper
            return await ExecuteWithResilienceAsync(
                "HandleSubscriptionResumedAsync(Stripe.Event stripeEvent)",
                new Dictionary<string, object>
                {
                    ["StripeSubscriptionId"] = stripeSubscription.Id,
                    ["EventId"] = stripeEvent.Id,
                    ["EventType"] = stripeEvent.Type,
                },
                async () =>
                {
                    // Extract subscription ID from metadata
                    var metadata = stripeSubscription.Metadata;
                    if (metadata == null ||
                        !metadata.TryGetValue("subscriptionId", out var subscriptionId) ||
                        string.IsNullOrEmpty(subscriptionId) ||
                        !Guid.TryParse(subscriptionId, out var parsedSubscriptionId))
                    {
                        _logger.LogWarning("Missing or invalid subscriptionId in Stripe subscription metadata for subscription {StripeSubscriptionId}",
                            stripeSubscription.Id);

                        // Try to find subscription by provider ID as fallback
                        var filter = Builders<SubscriptionData>.Filter.Eq(
                            s => s.ProviderSubscriptionId, stripeSubscription.Id);

                        var subscriptionResult = await _subscriptionService.GetOneAsync(filter);

                        if (subscriptionResult == null || !subscriptionResult.IsSuccess || subscriptionResult.Data == null)
                        {
                            throw new ResourceNotFoundException($"No subscription found with Stripe subscription ID: {stripeSubscription.Id}", stripeSubscription.Id);
                        }

                        parsedSubscriptionId = subscriptionResult.Data.Id;
                    }

                    _logger.LogInformation("Processing Stripe subscription resumed event for subscription {SubscriptionId}",
                        parsedSubscriptionId);

                    // Call the OnResumeAsync method that only updates domain records
                    var resumeResult = await _subscriptionService.OnResumeAsync(parsedSubscriptionId);

                    if (!resumeResult.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to resume subscription {parsedSubscriptionId}: {resumeResult.ErrorMessage}");
                    }

                    _logger.LogInformation("Successfully resumed subscription {SubscriptionId} due to Stripe event",
                        parsedSubscriptionId);
                });
        }

        private async Task<ResultWrapper> HandlePaymentFailedAsync(Stripe.Event stripeEvent, string correlationId)
        {
            var paymentIntent = stripeEvent.Data.Object as Stripe.PaymentIntent;
            if (paymentIntent == null)
            {
                _logger.LogWarning("Invalid event data: Expected PaymentIntent object");
                return ResultWrapper.Failure(FailureReason.ValidationError, "Invalid event data: Expected PaymentIntent object");
            }

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

            // Use the resilience wrapper
            return await ExecuteWithResilienceAsync(
                "HandlePaymentFailedAsync(Stripe.Event stripeEvent)",
                new Dictionary<string, object>
                {
                    ["EventType"] = stripeEvent.Type,
                    ["PaymentIntentId"] = paymentIntent.Id,
                    ["SubscriptionId"] = subscriptionId,
                    ["UserId"] = userId,
                    ["EventId"] = stripeEvent.Id,
                    ["InvoiceId"] = paymentIntent.InvoiceId,
                    ["Amount"] = paymentIntent.Amount,
                    ["Currency"] = paymentIntent.Currency,
                    ["Status"] = paymentIntent.Status,
                    ["LastPaymentError"] = paymentIntent.LastPaymentError?.Message
                },
                async () =>
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
                        LastPaymentError = paymentIntent.LastPaymentError?.Message,
                    });

                    if (processResult == null || !processResult.IsSuccess)
                    {
                        throw processResult == null
                            ? new ServiceUnavailableException("PaymentService")
                            : new PaymentApiException($"Failed to process payment.failed event: {processResult.ErrorMessage ?? "Process result returned null"}", "Stripe", paymentIntent.Id);
                    }

                    _logger.LogInformation("Successfully processed payment failure for subscription {SubscriptionId}",
                        subscriptionId);
                });
        }
        private async Task<ResultWrapper> HandleSetupIntentSucceededAsync(Stripe.Event stripeEvent, string correlationId)
        {
            var setupIntent = stripeEvent.Data.Object as Stripe.SetupIntent;
            if (setupIntent == null)
            {
                _logger.LogWarning("Invalid event data: Expected SetupIntent object");
                return ResultWrapper.Failure(FailureReason.ValidationError, "Invalid event data: Expected SetupIntent object");
            }

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

            // Use the resilience wrapper
            return await ExecuteWithResilienceAsync(
                "HandleSetupIntentSucceededAsync(Stripe.Event stripeEvent)",
                new Dictionary<string, object>
                {
                    ["SetupIntentId"] = setupIntent.Id,
                    ["SubscriptionId"] = subscriptionId,
                    ["UserId"] = userId,
                    ["EventId"] = stripeEvent.Id,
                    ["EventType"] = stripeEvent.Type,
                    ["Status"] = setupIntent.Status,
                    ["PaymentMethodId"] = setupIntent.PaymentMethodId,
                    ["CustomerId"] = setupIntent.CustomerId
                },
                async () =>
                {
                    var processResult = await _paymentService.ProcessSetupIntentSucceededAsync(parsedSubscriptionId);

                    if (processResult == null || !processResult.IsSuccess)
                    {
                        throw processResult == null
                            ? new ServiceUnavailableException("PaymentService")
                            : new PaymentApiException($"Failed to process setup_intent.succeeded event: {processResult.ErrorMessage ?? "Process result returned null"}", "Stripe", setupIntent.Id);
                    }

                    _logger.LogInformation("Successfully processed setup intent success for subscription {SubscriptionId}",
                        subscriptionId);
                });
        }

        /// <summary>
        /// Non-generic version for operations that don't return data
        /// </summary>
        private async Task<ResultWrapper> ExecuteWithResilienceAsync(
            string operationName,
            Dictionary<string, object> context,
            Func<Task> operation)
        {
            return await _resilienceService.CreateBuilder(
                new Domain.DTOs.Logging.Scope
                {
                    NameSpace = "Infrastructure.Services.Payment",
                    FileName = "StripeWebhookHandler",
                    OperationName = operationName,
                    State = context ?? [],
                    LogLevel = LogLevel.Error
                },
                operation)
                .WithContext("PaymentProvider", "Stripe")
                .ExecuteAsync();
        }
    }
}