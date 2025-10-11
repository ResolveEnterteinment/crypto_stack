using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Application.Interfaces.Payment;
using Application.Interfaces.Subscription;
using Domain.Constants.Logging;
using Domain.Constants.Subscription;
using Domain.DTOs.Base;
using Domain.DTOs.Logging;
using Domain.Events.Subscription;
using Domain.Exceptions;
using Domain.Models.Payment;
using Domain.Models.Subscription;
using MediatR;

namespace Infrastructure.Services.Subscription
{
    public class SubscriptionRetryService :
        ISubscriptionRetryService,
        INotificationHandler<SubscriptionPaymentFailedEvent>
    {
        private readonly IResilienceService<SubscriptionData> _resilienceService;
        private readonly ILoggingService _loggingService;
        private readonly IEventService _eventService;
        private readonly INotificationService _notificationService;
        private readonly ISubscriptionService _subscriptionService;
        private readonly IPaymentService _paymentService;

        // Configure retry settings
        private readonly int[] _retryIntervalHours = new[] { 24, 72, 168 }; // 1 day, 3 days, 7 days
        private readonly int _maxRetryAttempts = 3;

        public SubscriptionRetryService(
            IResilienceService<SubscriptionData> resilienceService,
            ILoggingService loggingService,
            IEventService eventService,
            INotificationService notificationService,
            ISubscriptionService subscriptionService,
            IPaymentService paymentService)
        {
            _resilienceService = resilienceService ?? throw new ArgumentNullException(nameof(resilienceService));
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
        }

        /// <summary>
        /// Handle payment failure event
        /// </summary>
        public Task Handle(SubscriptionPaymentFailedEvent notification, CancellationToken cancellationToken)
        {
            return _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Subscription",
                    FileName = "SubscriptionRetryService",
                    OperationName = "Handle(SubscriptionPaymentFailedEvent notification, CancellationToken cancellationToken)",
                    State =
                    {
                        ["PaymentId"] = notification.Payment.Id,
                        ["SubscriptionId"] = notification.Payment.SubscriptionId,
                        ["AttemptCount"] = notification.AttemptCount
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    var payment = notification.Payment;

                    // Get subscription - External call (already has database resilience)
                    var subscriptionResult = await _subscriptionService.GetByIdAsync(payment.SubscriptionId);
                    if (subscriptionResult == null || !subscriptionResult.IsSuccess || subscriptionResult.Data == null)
                    {
                        _loggingService.LogError("Failed to get subscription {SubscriptionId} for failed payment",
                            payment.SubscriptionId);
                        return;
                    }

                    var subscription = subscriptionResult.Data;
                    var attemptCount = notification.AttemptCount;

                    // Check if we should retry
                    if (attemptCount >= _maxRetryAttempts)
                    {
                        await HandleMaxRetriesExceeded(subscription, payment, notification.FailureReason);
                        return;
                    }

                    // Calculate next retry time
                    var nextRetryHours = _retryIntervalHours[Math.Min(attemptCount, _retryIntervalHours.Length - 1)];
                    var nextRetryAt = DateTime.UtcNow.AddHours(nextRetryHours);

                    // Update payment with retry information - External call (already has database resilience)
                    await _paymentService.UpdatePaymentRetryInfoAsync(
                        payment.Id,
                        attemptCount + 1,
                        DateTime.UtcNow,
                        nextRetryAt,
                        notification.FailureReason);

                    // Notify user - External call (may have HTTP resilience)
                    await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                    {
                        UserId = subscription.UserId.ToString(),
                        Message = $"Payment for your subscription failed. Reason: {notification.FailureReason}. We'll try again on {nextRetryAt:yyyy-MM-dd}.",
                        IsRead = false
                    });

                    _loggingService.LogInformation(
                        "Scheduled retry for subscription {SubscriptionId} payment, attempt {AttemptCount}, next retry at {NextRetryAt}",
                        subscription.Id, attemptCount + 1, nextRetryAt);
                })
                .WithContext("Operation", "PaymentFailureHandling")
                .WithContext("PaymentProvider", "Stripe")
                .OnError(async (ex) =>
                {
                    _loggingService.LogError("Critical error handling payment failure for subscription {SubscriptionId}: {Error}",
                        notification.Payment.SubscriptionId, ex.Message);
                })
                .ExecuteAsync();
        }

        /// <summary>
        /// Handle the case when max retry attempts have been exceeded
        /// </summary>
        private Task HandleMaxRetriesExceeded(
            SubscriptionData subscription,
            PaymentData payment,
            string failureReason)
        {
            return _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Subscription",
                    FileName = "SubscriptionRetryService",
                    OperationName = "HandleMaxRetriesExceeded(SubscriptionData subscription, PaymentData payment, string failureReason)",
                    State =
                    {
                        ["SubscriptionId"] = subscription.Id,
                        ["PaymentId"] = payment.Id,
                        ["FailureReason"] = failureReason
                    },
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    // Update subscription status to suspended - External call (already has database resilience)
                    await _subscriptionService.UpdateSubscriptionStatusAsync(subscription.Id, SubscriptionStatus.Suspended);

                    // Send notification to user - External call (may have HTTP resilience)
                    await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                    {
                        UserId = subscription.UserId.ToString(),
                        Message = $"Your subscription has been suspended due to payment failures. Reason: {failureReason}. Please update your payment method.",
                        IsRead = false
                    });

                    _loggingService.LogInformation(
                        "Subscription {SubscriptionId} suspended after {MaxRetries} failed payment attempts",
                        subscription.Id, _maxRetryAttempts);
                })
                .WithContext("Operation", "SubscriptionSuspension")
                .WithContext("Reason", failureReason)
                .ExecuteAsync();
        }

        /// <summary>
        /// Process any pending payment retries that are due
        /// </summary>
        public Task ProcessPendingRetriesAsync()
        {
            return _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Subscription",
                    FileName = "SubscriptionRetryService",
                    OperationName = "ProcessPendingRetriesAsync()",
                    State = [],
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    // Get pending retries - External call (already has database resilience)
                    var pendingRetriesResult = await _paymentService.GetPendingRetriesAsync();
                    if (pendingRetriesResult == null || !pendingRetriesResult.IsSuccess || pendingRetriesResult.Data == null)
                    {
                        _loggingService.LogWarning("Failed to fetch pending payment retries");
                        throw new InvalidOperationException("Failed to fetch pending payment retries");
                    }

                    var pendingRetries = pendingRetriesResult.Data;
                    _loggingService.LogInformation("Processing {Count} pending payment retries", pendingRetries.Count);

                    foreach (var payment in pendingRetries)
                    {
                        await ProcessSinglePendingPayment(payment);
                        // Continue processing other payments instead of failing the entire batch
                    }
                })
                .WithContext("Operation", "BatchPaymentRetryProcessing")
                .WithPerformanceMonitoring(TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1)) // Monitor performance
                .ExecuteAsync();
        }

        private Task ProcessSinglePendingPayment(PaymentData payment)
        {
            return _resilienceService.CreateBuilder(
                new Scope
                {
                    NameSpace = "Infrastructure.Services.Subscription",
                    FileName = "SubscriptionRetryService",
                    OperationName = "ProcessSinglePendingPayment(PaymentData payment)",
                    State = [],
                    LogLevel = LogLevel.Error
                },
                async () =>
                {
                    // Attempt to retry the payment - External call (already has resilience)
                    var retryResult = await _paymentService.RetryPaymentAsync(payment.Id);
                    if (retryResult == null || !retryResult.IsSuccess)
                        throw new PaymentApiException($"Failed to retry payment {payment.Id}: {retryResult?.ErrorMessage}", payment.Provider);
                })
                .OnSuccess(async () =>
                {
                    // Notify user of successful retry - External call (may have HTTP resilience)
                    await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                    {
                        UserId = payment.UserId.ToString(),
                        Message = $"Payment for your subscription has been successfully processed.",
                        IsRead = false
                    });
                })
                .OnError(async (ex) =>
                {
                    // Payment failed again, publish event to handle the failure - External call
                    await _eventService.PublishAsync(new SubscriptionPaymentFailedEvent(
                        payment,
                        ex.Message ?? "Payment retry failed",
                        payment.AttemptCount + 1,
                        _loggingService.Context
                    ));
                })
                .ExecuteAsync();
        }
    }
}