using Application.Interfaces.Payment;
using Application.Interfaces.Subscription;
using Domain.Constants;
using Domain.Exceptions;
using Domain.Models.Payment;
using Domain.Models.Subscription;
using Infrastructure.Services.Base;
using Infrastructure.Services.FlowEngine.Core.Models;
using Infrastructure.Services.FlowEngine.Definition.Builders;
using Microsoft.Extensions.Logging;
using MongoDB.Driver.Linq;

namespace Infrastructure.Flows.Payment
{
    public class UpdateSubscriptionPostPaymentFlow : FlowDefinition
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly IPaymentService _paymentService;
        private readonly INotificationService _notificationService;
        private readonly ILogger<UpdateSubscriptionPostPaymentFlow> _logger;
        private readonly FlowStepBuilder _builder;

        // Default constructor for deserialization purposes
        public UpdateSubscriptionPostPaymentFlow() { }

        public UpdateSubscriptionPostPaymentFlow(
            ILogger<UpdateSubscriptionPostPaymentFlow> logger,
            ISubscriptionService subscriptionService,
            IPaymentService paymentService,
            INotificationService notificationService)
        {
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _builder = new FlowStepBuilder(this);
        }
        protected override void DefineSteps()
        {
            _builder.Step("CalculateTotalInvestments")
                .RequiresData<PaymentData>("Payment")
                .Execute(async context =>
                {
                    var payment = context.GetData<PaymentData>("Payment");
                    var subscription = context.GetData<SubscriptionData>("Subscription");

                    if(subscription == null)
                    {
                        var subscriptionResult = await _subscriptionService.GetByIdAsync(payment.SubscriptionId);
                        if (subscriptionResult == null || !subscriptionResult.IsSuccess)
                        {
                            _logger.LogWarning("Failed to retrieve subscription {SubscriptionId} for payment {PaymentId}: {Error}",
                                payment.SubscriptionId, payment.Id, subscriptionResult?.ErrorMessage ?? "Subscription not found");
                            return StepResult.NotFound("Subscription", payment.SubscriptionId.ToString());
                        }
                        subscription = subscriptionResult.Data;
                    }

                    _logger.LogInformation("Processing payment for subscription {SubscriptionId}: {Amount} {Currency}",
                        subscription.Id, payment.NetAmount, payment.Currency);

                    // Calculate new investment total
                    var subscriptionPaymentsResult = await _paymentService.GetPaymentsForSubscriptionAsync(payment.SubscriptionId);

                    if (subscriptionPaymentsResult == null || !subscriptionPaymentsResult.IsSuccess)
                    {
                        _logger.LogWarning("Failed to calculate investment totals for subscription {SubscriptionId}: {Error}",
                            subscription.Id, subscriptionPaymentsResult?.ErrorMessage ?? "Subscription payments returned null");
                    }

                    var totalInvestments = subscriptionPaymentsResult?.Data.Select(p => p.TotalAmount).Sum() ?? 0m;

                    return StepResult.Success($"Calculated total investments for subscription ID {subscription.Id}.", new()
                    {
                        ["TotalInvestments"] = totalInvestments
                    });
                })
                .InParallel()
                .Build();

            _builder.Step("GetNextDueDate")
                .RequiresData<PaymentData>("Payment")
                .Execute(async context =>
                {
                    var payment = context.GetData<PaymentData>("Payment");
                    var subscription = context.GetData<SubscriptionData>("Subscription");

                    if (subscription == null)
                    {
                        var subscriptionResult = await _subscriptionService.GetByIdAsync(payment.SubscriptionId);
                        if (subscriptionResult == null || !subscriptionResult.IsSuccess)
                        {
                            _logger.LogWarning("Failed to retrieve subscription {SubscriptionId} for payment {PaymentId}: {Error}",
                                payment.SubscriptionId, payment.Id, subscriptionResult?.ErrorMessage ?? "Subscription not found");
                            return StepResult.NotFound("Subscription", payment.SubscriptionId.ToString());
                        }
                        subscription = subscriptionResult.Data;
                    }

                    // Get next due date from payment provider
                    var nextDueDate = await _paymentService.Providers["Stripe"].GetNextDueDate(payment.InvoiceId);

                    if (nextDueDate == null)
                    {
                        var interval = subscription.Interval;
                        nextDueDate = interval switch
                        {
                            SubscriptionInterval.Daily => DateTime.Now.AddDays(1),
                            SubscriptionInterval.Weekly => DateTime.Now.AddWeeks(1),
                            SubscriptionInterval.Monthly => DateTime.Now.AddMonths(1),
                            SubscriptionInterval.Yearly => DateTime.Now.AddYears(1),
                            _ => DateTime.Now.AddMonths(1),
                        };
                    }

                    return StepResult.Success($"Calculated next due date for subscription ID {subscription.Id}.", new()
                    {
                        ["NextDueDate"] = nextDueDate
                    });
                })
                .InParallel()
                .Build();

            _builder.Step("UpdateSubscription")
                .After("CalculateTotalInvestments")
                .After("GetNextDueDate")
                .Execute(async context =>
                {
                    var payment = context.GetData<PaymentData>("Payment");
                    var subscription = context.GetData<SubscriptionData>("Subscription");
                    var totalInvestments = context.GetData<decimal>("TotalInvestments");
                    var nextDueDate = context.GetData<DateTime>("NextDueDate");

                    // Update subscription with new investment total and next due date
                    var updatedFields = new Dictionary<string, object>
                    {
                        ["LastPayment"] = payment.CreatedAt,
                        ["NextDueDate"] = nextDueDate,
                        ["TotalInvestments"] = totalInvestments,
                        ["Status"] = SubscriptionStatus.Active
                    };

                    var updateResult = await _subscriptionService.UpdateAsync(subscription.Id, updatedFields);

                    if (!updateResult.IsSuccess)
                    {
                        throw new DatabaseException($"Failed to update subscription {subscription.Id} with payment details: {updateResult.ErrorMessage}");
                    }

                    return StepResult.Success($"Calculated next due date for subscription ID {subscription.Id}.", new()
                    {
                        ["Subscription"] = updateResult.Data.Documents.FirstOrDefault()
                    });
                })
                .Critical()
                .Build();

            _builder.Step("NotifyUser")
                .After("UpdateSubscription")
                .Execute(async context =>
                {
                    var subscription = context.GetData<SubscriptionData>("Subscription");
                    var payment = context.GetData<PaymentData>("Payment");

                    await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                    {
                        UserId = subscription.UserId.ToString(),
                        Message = $"Payment of {payment.NetAmount} {payment.Currency} processed for your subscription."
                    });

                    return StepResult.Success($"User {subscription.UserId} notified about successful payment.");
                })
                .Build();
        }
    }
}
