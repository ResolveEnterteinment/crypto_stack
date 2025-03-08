using Domain.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;

namespace StripeLibrary
{
    public class StripeService : IStripeService
    {
        private readonly StripeClient _client;
        private readonly ChargeService _chargeService;
        private readonly BalanceTransactionService _balanceTransactionService;
        private readonly ILogger _logger;

        public StripeService(
            IOptions<StripeSettings> stripeSettings,
            ILogger logger)
        {
            //_client = new StripeClient(stripeSettings.Value.ApiKey);
            StripeConfiguration.ApiKey = stripeSettings.Value.ApiSecret;
            _chargeService = new ChargeService();
            _balanceTransactionService = new BalanceTransactionService();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Retrieves the Stripe fee for a given PaymentIntent, retrying if charge data is not yet available.
        /// </summary>
        /// <param name="paymentIntentId">The ID of the PaymentIntent to fetch the fee for.</param>
        /// <returns>The Stripe fee in decimal format.</returns>
        /// <exception cref="InvalidOperationException">Thrown if charge data cannot be retrieved after max retries.</exception>
        public async Task<decimal> GetStripeFeeAsync(string paymentIntentId)
        {
            int attempt = 0;
            var MaxRetries = 5;
            var RetryDelaySeconds = 2;

            while (attempt < MaxRetries)
            {
                try
                {
                    // Step 1: Fetch the PaymentIntent with the latest_charge expanded
                    var paymentIntentService = new PaymentIntentService();
                    var paymentIntent = await paymentIntentService.GetAsync(
                        paymentIntentId,
                        new PaymentIntentGetOptions { Expand = new List<string> { "latest_charge" } }
                    );

                    // Step 2: Check if the latest charge exists
                    if (string.IsNullOrEmpty(paymentIntent.LatestChargeId))
                    {
                        _logger.LogWarning(
                            "No charge found for PaymentIntent {PaymentIntentId} on attempt {Attempt}",
                            paymentIntentId, attempt + 1);
                        throw new InvalidOperationException("No charge associated with PaymentIntent.");
                    }

                    // Step 3: Fetch the Charge using the latest_charge ID
                    var charge = await _chargeService.GetAsync(
                        paymentIntent.LatestChargeId,
                        new ChargeGetOptions { Expand = new List<string> { "balance_transaction" } }
                    );

                    // Step 4: Verify the charge status
                    if (charge.Status != "succeeded")
                    {
                        _logger.LogWarning(
                            "Charge {ChargeId} for PaymentIntent {PaymentIntentId} is not succeeded. Status: {Status} on attempt {Attempt}",
                            charge.Id, paymentIntentId, charge.Status, attempt + 1);
                        throw new InvalidOperationException($"Charge is not succeeded. Current status: {charge.Status}");
                    }

                    // Step 5: Retrieve the balance transaction
                    var balanceTransaction = charge.BalanceTransaction;
                    if (balanceTransaction == null)
                    {
                        _logger.LogWarning(
                            "Failed to retrieve BalanceTransaction for Charge {ChargeId} on attempt {Attempt}",
                            charge.Id, attempt + 1);
                        throw new InvalidOperationException("Unable to retrieve fee details.");
                    }

                    // Step 6: Return the fee (converted from cents to decimal)
                    var chargeAmount = Math.Round((decimal)((balanceTransaction.Fee / balanceTransaction.ExchangeRate)), 0, MidpointRounding.AwayFromZero);
                    return chargeAmount / 100m;
                }
                catch (InvalidOperationException ex)
                {
                    attempt++;
                    if (attempt >= MaxRetries)
                    {
                        _logger.LogError(
                            ex,
                            "Failed to retrieve Stripe fee after {MaxRetries} attempts for PaymentIntent {PaymentIntentId}",
                            MaxRetries, paymentIntentId);
                        throw new InvalidOperationException("Maximum retry attempts exceeded.", ex);
                    }

                    _logger.LogInformation(
                        "Retrying to fetch charge data for PaymentIntent {PaymentIntentId}. Attempt {Attempt} of {MaxRetries}",
                        paymentIntentId, attempt + 1, MaxRetries);
                    await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds));
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Unexpected error retrieving Stripe fee for PaymentIntent {PaymentIntentId}",
                        paymentIntentId);
                    throw;
                }
            }

            throw new InvalidOperationException("Maximum retry attempts exceeded.");
        }
    }
}