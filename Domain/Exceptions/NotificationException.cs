using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Domain.Exceptions
{
    /// <summary>
    /// Exception thrown when a payment processing error occurs
    /// </summary>
    [Serializable]
    public class NotificationException : DomainException
    {
        /// <summary>
        /// Gets the payment provider.
        /// </summary>
        public NotificationData Notification { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PaymentApiException"/> class.
        /// </summary>
        /// <param name="notification">The <see cref="NotificationData"/>.</param>
        public NotificationException(NotificationData notification)
            : base($"Failed to send real-time notification to user {notification.UserId}", "REALTIME_NOTIFICATION_ERROR")
        {
            Notification = notification;

            _ = AddContext("Notification", notification.Id);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PaymentApiException"/> class.
        /// </summary>
        /// <param name="notification">The <see cref="NotificationData"/>.</param>
        /// <param name="innerException">The inner exception.</param>
        public NotificationException(NotificationData notification, Exception innerException)
            : base($"Failed to send real-time notification to user {notification.UserId}", "REALTIME_NOTIFICATION_ERROR", innerException)
        {
            Notification = notification;

            _ = AddContext("Notification", notification);
        }

        /// <summary>
        /// Used for serialization purposes.
        /// </summary>
        protected NotificationException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            Notification = (NotificationData)info.GetValue(
                nameof(Notification), typeof(NotificationData));
        }
    }
}
