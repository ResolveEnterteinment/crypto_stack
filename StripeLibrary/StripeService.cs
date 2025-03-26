using Application.Interfaces;
using Domain.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Stripe;

namespace StripeLibrary
{
    public class StripeService : IPaymentProvider, IStripeService

    {
        //private readonly StripeClient _client;
        private readonly ChargeService _chargeService;
        private readonly BalanceTransactionService _balanceTransactionService;
        private readonly PaymentIntentService _paymentIntentService;
        private readonly InvoiceService _invoiceService;
        private readonly SubscriptionService _subscriptionService;
        private readonly ILogger _logger;

        public StripeService(
            IOptions<StripeSettings> stripeSettings,
            ILogger logger)
        {
            //_client = new StripeClient(stripeSettings.Value.ApiKey);
            StripeConfiguration.ApiKey = stripeSettings.Value.ApiSecret;
            _chargeService = new ChargeService();
            _balanceTransactionService = new BalanceTransactionService();
            _paymentIntentService = new PaymentIntentService();
            _invoiceService = new InvoiceService();
            _subscriptionService = new SubscriptionService();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<PaymentIntent> GetPaymentIntentAsync(string id, PaymentIntentGetOptions? options = null)
        {
            return await _paymentIntentService.GetAsync(id, options);
        }

        /// <summary>
        /// Retrieves the Stripe fee for a given PaymentIntent, retrying if charge data is not yet available.
        /// </summary>
        /// <param name="paymentIntentId">The ID of the PaymentIntent to fetch the fee for.</param>
        /// <returns>The Stripe fee in decimal format.</returns>
        /// <exception cref="InvalidOperationException">Thrown if charge data cannot be retrieved after max retries.</exception>
        public async Task<decimal> GetFeeAsync(string paymentIntentId)
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

        public async Task<Domain.DTOs.Invoice> GetInvoiceAsync(string invoiceId)
        {
            var invoice = await _invoiceService.GetAsync(invoiceId);
            return new Domain.DTOs.Invoice()
            {
                Id = invoice.Id,
                CreatedAt = invoice.Created,
                AmountDue = invoice.AmountDue,
                AmountPaid = invoice.AmountPaid,
                AmountRemaining = invoice.AmountRemaining,
                Currency = invoice.Currency,
                CustomerEmail = invoice.CustomerEmail,
                DueDate = invoice.DueDate,
                Paid = invoice.Paid,
                Tax = invoice.Tax,
                Discount = invoice.TotalDiscountAmounts.Select(d => d.Amount).Sum(),
                Total = invoice.Total,
                Status = invoice.Status,
            };
        }

        public async Task<DateTime?> GetNextDueDate(string invoiceId)
        {
            if (string.IsNullOrEmpty(invoiceId))
            {
                return null;
            }
            var invoice = await _invoiceService.GetAsync(invoiceId);
            if (invoice == null || string.IsNullOrEmpty(invoice.SubscriptionId))
            {
                return null;
            }
            var subscription = await _subscriptionService.GetAsync(invoice.SubscriptionId);
            if (subscription == null || subscription.Status == "canceled")
            {
                return null;
            }
            return subscription.CurrentPeriodEnd;
        }

        public async Task<Domain.DTOs.Payment.Subscription> GetSubscriptionAsync(string? subscriptionId)
        {
            var subscription = await _subscriptionService.GetAsync(subscriptionId);
            return new Domain.DTOs.Payment.Subscription()
            {
                NextDueDate = subscription.CurrentPeriodEnd,
            };
        }

        public async Task<Domain.DTOs.Payment.Subscription> GetSubscriptionByPaymentAsync(string paymentProviderId)
        {
            var payment = await _paymentIntentService.GetAsync(paymentProviderId);
            var invoice = await _invoiceService.GetAsync(payment.Invoice.Id);
            var subscription = await _subscriptionService.GetAsync(invoice.Subscription.Id);
            return new()
            {
                NextDueDate = subscription.CurrentPeriodEnd,
            };
        }
    }
}