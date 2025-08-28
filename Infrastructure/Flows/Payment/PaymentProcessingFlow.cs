using Application.Contracts.Requests.Payment;
using Application.Interfaces.Payment;
using Application.Interfaces.Subscription;
using Domain.DTOs.Subscription;
using Domain.Exceptions;
using Domain.Models.Payment;
using Domain.Models.Subscription;
using Infrastructure.Flows.Exchange;
using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Definition.Builders;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Infrastructure.Flows.Payment
{
    public class PaymentProcessingFlow : FlowDefinition
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly IPaymentService _paymentService;
        private readonly ILogger<PaymentProcessingFlow> _logger;
        private readonly FlowStepBuilder _builder;

        public PaymentProcessingFlow(
            ILogger<PaymentProcessingFlow> logger,
            ISubscriptionService subscriptionService,
            IPaymentService paymentService)
        {
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _builder = new FlowStepBuilder(this);
        }
        protected override void DefineSteps()
        {
            _builder.Step("ValidateInvoiceAndExtractMetadata")
                .RequiresData<Stripe.Invoice>("Invoice")
                .Execute(async context =>
                {
                    _logger.LogInformation($"Handling stripe event invoice.paid");

                    var invoice = context.GetData<Stripe.Invoice>("Invoice");

                    var metadata = invoice.SubscriptionDetails.Metadata;
                    // Extract subscription ID from metadata

                    if (metadata == null || metadata.Count == 0)
                    {
                        return StepResult.Failure("Invalid metadata");
                    }

                    if (!metadata.TryGetValue("subscriptionId", out var subscriptionId) ||
                        string.IsNullOrEmpty(subscriptionId) ||
                        !Guid.TryParse(subscriptionId, out var parsedSubscriptionId))
                    {
                        return StepResult.Failure("Missing or invalid subscriptionId in Invoice metadata");
                    }

                    var subscriptionResult = await _subscriptionService.GetByIdAsync(parsedSubscriptionId);

                    if (subscriptionResult == null || !subscriptionResult.IsSuccess)
                    {
                        return StepResult.NotFound("Subscription", subscriptionId);
                    }

                    context.SetData("Subscription", subscriptionResult.Data);

                    // Extract user ID from metadata
                    if (!metadata.TryGetValue("userId", out var userId) ||
                        string.IsNullOrEmpty(userId) ||
                        !Guid.TryParse(userId, out var parsedUserId))
                    {
                        return StepResult.Failure("Missing or invalid userId in Invoice metadata");
                    }

                    if (subscriptionResult.Data.UserId != parsedUserId)
                    {
                        return StepResult.NotAuthorized("User ID in metadata does not match subscription user ID.");
                    }

                    return StepResult.Success("Stripe invoice validated.", new()
                    {
                        ["InvoiceRequest"] = new InvoiceRequest()
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
                        }
                    });
                })
                .Build();

            _builder.Step("PreparePayment")
                .After("ValidateInvoiceAndExtractMetadata")
                .RequiresData<InvoiceRequest>("InvoiceRequest")
                .Execute(async context =>
                {
                    var invoice = GetData<InvoiceRequest>("InvoiceRequest");

                    ValidateInvoiceRequest(invoice);

                    // Check existing
                    var existingWr = await _paymentService.GetOneAsync(
                        Builders<PaymentData>.Filter.Eq(p => p.InvoiceId, invoice.Id));

                    if (existingWr != null && existingWr.Data != null)
                    {
                        return StepResult.Success($"Payment data with invoice ID {invoice.Id} already present.", new()
                        {
                            ["Payment"] = existingWr.Data
                        });
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

                    return StepResult.Success($"Payment data ready", new()
                    {
                        ["Payment"] = paymentData
                    });
                })
                .WithIdempotency()
                .Build();

            _builder.Step("CreatePaymentRecord")
                .After("PreparePayment")
                .RequiresData<PaymentData>("Payment")
                .Execute(async context =>
                {
                    var paymentData = GetData<PaymentData>("Payment");
                    // Update status to processing
                    var insertWr = await _paymentService.InsertAsync(paymentData);

                    if (insertWr == null || !insertWr.IsSuccess)
                        throw new DatabaseException(insertWr?.ErrorMessage ?? "Insert result returned null");

                    return StepResult.Success($"Payment record created.", new()
                    {
                        ["Payment"] = insertWr.Data.Documents.FirstOrDefault()
                    });
                })
                .WithIdempotency()
                .Critical()
                .InParallel()
                .Triggers<UpdateSubscriptionPostPaymentFlow>()
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

                    return StepResult.Success($"Fetched {allocations.Count} allocations for subscription {subscription.Id}.", new()
                    {
                        ["Allocations"] = allocations
                    });
                })
                .WithDynamicBranches(
                // Data selector: Your existing allocation fetching logic
                ctx => ctx.GetData<List<EnhancedAllocationDto>>("Allocations"),

                // Step factory: Convert each allocation to a sub-step

                (allocation, index) => new FlowSubStep
                {
                    Name = $"ExecuteOrder_{allocation.AssetTicker}_{index}",
                    SourceData = allocation,
                    ResourceGroup = allocation.Exchange, // For round-robin
                    Priority = allocation.Priority,
                    ExecuteAsync = async ctx =>
                    {
                        // Your existing ProcessSingleAllocation logic
                        var payment = ctx.GetData<PaymentData>("Payment");
                        return StepResult.Success($"Processing allocation for {allocation.AssetTicker} on {allocation.Exchange}.",
                            new Dictionary<string, object> { ["Allocation"] = allocation });
                    },
                    TriggeredFlows = new List<Type> { typeof(ExecuteExchangeOrderFlow) }
                },
                ExecutionStrategy.RoundRobin // Smart exchange distribution
                )
                .InParallel()
                .Critical()
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
