using Application.Contracts.Requests.Subscription;
using Application.Contracts.Responses.Subscription;
using Application.Interfaces;
using Application.Interfaces.Payment;
using Application.Interfaces.Subscription;
using Domain.Constants.Subscription;
using Domain.DTOs.Payment;
using Domain.Exceptions;
using Domain.Models.Dashboard;
using Domain.Models.Payment;
using FluentValidation;
using Infrastructure.Hubs;
using Infrastructure.Services;
using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Infrastructure.Flows.Subscription
{
    /// <summary>
    /// Comprehensive flow for creating subscriptions with validation, payment setup, and activation
    /// </summary>
    public class SubscriptionCreationFlow : FlowDefinition
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly IPaymentService _paymentService;
        private readonly IValidator<SubscriptionCreateRequest> _subscriptionValidator;
        private readonly IValidator<CheckoutSessionRequest> _checkoutValidator;
        private readonly INotificationService _notificationService;
        private readonly IIdempotencyService _idempotencyService;
        private readonly ILogger<SubscriptionCreationFlow> _logger;
        private readonly IDashboardService _dashboardService;

        public SubscriptionCreationFlow(
            ISubscriptionService subscriptionService,
            IPaymentService paymentService,
            IValidator<SubscriptionCreateRequest> validator,
            INotificationService notificationService,
            IIdempotencyService idempotencyService,
            ILogger<SubscriptionCreationFlow> logger,
            IDashboardService dashboardService)
        {
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _subscriptionValidator = validator ?? throw new ArgumentNullException(nameof(validator));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dashboardService = dashboardService ?? throw new ArgumentNullException(nameof(dashboardService));
        }

        protected override void ConfigureMiddleware()
        {
            // TO-DO: UseMiddleware<IdempotencyMiddleware>();
        }

        protected override void DefineSteps()
        {
            // Step 1: Validate request and check idempotency
            _builder.Step("ValidateSubscriptionRequest")
                .RequiresData<SubscriptionCreateRequest>("Request")
                .RequiresData<string>("IdempotencyKey")
                .Execute(async context =>
                {
                    var request = context.GetData<SubscriptionCreateRequest>("Request");

                    _logger.LogInformation("Starting subscription creation for user {UserId}", request.UserId);

                    // Validate request
                    var validationResult = await _subscriptionValidator.ValidateAsync(request);
                    if (!validationResult.IsValid)
                    {
                        var errors = validationResult.Errors
                            .GroupBy(e => e.PropertyName)
                            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

                        _logger.LogWarning("Validation failed for subscription creation: {ValidationErrors}",
                            string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)));

                        return StepResult.Failure("Validation failed: " + string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage)));
                    }

                    return StepResult.Success("Request validated successfully");
                })
                .Build();

            // Step 2: Handle idempotent requests (early exit)
            _builder.Step("CheckIdempotency")
                .After("ValidateSubscriptionRequest")
                .RequiresData<string>("IdempotencyKey")
                .Execute(async context =>
                {
                    var idempotencyKey = context.GetData<string>("IdempotencyKey");
                    var (resultExists, existingResult) = await _idempotencyService.GetResultAsync<SubscriptionCreateResponse>(idempotencyKey);

                    if (resultExists && existingResult != null)
                    {
                        _logger.LogInformation("Idempotent request detected for key {IdempotencyKey}, returning existing subscription {SubscriptionId}",
                            idempotencyKey, existingResult);

                        context.SetData("IsIdempotentRequest", true);
                        context.SetData("SubscriptionResponse", existingResult);

                        return StepResult.Success("Idempotent request - returning existing result", existingResult);
                    }

                    context.SetData("IsIdempotentRequest", false);

                    return StepResult.Success("No existing idempotent result found");
                })
                .WithStaticBranches(builder => builder
                    .Branch("IdempotentRequest")
                    .When(context => context.GetData<bool>("IsIdempotentRequest"))
                    .WithSteps(builder => builder
                        .Step("SkipToEnd")
                        .Execute( async context => StepResult.Success($"Idempotent request - Jumping to step PrepareResponse"))
                        .JumpTo("PrepareResponse")
                        .Build())
                    .Build())
                .Build();

            // Step 3: Create subscription entity
            _builder.Step("CreateSubscriptionEntity")
                .After("CheckIdempotency")
                .OnlyIf(context => !context.GetData<bool>("IsIdempotentRequest"))
                .RequiresData<SubscriptionCreateRequest>("Request")
                .Execute(async context =>
                {
                    var request = context.GetData<SubscriptionCreateRequest>("Request");

                    _logger.LogInformation("Creating subscription entity for user {UserId}", request.UserId);

                    // Create the subscription
                    var subscriptionCreateResult = await _subscriptionService.CreateAsync(request);

                    if (subscriptionCreateResult == null || !subscriptionCreateResult.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to create subscription: {subscriptionCreateResult?.ErrorMessage}");
                    }

                    var subscriptionId = subscriptionCreateResult.Data.AffectedIds.First();

                    _logger.LogInformation("Successfully created subscription {SubscriptionId} for user {UserId}",
                        subscriptionId, request.UserId);

                    context.SetData("SubscriptionId", subscriptionId);

                    return StepResult.Success($"Subscription entity with ID {subscriptionId} created", subscriptionId);
                })
                .Critical()
                .Build();

            // Step 4: Store idempotency result
            _builder.Step("StoreIdempotencyResult")
                .After("CreateSubscriptionEntity")
                .RequiresData<string>("IdempotencyKey")
                .RequiresData<Guid>("SubscriptionId")
                .Execute(async context =>
                {
                    var idempotencyKey = context.GetData<string>("IdempotencyKey");
                    var subscriptionId = context.GetData<Guid>("SubscriptionId");

                    await _idempotencyService.StoreResultAsync(idempotencyKey, subscriptionId);

                    return StepResult.Success($"Idempotency result stored with key {idempotencyKey}");
                })
                .Build();

            // Step 5: Update subscription state to PendingCheckout
            _builder.Step("UpdateSubscriptionState")
                .After("CreateSubscriptionEntity")
                .RequiresData<Guid>("SubscriptionId")
                .Execute(async context =>
                {
                    var subscriptionId = context.GetData<Guid>("SubscriptionId");

                    var updateResult = await _subscriptionService.UpdateAsync(subscriptionId, new Dictionary<string, object>
                    {
                        ["State"] = SubscriptionState.PendingCheckout
                    });
                    if (!updateResult.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to update subscription {subscriptionId} state to PendingPayment: {updateResult.ErrorMessage}");
                    }
                    return StepResult.Success($"Subscription {subscriptionId} state updated to PendingPayment");
                })
                .AllowFailure()
                .InParallel()
                .Critical()
                .Build();

            // Step 6: Setup payment method and create checkout session
            _builder.Step("SetupPaymentSession")
                .After("CreateSubscriptionEntity")
                .RequiresData<SubscriptionCreateRequest>("Request")
                .RequiresData<Guid>("SubscriptionId")
                .Execute(async context =>
                {
                    var request = context.GetData<SubscriptionCreateRequest>("Request");
                    var subscriptionId = context.GetData<Guid>("SubscriptionId");

                    _logger.LogInformation("Setting up payment session for subscription {SubscriptionId}", subscriptionId);

                    // Create checkout session for payment
                    var checkoutRequest = new CreateCheckoutSessionDto
                    {
                        SubscriptionId = subscriptionId.ToString(),
                        UserId = request.UserId,
                        UserEmail = context.Flow.State.UserEmail,
                        Amount = request.Amount,
                        Currency = request.Currency,
                        IsRecurring = true,
                        Interval = request.Interval,
                        ReturnUrl = $"{request.SuccessUrl}?subscription_id={subscriptionId}&amount={request.Amount}&currency={request.Currency}",
                        CancelUrl = $"{request.CancelUrl}?subscription_id=${subscriptionId}",
                        Provider = "Stripe",
                        Metadata = new Dictionary<string, string>
                        {
                            ["correlationId"] = context.Flow.State.CorrelationId,
                            ["userId"] = request.UserId,
                            ["subscriptionId"] = subscriptionId.ToString()
                        }
                    };

                    var sessionResult = await _paymentService.CreateCheckoutSessionAsync(checkoutRequest);

                    if (sessionResult == null || !sessionResult.IsSuccess)
                    {
                        throw new PaymentApiException($"Failed to create checkout session: {sessionResult?.ErrorMessage}", "Stripe");
                    }

                    var session = sessionResult.Data;

                    context.SetData("CheckoutSession", session);

                    return StepResult.Success($"Payment session created with ID {session.Id}", session);
                })
                .InParallel()
                .Critical()
                .Build();

            // Step 7: Wait for payment completion (pause flow)
            _builder.Step("AwaitPaymentCompletion")
                .After("SetupPaymentSession")
                .RequiresData<SessionDto>("CheckoutSession")
                .CanPause(async context =>
                {
                    _logger.LogInformation("Pausing flow to await checkout completion");

                    var session = context.GetData<SessionDto>("CheckoutSession");

                    return PauseCondition.Pause(
                        PauseReason.WaitingForPayment,
                        "Waiting for checkout completion",
                        session
                        );
                })
                .ResumeOn(resume =>
                {
                    // Resume when checkout session is completed
                    resume.OnEvent("CheckoutSessionCompleted", (context, eventData) =>
                    {
                        if (eventData == null || eventData is not Stripe.Checkout.Session session)
                            return false;

                        var subscriptionId = context.GetData<Guid>("SubscriptionId");

                        // Check if this event is for our subscription
                        var canResume = session.Metadata.ContainsKey("subscriptionId") &&
                               session.Metadata["subscriptionId"] == subscriptionId.ToString();

                        if (canResume)
                        {
                            context.SetData("Session", session);
                            if (context.HasData("Invoice")) return true; // Resume if we also received the invoice.paid event
                        }

                        return false;
                    });

                    // Resume when invoice.paid event is received
                    resume.OnEvent("InvoicePaid", (context, eventData) =>
                    {
                        if (eventData == null || eventData is not Stripe.Invoice invoice)
                            return false;

                        var subscriptionId = context.GetData<Guid>("SubscriptionId");
                        var invoiceId = context.GetData<string>("InvoiceId");

                        // Check if this event is for our subscription
                        var canResume = invoice.SubscriptionDetails.Metadata?.ContainsKey("subscriptionId") == true &&
                                invoice.SubscriptionDetails.Metadata["subscriptionId"] == subscriptionId.ToString();

                        if (canResume)
                        {
                            context.SetData("Invoice", invoice);
                            if (context.HasData("Session")) return true; // Resume if we also received the checkout.session.completed event
                        }

                        return false;
                    });

                    // Auto-resume after timeout (e.g., for abandoned payments)
                    resume.WhenCondition(async context =>
                    {
                        var pausedAt = context.State.PausedAt;
                        bool hasReachedtimeout = pausedAt.HasValue && 
                               DateTime.UtcNow.Subtract(pausedAt.Value) > TimeSpan.FromMinutes(15);

                        if (hasReachedtimeout)
                        {
                            context.SetData("CheckoutTimedOut", true);
                        }
                        
                        return hasReachedtimeout;
                    });

                    // Allow manual resume by admins
                    resume.AllowManual(["ADMIN"]);
                })
                .Execute(async context =>
                {
                    if (context.TryGetData("CheckoutTimedOut", out bool chekoutTimedOut) && chekoutTimedOut)
                    {
                        throw new TimeoutException("Checkout session was not completed in time.");
                    }

                    // This executes after resume
                    var subscriptionId = context.GetData<Guid>("SubscriptionId");

                    var checkoutSession = context.GetData<SessionDto>("CheckoutSession");
                    var session = context.GetData<Stripe.Checkout.Session>("Session");

                    // Update our subscription with the active status
                    var updatedFields = new Dictionary<string, object>
                    {
                        ["Provider"] = checkoutSession.Provider,
                        ["ProviderSubscriptionId"] = checkoutSession.SubscriptionId,
                        ["Status"] = SubscriptionStatus.Active,
                        ["State"] = SubscriptionState.ProcessingInvoice,
                    };

                    var updateResult = await _subscriptionService.UpdateAsync(subscriptionId, updatedFields);

                    if (!updateResult.IsSuccess || updateResult.Data.Documents.FirstOrDefault() == null)
                    {
                        throw new DatabaseException($"Failed to update subscription {subscriptionId}: {updateResult.ErrorMessage}");
                    }

                    var subscription = updateResult.Data.Documents.FirstOrDefault();

                    context.SetData("InvoiceId", session.InvoiceId);
                    context.SetData("Subscription", subscription);

                    await _dashboardService.InvalidateCacheAndPush(subscription.UserId);

                    return StepResult.Success($"Updated and activated subscription {subscriptionId} with session data. Notification sent to user ID {context.State.UserId}");
                })
                .Build();

            // Step 8: Send notification to user
            _builder.Step("NotifyUser")
                .After("AwaitPaymentCompletion")
                .RequiresData<SubscriptionCreateRequest>("Request")
                .RequiresData<Guid>("SubscriptionId")
                .Execute(async context =>
                {
                    var request = context.GetData<SubscriptionCreateRequest>("Request");
                    var subscriptionId = context.GetData<Guid>("SubscriptionId");
                    var paymentCompleted = context.GetData<bool>("PaymentCompleted");

                    var message = $"Your {request.Interval} {request.Amount} {request.Currency} subscription has been activated!";

                    await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                    {
                        UserId = request.UserId,
                        Message = message,
                        IsRead = false
                    });

                    return StepResult.Success("User notification sent");
                })
                .InParallel()
                .AllowFailure() // Don't fail the entire flow if notification fails
                .Build();

            // Step 9: Prepare final response
            _builder.Step("PrepareResponse")
                .Execute(async context =>
                {
                    // Check if we already have a response from idempotent handling

                    var existingResponse = context.GetData<SubscriptionCreateResponse>("SubscriptionResponse");
                    if (existingResponse == null)
                    {
                        return StepResult.Success("Idempotent request - returning existing result", existingResponse);
                    }

                    var subscriptionId = context.GetData<Guid>("SubscriptionId");
                    var checkoutSession = context.HasData("CheckoutSession") 
                        ? context.GetData<SessionDto>("CheckoutSession") 
                        : null;

                    var response = new SubscriptionCreateResponse
                    {
                        Id = subscriptionId.ToString(),
                        CheckoutUrl = checkoutSession?.Url,
                        Status = context.GetData<bool>("PaymentCompleted") 
                            ? SubscriptionStatus.Active 
                            : SubscriptionStatus.Pending
                    };

                    context.SetData("SubscriptionResponse", response);

                    return StepResult.Success("Response prepared", response);
                })
                .Build();
        }
    }
}