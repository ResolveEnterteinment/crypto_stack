using Application.Contracts.Requests.Payment;
using Application.Interfaces;
using Application.Interfaces.Base;
using Application.Interfaces.Payment;
using Application.Interfaces.Subscription;
using Domain.Constants.Subscription;
using Domain.DTOs.Subscription;
using Domain.Events.Payment;
using Domain.Exceptions;
using Domain.Models.Payment;
using Domain.Models.Subscription;
using Infrastructure.Flows.Exchange;
using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Models;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Infrastructure.Flows.Payment
{
    public class PaymentProcessingFlow : FlowDefinition
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly IPaymentService _paymentService;
        private readonly IDashboardService _dashboardService;
        private readonly IEventService _eventService;
        private readonly ILogger<PaymentProcessingFlow> _logger;

        public PaymentProcessingFlow(
            ILogger<PaymentProcessingFlow> logger,
            ISubscriptionService subscriptionService,
            IPaymentService paymentService,
            IDashboardService dashboardService,
            IEventService eventService)
        {
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _dashboardService = dashboardService ?? throw new ArgumentNullException(nameof(dashboardService));
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        protected override void DefineSteps()
        {
            _builder.Step("ValidateInvoice")
                .RequiresData<InvoiceRequest>("InvoiceRequest")
                .Execute(async context =>
                {
                    var invoice = context.GetData<InvoiceRequest>("InvoiceRequest");

                    ValidateInvoiceRequest(invoice);

                    return StepResult.Success($"Invoice {invoice.Id} validated.");
                })
                .Build();

            _builder.Step("SetSubscripitonStateToProcessingInvoice")
                .After("ValidateInvoice")
                .Execute(async context =>
                {
                    var invoice = context.GetData<InvoiceRequest>("InvoiceRequest");

                    // Fetch subscription
                    var subWr = await _subscriptionService.GetOneAsync(
                        Builders<SubscriptionData>.Filter.Eq(s => s.Id, Guid.Parse(invoice.SubscriptionId)));

                    if (subWr == null || !subWr.IsSuccess || subWr.Data == null)
                        throw new SubscriptionFetchException($"Subscription with ID {invoice.SubscriptionId} not found");

                    var subscription = subWr.Data;

                    // Update our subscription with the active status
                    var updatedFields = new Dictionary<string, object>
                    {
                        ["State"] = SubscriptionState.ProcessingInvoice
                    };

                    var updateResult = await _subscriptionService.UpdateAsync(subscription.Id, updatedFields);

                    if (!updateResult.IsSuccess || updateResult.Data.Documents.FirstOrDefault() == null)
                    {
                        throw new DatabaseException($"Failed to set subscription {subscription.Id} state to ProcessingInvoice: {updateResult.ErrorMessage}");
                    }
                    context.SetData("Subscription", updateResult.Data.Documents.First());

                    await _dashboardService.InvalidateCacheAndPush(subscription.UserId);

                    return StepResult.Success($"Set Subscription ID {subscription.Id} state to ProcessingPayments.", updateResult.Data.Documents.First());
                })
                .InParallel()
                .Build();

            _builder.Step("PreparePayment")
                .After("ValidateInvoice")
                .RequiresData<InvoiceRequest>("InvoiceRequest")
                .Execute(async context =>
                {
                    var invoice = context.GetData<InvoiceRequest>("InvoiceRequest");

                    // Check existing
                    var existingWr = await _paymentService.GetOneAsync(
                        Builders<PaymentData>.Filter.Eq(p => p.InvoiceId, invoice.Id));

                    if (existingWr != null && existingWr.Data != null)
                    {
                        context.SetData("Payment", existingWr.Data); // Pass existing payment data forward

                        return StepResult.Success($"Payment data with invoice ID {invoice.Id} already present.", existingWr.Data);
                    }

                    // Calculate amounts
                    var (total, fee, platformFee, net) = await _paymentService.CalculatePaymentFees(invoice);

                    var paymentData = new PaymentData
                    {
                        UserId = Guid.Parse(invoice.UserId),
                        SubscriptionId = Guid.Parse(invoice.SubscriptionId),
                        ProviderSubscriptionId = invoice.ProviderSubscripitonId,
                        Provider = invoice.Provider,
                        PaymentProviderId = invoice.PaymentIntentId,
                        InvoiceId = invoice.Id,
                        TotalAmount = total,
                        PaymentProviderFee = fee,
                        PlatformFee = platformFee,
                        NetAmount = net,
                        Currency = invoice.Currency,
                        Status = invoice.Status
                    };

                    context.SetData("Payment", paymentData); // Pass new payment
                    return StepResult.Success($"Payment data ready", paymentData);
                })
                .WithIdempotency()
                .InParallel()
                .Build();

            _builder.Step("CreatePaymentRecord")
                .After("PreparePayment")
                .RequiresData<PaymentData>("Payment")
                .Execute(async context =>
                {
                    var paymentData = context.GetData<PaymentData>("Payment");
                    // Update status to processing
                    var insertWr = await _paymentService.InsertAsync(paymentData!);

                    if (insertWr == null || !insertWr.IsSuccess)
                        throw new DatabaseException(insertWr?.ErrorMessage ?? "Insert result returned null");

                    await _eventService.PublishAsync(new PaymentReceivedEvent(paymentData!, context.State.Data.FromSafe()));

                    return StepResult.Success($"Payment record created.", insertWr.Data.Documents.FirstOrDefault());
                })
                .WithIdempotency()
                .Critical()
                .InParallel()
                .Triggers<UpdateSubscriptionPostPaymentFlow>(context => new Dictionary<string, object>()
                {
                    ["Payment"] = context.GetData<PaymentData>("Payment"),
                    ["Subscription"] = context.GetData<SubscriptionData>("Subscription")
                })
                .Build();

            _builder.Step("SetSubscriptionStateToAcquiringAssets")
                .Execute(async ctx =>
                {
                    var subscription = ctx.GetData<SubscriptionData>("Subscription");

                    var updatedFields = new Dictionary<string, object>
                    {
                        ["State"] = SubscriptionState.AcquiringAssets
                    };

                    var updateResult = await _subscriptionService.UpdateAsync(subscription.Id, updatedFields);
                    if (!updateResult.IsSuccess || updateResult.Data.Documents.FirstOrDefault() == null)
                    {
                        throw new DatabaseException($"Failed to update subscription {subscription.Id} state to AcquiringAssets: {updateResult.ErrorMessage}");
                    }

                    ctx.SetData("Subscription", updateResult.Data.Documents.First());

                    await _dashboardService.InvalidateCacheAndPush(subscription.UserId);

                    return StepResult.Success($"Updated subscription {subscription.Id} state to AcquiringAssets.", updateResult.Data.Documents.First());
                })
                .Build();

            _builder.Step("FetchAllocations")
                .After("PreparePayment")
                .RequiresData<SubscriptionData>("Subscription")
                .Execute(async context =>
                {
                    var subscription = context.GetData<SubscriptionData>("Subscription");

                    var fetchAllocationsResult = await _subscriptionService.GetEnhancedAllocationsAsync(subscription.Id);

                    if (!fetchAllocationsResult.IsSuccess || fetchAllocationsResult.Data is null || !fetchAllocationsResult.Data.Any())
                    {
                        var error = fetchAllocationsResult.ErrorMessage ?? "No allocations found";
                        throw new SubscriptionFetchException($"Unable to fetch subscription allocations: {error}");
                    }
                    var allocations = fetchAllocationsResult.Data;

                    _logger.LogInformation("Processing {Count} allocations for subscription {SubscriptionId}",
                        fetchAllocationsResult.Data.Count(), subscription.Id);

                    context.SetData("Allocations", allocations);

                    return StepResult.Success($"Fetched {allocations.Count} allocations for subscription {subscription.Id}.", allocations);
                })
                .WithDynamicBranches(
                    // Data selector: Your existing allocation fetching logic
                    ctx => ctx.GetData<List<EnhancedAllocationDto>>("Allocations"),

                    // Step factory: Convert each allocation to a sub-step
                    (builder, allocation, index) => builder.Branch($"Allocation_{index}_{allocation.AssetTicker}")
                        .WithSourceData(allocation)
                        .WithResourceGroup(allocation.Exchange) // For round-robin
                        .WithPriority(allocation.Priority)
                        .WithSteps(stepBuilder => {
                            stepBuilder.Step("ProcessSingleAllocation")
                                .Execute(async ctx =>
                                {
                                    // Your existing ProcessSingleAllocation logic
                                    var payment = ctx.GetData<PaymentData>("Payment");
                                    return StepResult.Success($"Processing allocation for {allocation.AssetTicker} on {allocation.Exchange}.",
                                        allocation);
                                })
                                .Triggers<AllocationExchangeOrderFlow>(context => new()
                                {
                                    ["Allocation"] = allocation,
                                    ["Payment"] = context.GetData<PaymentData>("Payment")
                                })
                                .Build();
                        })
                        .Build(),
                    ExecutionStrategy.RoundRobin) // Smart exchange distribution
                .InParallel()
                .Critical()
                .Build();

            _builder.Step("ResetSubscriptionState")
                .Execute(async ctx =>
                {
                    var subscription = ctx.GetData<SubscriptionData>("Subscription");

                    var updatedFields = new Dictionary<string, object>
                    {
                        ["State"] = SubscriptionState.Idle
                    };

                    var updateResult = await _subscriptionService.UpdateAsync(subscription.Id, updatedFields);
                    if (!updateResult.IsSuccess || updateResult.Data.Documents.FirstOrDefault() == null)
                    {
                        throw new DatabaseException($"Failed to update subscription {subscription.Id} state to AcquiringAssets: {updateResult.ErrorMessage}");
                    }

                    ctx.SetData("Subscription", updateResult.Data.Documents.First());

                    await _dashboardService.InvalidateCacheAndPush(subscription.UserId);

                    return StepResult.Success($"Updated subscription {subscription.Id} state to AcquiringAssets.", updateResult.Data.Documents.First());
                })
                .Build();

            _builder.Step("HandleDust")
                .After("FetchAllocations")
                .Triggers<HandleDustFlow>()
                .Build();
        }

        private void AddValidationError(Dictionary<string, List<string>> errs, string key, string msg)
        {
            if (!errs.TryGetValue(key, out var list)) { list = new(); errs[key] = list; }
            list.Add(msg);
        }
        private void ValidateInvoiceRequest(InvoiceRequest r)
        {
            var errors = new Dictionary<string, List<string>>();
            if (string.IsNullOrWhiteSpace(r.UserId) || !Guid.TryParse(r.UserId, out _))
                AddValidationError(errors, "UserId", "Invalid");
            if (string.IsNullOrWhiteSpace(r.SubscriptionId) || !Guid.TryParse(r.SubscriptionId, out _))
                AddValidationError(errors, "SubscriptionId", "Invalid");
            if (string.IsNullOrWhiteSpace(r.Id))
                AddValidationError(errors, "InvoiceId", "Invalid");
            if (r.Amount <= 0)
                AddValidationError(errors, "Amount", "Must be greater than 0");
            if (errors.Any()) throw new ValidationException("Invoice validation failed", errors.ToDictionary(k => k.Key, k => k.Value.ToArray()));
        }
    }
}
