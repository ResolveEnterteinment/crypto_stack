// Infrastructure/Services/Subscription/SubscriptionRetryService.cs
using Application.Interfaces;
using Application.Interfaces.Logging;
using Application.Interfaces.Payment;
using Application.Interfaces.Subscription;
using Domain.Constants;
using Domain.Events;
using Domain.Models.Payment;
using Domain.Models.Subscription;
using MediatR;

namespace Infrastructure.Services.Subscription
{
    public class SubscriptionRetryService :
        INotificationHandler<SubscriptionPaymentFailedEvent>,
        ISubscriptionRetryService
    {
        private readonly ISubscriptionService _subscriptionService;
        private readonly IPaymentService _paymentService;
        private readonly INotificationService _notificationService;
        private readonly ILoggingService _logger;
        private readonly IEventService _eventService;

        // Configure retry settings
        private readonly int[] _retryIntervalHours = new[] { 24, 72, 168 }; // 1 day, 3 days, 7 days
        private readonly int _maxRetryAttempts = 3;

        public SubscriptionRetryService(
            ISubscriptionService subscriptionService,
            IPaymentService paymentService,
            INotificationService notificationService,
            ILoggingService logger,
            IEventService eventService)
        {
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
            _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _eventService = eventService ?? throw new ArgumentNullException(nameof(eventService));
        }

        /// <summary>
        /// Handle payment failure event
        /// </summary>
        public async Task Handle(SubscriptionPaymentFailedEvent notification, CancellationToken cancellationToken)
        {
            using var scope = _logger.BeginScope("SubscriptionRetryService::Handle", new Dictionary<string, object>
            {
                ["PaymentId"] = notification.Payment.Id,
                ["SubscriptionId"] = notification.Payment.SubscriptionId,
                ["AttemptCount"] = notification.AttemptCount
            });

            try
            {
                var payment = notification.Payment;

                // Get subscription
                var subscriptionResult = await _subscriptionService.GetByIdAsync(payment.SubscriptionId);
                if (subscriptionResult == null || !subscriptionResult.IsSuccess || subscriptionResult.Data == null)
                {
                    _logger.LogError("Failed to get subscription {SubscriptionId} for failed payment",
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

                // Update payment with retry information
                await _paymentService.UpdatePaymentRetryInfoAsync(
                    payment.Id,
                    attemptCount + 1,
                    DateTime.UtcNow,
                    nextRetryAt,
                    notification.FailureReason);

                // Notify user
                await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                {
                    UserId = subscription.UserId.ToString(),
                    Message = $"Payment for your subscription failed. Reason: {notification.FailureReason}. We'll try again on {nextRetryAt:yyyy-MM-dd}.",
                    IsRead = false
                });

                _logger.LogInformation(
                    "Scheduled retry for subscription {SubscriptionId} payment, attempt {AttemptCount}, next retry at {NextRetryAt}",
                    subscription.Id, attemptCount + 1, nextRetryAt);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error handling payment failure for payment {notification.Payment.Id} : {ex.Message}");
            }
        }

        /// <summary>
        /// Handle the case when max retry attempts have been exceeded
        /// </summary>
        private async Task HandleMaxRetriesExceeded(
            SubscriptionData subscription,
            PaymentData payment,
            string failureReason)
        {
            using var scope = _logger.BeginScope("SubscriptionRetryService::HandleMaxRetriesExceeded", new Dictionary<string, object>
            {
                ["SubscriptionId"] = subscription.Id,
                ["PaymentId"] = payment.Id
            });

            try
            {
                // Update subscription status to suspended
                await _subscriptionService.UpdateSubscriptionStatusAsync(subscription.Id, SubscriptionStatus.Suspended);

                // Send notification to user
                await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                {
                    UserId = subscription.UserId.ToString(),
                    Message = $"Your subscription has been suspended due to payment failures. Reason: {failureReason}. Please update your payment method.",
                    IsRead = false
                });

                _logger.LogInformation(
                    "Subscription {SubscriptionId} suspended after {MaxRetries} failed payment attempts",
                    subscription.Id, _maxRetryAttempts);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error suspending subscription {subscription.Id} after max retries: {ex.Message}");
            }
        }

        /// <summary>
        /// Process any pending payment retries that are due
        /// </summary>
        public async Task ProcessPendingRetriesAsync()
        {
            using var scope = _logger.BeginScope("SubscriptionRetryService::ProcessPendingRetriesAsync");

            try
            {
                var pendingRetries = await _paymentService.GetPendingRetriesAsync();
                if (pendingRetries == null || !pendingRetries.IsSuccess || pendingRetries.Data == null)
                {
                    _logger.LogWarning("Failed to fetch pending payment retries");
                    return;
                }

                var retryPayments = pendingRetries.Data;
                _logger.LogInformation("Processing {Count} pending payment retries", retryPayments.Count());

                foreach (var payment in retryPayments)
                {
                    try
                    {
                        // Attempt to retry the payment
                        var retryResult = await _paymentService.RetryPaymentAsync(payment.Id);

                        if (retryResult.IsSuccess)
                        {
                            _logger.LogInformation("Successfully retried payment {PaymentId} for subscription {SubscriptionId}",
                                payment.Id, payment.SubscriptionId);

                            // Notify user of successful retry
                            await _notificationService.CreateAndSendNotificationAsync(new NotificationData
                            {
                                UserId = payment.UserId.ToString(),
                                Message = $"Payment for your subscription has been successfully processed.",
                                IsRead = false
                            });
                        }
                        else
                        {
                            // Payment failed again, publish event to handle the failure
                            await _eventService.PublishAsync(new SubscriptionPaymentFailedEvent(
                                payment,
                                retryResult.ErrorMessage ?? "Payment retry failed",
                                payment.AttemptCount + 1,
                                _logger.Context
                            ));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error processing retry for payment {payment.Id}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing pending payment retries: {ex.Message}");
            }
        }
    }
}