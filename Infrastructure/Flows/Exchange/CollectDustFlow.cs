using Application.Interfaces.Treasury;
using Domain.Constants;
using Domain.DTOs.Exchange;
using Domain.DTOs.Subscription;
using Domain.Exceptions;
using Domain.Models.Exchange;
using Infrastructure.Services.FlowEngine.Core.Models;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Flows.Exchange
{
    /// <summary>
    /// Enhanced flow for handling dust from exchange orders
    /// Dust is retained by the platform and tracked in the treasury system
    /// </summary>
    public class CollectDustFlow : FlowDefinition
    {
        private readonly ITreasuryService _treasuryService;
        private readonly ILogger<CollectDustFlow> _logger;

        public CollectDustFlow(
            ITreasuryService treasuryService,
            ILogger<CollectDustFlow> logger)
        {
            _treasuryService = treasuryService ?? throw new ArgumentNullException(nameof(treasuryService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override void DefineSteps()
        {
            _builder.Step("CalculateDust")
                .RequiresData<ExchangeOrderData>("ExchangeOrderData")
                .RequiresData<PlacedExchangeOrder>("PlacedOrder")
                .RequiresData<EnhancedAllocationDto>("Allocation")
                .Execute(async context =>
                {
                    var placedOrder = context.GetData<PlacedExchangeOrder>("PlacedOrder");
                    var exchangeOrder = context.GetData<ExchangeOrderData>("ExchangeOrderData");
                    var allocation = context.GetData<EnhancedAllocationDto>("Allocation");

                    // Calculate dust amount
                    // Dust = Quantity ordered - Quantity filled
                    decimal dustAmount = 0m;
                    string dustTicker = allocation.Currency.ToUpperInvariant(); // Usually the quote asset

                    if (exchangeOrder.Side == OrderSide.Buy)
                    {
                        // For buy orders, dust is in quote asset (what we're paying with)
                        dustAmount = (exchangeOrder.QuoteQuantity - exchangeOrder.QuoteQuantityFilled) ?? 0m;
                    }
                    else if (exchangeOrder.Side == OrderSide.Sell)
                    {
                        // For sell orders, dust is in base asset (what we're selling)
                        dustAmount = (placedOrder.Quantity - placedOrder.QuantityFilled);
                        dustTicker = exchangeOrder.Ticker;
                    }

                    // Store calculated values
                    context.SetData("DustAmount", dustAmount);
                    context.SetData("DustTicker", dustTicker);

                    _logger.LogInformation(
                        "Calculated dust for order {OrderId}: {Amount} {Asset}",
                        placedOrder.OrderId, dustAmount, dustTicker);

                    return StepResult.Success($"Dust calculated: {dustAmount} {dustTicker}");
                })
                .Build();

            _builder.Step("CheckDustThreshold")
                .RequiresData<EnhancedAllocationDto>("Allocation")
                .Execute(async context =>
                {
                    var dustAmount = context.GetData<decimal>("DustAmount");
                    var dustTicker = context.GetData<string>("DustTicker");

                    // Define minimum dust thresholds by asset type
                    // decimal minThreshold = GetMinimumDustThreshold(dustAsset);
                    var allocation = context.GetData<EnhancedAllocationDto>("Allocation");

                    decimal minThreshold = 1m/((decimal)Math.Pow(10, allocation.Precision));

                    if (dustAmount < minThreshold)
                    {
                        _logger.LogTrace(
                            "Dust amount {Amount} {Asset} below threshold {Threshold}, skipping collection",
                            dustAmount, dustTicker, minThreshold);

                        context.SetData("ShouldCollectDust", false);

                        return StepResult.Success("Dust below threshold, skipped");
                    }

                    context.SetData("ShouldCollectDust", true);
                    return StepResult.Success("Dust above threshold, will collect");
                })
                .Build();

            _builder.Step("RecordDustInTreasury")
                .OnlyIf(context => context.GetData<bool>("ShouldCollectDust"))
                .Execute(async context =>
                    {
                        var order = context.GetData<PlacedExchangeOrder>("ValidatedOrder");

                        try
                        {
                            var dustAmount = context.GetData<decimal>("DustAmount");
                            var dustTicker = context.GetData<string>("DustTicker");

                            // Get asset ID (you would need to implement this lookup)
                            // For now, using a placeholder
                            var assetId = await GetAssetIdByTicker(dustTicker);

                            // Record in treasury
                            var treasuryTransaction = await _treasuryService.RecordDustCollectionAsync(
                                dustAmount: dustAmount,
                                assetTicker: dustTicker,
                                assetId: assetId,
                                exchange: order.Exchange,
                                orderId: order.OrderId.ToString(),
                                userId: Guid.Parse(context.State.UserId),
                                cancellationToken: context.CancellationToken);

                            if (treasuryTransaction != null)
                            {
                                _logger.LogInformation(
                                    "Dust recorded in treasury: {TreasuryTxId} - {Amount} {Asset}",
                                    treasuryTransaction.Id, dustAmount, dustTicker);

                                context.SetData("TreasuryTransactionId", treasuryTransaction.Id);
                                return StepResult.Success($"Dust collected: {dustAmount} {dustTicker}");
                            }

                            return StepResult.Success("Dust was too small to record");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex,
                                "Error recording dust in treasury for order {OrderId}",
                                context.GetData<PlacedExchangeOrder>("ValidatedOrder")?.OrderId);

                            throw new DomainException($"Error recording dust in treasury for order {order.OrderId}","DUST_COLLECTION_ERROR", ex);
                        }
                    })
                .AllowFailure()
                .Build();
        }

        /// <summary>
        /// Gets minimum dust threshold by asset
        /// Below this amount, dust is not worth collecting due to gas fees, etc.
        /// </summary>
        private decimal GetMinimumDustThreshold(string assetTicker)
        {
            // Define thresholds based on asset type
            return assetTicker.ToUpperInvariant() switch
            {
                "BTC" => 0.00000010m,  // 10 satoshis
                "ETH" => 0.000001m,     // 1 gwei equivalent
                "USDT" or "USDC" or "USD" => 0.01m,  // 1 cent
                "BNB" => 0.0001m,
                _ => 0.00001m  // Default small threshold
            };
        }

        /// <summary>
        /// Placeholder for asset lookup
        /// In production, this would query the asset service
        /// </summary>
        private async Task<Guid> GetAssetIdByTicker(string ticker)
        {
            // TODO: Implement actual asset lookup
            // For now, return a placeholder
            await Task.CompletedTask;
            return Guid.NewGuid();
        }
    }
}
