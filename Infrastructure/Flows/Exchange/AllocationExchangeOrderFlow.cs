using Application.Interfaces.Asset;
using Application.Interfaces.Base;
using Application.Interfaces.Exchange;
using Application.Interfaces.Logging;
using Domain.Constants.Logging;
using Domain.DTOs.Exchange;
using Domain.DTOs.Subscription;
using Domain.Events;
using Domain.Exceptions;
using Domain.Models.Asset;
using Domain.Models.Exchange;
using Domain.Models.Payment;
using Infrastructure.Services.FlowEngine.Core.Enums;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Core.PauseResume;

namespace Infrastructure.Flows.Exchange
{
    public class AllocationExchangeOrderFlow : FlowDefinition
    {
        private readonly IAssetService _assetService;
        private readonly IExchangeService _exchangeService;
        private readonly IBalanceManagementService _balanceManagementService;
        private readonly IOrderManagementService _orderManagementService;
        private readonly IEventService _eventService;
        private readonly ILoggingService _logger;

        public AllocationExchangeOrderFlow(
            IAssetService assetService,
            IExchangeService exchangeService,
            IBalanceManagementService balanceManagementService,
            IOrderManagementService orderManagementService,
            IEventService eventService,
            ILoggingService logger)
        {
            _assetService = assetService ?? throw new ArgumentNullException(nameof(assetService));
            _exchangeService = exchangeService ?? throw new ArgumentNullException(nameof(exchangeService));
            _balanceManagementService = balanceManagementService ?? throw new ArgumentNullException(nameof(balanceManagementService));
            _orderManagementService = orderManagementService ?? throw new ArgumentNullException(nameof(orderManagementService));
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override void DefineSteps()
        {
            _builder.Step("ValidateAndPrepareAllocation")
                .RequiresData<PaymentData>("Payment")
                .RequiresData<EnhancedAllocationDto>("Allocation")
                .Execute(async context =>
                {
                    var payment = context.GetData<PaymentData>("Payment");
                    var allocation = context.GetData<EnhancedAllocationDto>("Allocation");

                    _logger.LogInformation("Starting allocation processing for asset {AssetId} with {Percent}% allocation",
                        allocation.AssetId, allocation.PercentAmount);

                    // Validate allocation percentage
                    if (allocation.PercentAmount <= 0 || allocation.PercentAmount > 100)
                    {
                        return StepResult.Failure($"Invalid allocation percentage: {allocation.PercentAmount}%");
                    }

                    // Calculate quote order quantity
                    decimal quoteOrderQuantity = Math.Round(payment.NetAmount * (allocation.PercentAmount / 100m), 2, MidpointRounding.ToZero);

                    if (quoteOrderQuantity <= 0m)
                    {
                        return StepResult.Failure($"Calculated order quantity must be positive: {quoteOrderQuantity}");
                    }

                    context.SetData("QuoteOrderQuantity", quoteOrderQuantity);

                    return StepResult.Success("Allocation validated and prepared", quoteOrderQuantity);
                })
                .Build();

            _builder.Step("FetchAndValidateAsset")
                .After("ValidateAndPrepareAllocation")
                .RequiresData<EnhancedAllocationDto>("Allocation")
                .Execute(async context =>
                {
                    EnhancedAllocationDto allocation = context.GetData<EnhancedAllocationDto>("Allocation")!;

                    var assetResult = await _assetService.GetByIdAsync(allocation.AssetId);
                    if (assetResult == null || !assetResult.IsSuccess || assetResult.Data == null)
                    {
                        throw new AssetFetchException($"Failed to fetch asset {allocation.AssetId}");
                    }

                    var asset = assetResult.Data;

                    context.SetData("Asset", asset);

                    return StepResult.Success($"Asset {asset.Ticker} fetched and validated", asset);
                })
                .Build();

            _builder.Step("GetAndValidateExchange")
                .After("FetchAndValidateAsset")
                .RequiresData<AssetData>("Asset")
                .Execute(async context =>
                {
                    AssetData asset = context.GetData<AssetData>("Asset")!;

                    if (string.IsNullOrEmpty(asset.Exchange) ||
                        !_exchangeService.Exchanges.TryGetValue(asset.Exchange, out var exchange) || 
                        exchange == null)
                    {
                        return StepResult.Failure($"No exchange configured for asset {asset.Ticker}");
                    }

                    context.SetRuntime("Exchange", exchange);

                    return StepResult.Success($"Exchange {exchange.Name} validated for asset {asset.Ticker}");
                })
                .Build();

            _builder.Step("ValidateExchangeBalance")
                .After("GetAndValidateExchange")
                .RequiresData<AssetData>("Asset")
                .RequiresData<decimal>("QuoteOrderQuantity")
                .CanPause(async context => {
                    AssetData asset = context.GetData<AssetData>("Asset")!;
                    var exchange = context.GetRuntime<IExchange>("Exchange") ?? _exchangeService.Exchanges[asset.Exchange];
                    var quoteOrderQuantity = context.GetData<decimal>("QuoteOrderQuantity");

                    var checkBalanceResult = await _balanceManagementService.CheckExchangeBalanceAsync(
                        asset.Exchange, exchange.QuoteAssetTicker, quoteOrderQuantity);

                    if (!checkBalanceResult.IsSuccess)
                    {
                        throw new ExchangeApiException($"Failed to check exchange balance: {checkBalanceResult.ErrorMessage}");
                    }

                    context.SetData("IsExchangeBalanceSufficient", checkBalanceResult.Data);

                    if (checkBalanceResult.Data is false)
                    {
                        // Pause the flow if insufficient balance - can be resumed when balance is topped up
                        return PauseCondition.Pause(PauseReason.InsufficientResources,
                            checkBalanceResult.DataMessage ?? "Insufficient exchange balance to process order");
                    }

                    return PauseCondition.Continue();
                })
                .ResumeOn(resume => {
                    resume.OnEvent("BalanceTopUp");
                    resume.AllowManual(["ADMIN"]);
                })
                .Execute(async context =>
                {
                    return StepResult.Success("Exchange balance validated");
                })
                .Build();

            _builder.Step("CalculateRemainingQuantity")
                .After("ValidateExchangeBalance")
                .RequiresData<AssetData>("Asset")
                .RequiresData<PaymentData>("Payment")
                .RequiresData<decimal>("QuoteOrderQuantity")
                .Execute(async context =>
                {
                    var asset = context.GetData<AssetData>("Asset");
                    var exchange = context.GetRuntime<IExchange>("Exchange") ?? _exchangeService.Exchanges[asset!.Exchange];
                    var payment = context.GetData<PaymentData>("Payment");
                    var quoteOrderQuantity = context.GetData<decimal>("QuoteOrderQuantity");

                    var previousFilledSumResult = await _orderManagementService.GetPreviousOrdersSum(exchange, asset, payment);

                    if (!previousFilledSumResult.IsSuccess)
                    {
                        throw new OrderFetchException($"Failed to calculate remaining order quantity for {payment.PaymentProviderId}: {previousFilledSumResult.ErrorMessage}");
                    }

                    var remainingQuantity = quoteOrderQuantity - previousFilledSumResult.Data;

                    context.SetData("RemainingQuantity", remainingQuantity);

                    return StepResult.Success($"Remaining quantity calculated: {remainingQuantity}", remainingQuantity);
                })
                .Build();

            _builder.Step("CheckMinimumNotional")
                .After("CalculateRemainingQuantity")
                .RequiresData<AssetData>("Asset")
                .RequiresData<decimal>("RemainingQuantity")
                .Execute(async context =>
                {
                    var asset = context.GetData<AssetData>("Asset");
                    var exchange = context.GetRuntime<IExchange>("Exchange") ?? _exchangeService.Exchanges[asset.Exchange];

                    var minNotionalResult = await _orderManagementService.GetMinNotional(exchange, asset);
                    var minNotional = minNotionalResult?.Data ?? _exchangeService.MIN_NOTIONAL_FALLBACK_AMOUNT;

                    if (minNotionalResult == null || !minNotionalResult.IsSuccess)
                    {
                        await _logger.LogTraceAsync($"Failed to fetch minimum notional for {asset.Ticker} on {exchange.Name}, using default: {minNotional}",
                            level: LogLevel.Warning);
                    }

                    context.SetData("MinNotional", minNotional);

                    return StepResult.Success($"Min notional: {minNotional}", minNotional);
                })
                .WithStaticBranches(builder =>
                {
                    builder.Branch("MinNotinalCheckFailed")
                    .When(context =>
                    {
                        var remainingQuantity = context.GetData<decimal>("RemainingQuantity");
                        var minNotional = context.GetData<decimal>("MinNotional");
                        return remainingQuantity <= minNotional;
                    })
                    .WithSteps(stepBuilder =>
                    {
                        stepBuilder.Step("FailedPath")
                        .Execute(async context =>
                        {
                            var exchange = context.GetRuntime<IExchange>("Exchange");
                            var asset = context.GetData<AssetData>("Asset");
                            var remainingQuantity = context.GetData<decimal>("RemainingQuantity");
                            var assetId = context.GetData<string>("AssetId");
                            var minNotional = context.GetData<decimal>("MinNotional");

                            _logger.LogInformation("Order for asset {AssetId} already fully processed or remaining quantity {RemainingQuantity} is less than minimum notional {MinNotional}",
                            assetId, remainingQuantity, minNotional);

                            // Create success result for already processed order
                            var orderResult = OrderResult.Success(exchange.Name, 0, assetId, remainingQuantity, 0, "BELOW_MIN_NOTIONAL");

                            context.SetData("OrderResult", orderResult);

                            return StepResult.Success($"Minimum notional check failed. Order for asset {assetId} already fully processed or remaining quantity {remainingQuantity} is less than minimum notional {minNotional}. Jumping to step OrderResult", orderResult);
                        })
                        .JumpTo("OrderResult")
                        .Build();
                    })
                    .Build();
                })
                .Build();

            _builder.Step("PlaceExchangeOrder")
                .After("CheckMinimumNotional")
                .RequiresData<PaymentData>("Payment")
                .RequiresData<EnhancedAllocationDto>("Allocation")
                .RequiresData<AssetData>("Asset")
                .RequiresData<decimal>("RemainingQuantity")
                .Execute(async context =>
                {
                    var payment = context.GetData<PaymentData>("Payment");
                    var allocation = context.GetData<EnhancedAllocationDto>("Allocation");
                    var asset = context.GetData<AssetData>("Asset");
                    var exchange = context.GetRuntime<IExchange>("Exchange") ?? _exchangeService.Exchanges[asset!.Exchange];
                    var remainingQuantity = context.GetData<decimal>("RemainingQuantity");

                    _logger.LogInformation("Placing order for {Ticker}: {Amount} {Currency} ({Percent}%)",
                        asset.Ticker, remainingQuantity, exchange.QuoteAssetTicker, allocation.PercentAmount);

                    // Place the order
                    var placedOrderResult = await _orderManagementService.PlaceExchangeOrderAsync(
                        exchange, asset.Ticker, remainingQuantity, payment.PaymentProviderId);

                    if (placedOrderResult == null || !placedOrderResult.IsSuccess || placedOrderResult.Data is null)
                    {
                        throw new OrderExecutionException(
                            $"Unable to place order: {placedOrderResult?.ErrorMessage ?? "Order result returned null"}",
                            exchange.Name);
                    }

                    var placedOrder = placedOrderResult.Data;

                    _logger.LogInformation("Successfully placed order {OrderId} for {Ticker}: {Quantity} @ {Price}",
                        placedOrder.OrderId, asset.Ticker, placedOrder.QuantityFilled, placedOrder.Price);

                    context.SetData("PlacedOrder", placedOrder);

                    return StepResult.Success($"Successfully placed order {placedOrder.OrderId} for {asset.Ticker}: {placedOrder.QuantityFilled} @ {placedOrder.Price}", placedOrder);
                })
                .Critical()
                .WithIdempotency()
                .Build();

            _builder.Step("RecordExchangeOrder")
                .After("PlaceExchangeOrder")
                .RequiresData<PaymentData>("Payment")
                .RequiresData<EnhancedAllocationDto>("Allocation")
                .RequiresData<PlacedExchangeOrder>("PlacedOrder")
                .Execute(async context =>
                {
                    var payment = context.GetData<PaymentData>("Payment");
                    var allocation = context.GetData<EnhancedAllocationDto>("Allocation");
                    var placedOrder = context.GetData<PlacedExchangeOrder>("PlacedOrder");

                    var quoteTicker = _exchangeService.Exchanges[placedOrder.Exchange].QuoteAssetTicker;
                    var exchangeOrder = new ExchangeOrderData
                    {
                        UserId = payment.UserId,
                        PaymentProviderId = payment.PaymentProviderId,
                        SubscriptionId = payment.SubscriptionId,
                        Exchange = placedOrder.Exchange,
                        Side = placedOrder.Side.ToString().ToLowerInvariant(),
                        PlacedOrderId = placedOrder.OrderId,
                        AssetId = allocation.AssetId,
                        Ticker = allocation.AssetTicker,
                        Quantity = placedOrder.QuantityFilled,
                        QuoteTicker = quoteTicker,
                        QuoteQuantity = placedOrder.QuoteQuantity,
                        QuoteQuantityFilled = placedOrder.QuoteQuantityFilled,
                        Price = placedOrder.Price,
                        Status = placedOrder.Status
                    };

                    var insertOrderResult = await _exchangeService.InsertAsync(exchangeOrder);
                    if (insertOrderResult == null || !insertOrderResult.IsSuccess || !insertOrderResult.Data.IsSuccess)
                    {
                        await _logger.LogTraceAsync($"Failed to create exchange order record: {insertOrderResult?.ErrorMessage ?? "Unknown error"}",
                            action: "RecordExchangeOrder", level: LogLevel.Error);
                        throw new DatabaseException($"Failed to record exchange order: {insertOrderResult?.ErrorMessage ?? "Unknown error"}");
                    }

                    var recordedOrder = insertOrderResult.Data.Documents.First();

                    context.SetData("ExchangeOrderData", recordedOrder);

                    return StepResult.Success($"Exchange order recorded with ID: {recordedOrder.Id}", recordedOrder);
                })
                .WithIdempotency()
                .Critical()
                .Build();

            _builder.Step("PublishOrderCompletedEvent")
                .After("RecordExchangeOrder")
                .RequiresData<ExchangeOrderData>("ExchangeOrderData")
                .Execute(async context =>
                {
                    var exchangeOrderData = context.GetData<ExchangeOrderData>("ExchangeOrderData");

                    await _eventService.PublishAsync(new ExchangeOrderCompletedEvent(exchangeOrderData, _logger.Context));

                    return StepResult.Success($"Order completed event published for order {exchangeOrderData.Id}");
                })
                .InParallel()
                .AllowFailure() // Don't fail the entire flow if event publishing fails
                .Build();

            _builder.Step("OrderResult")
                .After("PublishOrderCompletedEvent", "CheckMinimumNotional") // Can come from either path
                .Execute(async context =>
                {
                    // If order result already exists (from min notional check failure), reuse it
                    var existingResult = context.GetData<OrderResult>("OrderResult");
                    if (existingResult != null)
                    {
                        return StepResult.Success("Using existing order result from previous step", existingResult);
                    }

                    // Otherwise, create result from placed order
                    var placedOrder = context.GetData<PlacedExchangeOrder>("PlacedOrder");
                    var assetId = context.GetData<string>("AssetId");

                    var orderResult = OrderResult.Success(
                        placedOrder.Exchange,
                        placedOrder.OrderId,
                        assetId,
                        placedOrder.QuoteQuantity,
                        placedOrder.QuantityFilled,
                        placedOrder.Status);

                    context.SetData("OrderResult", orderResult);

                    return StepResult.Success("Order result finalized", orderResult);
                })
                .InParallel()
                .Critical()
                .Build();
        }
    }
}