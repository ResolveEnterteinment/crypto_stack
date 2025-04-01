import React, { useState } from "react";
import INotification from "./INotification";

interface NotificationItemProps {
    data: INotification;
    handleRead: (notificationId: string) => void;
}

const NotificationItem: React.FC<NotificationItemProps> = ({ data, handleRead }) => {
    const [isMarking, setIsMarking] = useState(false);

    // Format the date to be more user-friendly
    const formatDate = (dateString: string) => {
        const date = new Date(dateString);
        const now = new Date();
        const diff = now.getTime() - date.getTime();

        // Just now (less than a minute ago)
        if (diff < 60 * 1000) {
            return 'Just now';
        }

        // Minutes ago (less than an hour)
        if (diff < 60 * 60 * 1000) {
            const minutes = Math.floor(diff / (60 * 1000));
            return `${minutes}m ago`;
        }

        // Hours ago (less than a day)
        if (diff < 24 * 60 * 60 * 1000) {
            const hours = Math.floor(diff / (60 * 60 * 1000));
            return `${hours}h ago`;
        }

        // Yesterday
        if (diff < 48 * 60 * 60 * 1000) {
            return 'Yesterday';
        }

        // Day of week (less than a week)
        if (diff < 7 * 24 * 60 * 60 * 1000) {
            return date.toLocaleDateString(undefined, { weekday: 'long' });
        }

        // Full date for older notifications
        return date.toLocaleDateString(undefined, {
            month: 'short',
            day: 'numeric',
            year: now.getFullYear() !== date.getFullYear() ? 'numeric' : undefined
        });
    };

    // Handle mark as read with loading state
    const onMarkAsRead = async () => {
        if (isMarking) return;

        setIsMarking(true);
        try {
            await handleRead(data.id);
        } finally {
            setIsMarking(false);
        }
    };

    // Determine notification icon based on content analysis
    const NotificationIcon = () => {
        const message = data.message.toLowerCase();

        // Payment related notification
        if (message.includes('payment') || message.includes('transaction') || message.includes('deposit')) {
            return (
                <div className="bg-green-100 rounded-full p-2 flex-shrink-0">
                    <svg className="h-5 w-5 text-green-600" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8c-1.657 0-3 .895-3 2s1.343 2 3 2 3 .895 3 2-1.343 2-3 2m0-8c1.11 0 2.08.402 2.599 1M12 8V7m0 1v8m0 0v1m0-1c-1.11 0-2.08-.402-2.599-1M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                    </svg>
                </div>
            );
        }

        // Subscription related notification
        if (message.includes('subscription') || message.includes('invested')) {
            return (
                <div className="bg-blue-100 rounded-full p-2 flex-shrink-0">
                    <svg className="h-5 w-5 text-blue-600" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 7h8m0 0v8m0-8l-8 8-4-4-6 6" />
                    </svg>
                </div>
            );
        }

        // System notification
        if (message.includes('system') || message.includes('update') || message.includes('maintenance')) {
            return (
                <div className="bg-yellow-100 rounded-full p-2 flex-shrink-0">
                    <svg className="h-5 w-5 text-yellow-600" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                    </svg>
                </div>
            );
        }

        // Default icon for other notifications
        return (
            <div className="bg-gray-100 rounded-full p-2 flex-shrink-0">
                <svg className="h-5 w-5 text-gray-600" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
            </div>
        );
    };

    return (
        <div
            className={`py-3 px-4 transition-all duration-200 hover:bg-gray-50 ${!data.isRead ? 'bg-blue-50 bg-opacity-30' : ''
                }`}
        >
            <div className="flex gap-3 items-start">
                <NotificationIcon />

                <div className="flex-1 min-w-0">
                    <p className={`text-sm ${data.isRead ? 'text-gray-600' : 'text-gray-800 font-medium'
                        }`}>
                        {data.message}
                    </p>

                    <div className="flex items-center justify-between mt-1">
                        <span className="text-xs text-gray-500">
                            {formatDate(data.createdAt)}
                        </span>

                        {!data.isRead && (
                            <button
                                className={`text-xs font-medium text-blue-600 hover:text-blue-800 transition-colors flex items-center ${isMarking ? 'opacity-50 cursor-not-allowed' : ''
                                    }`}
                                onClick={onMarkAsRead}
                                disabled={isMarking}
                                aria-label="Mark as read"
                            >
                                {isMarking ? (
                                    <>
                                        <svg className="animate-spin -ml-1 mr-1 h-3 w-3 text-blue-600" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                                            <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                                            <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                                        </svg>
                                        Marking...
                                    </>
                                ) : (
                                    <>
                                        <svg className="w-3 h-3 mr-1" fill="none" stroke="currentColor" viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg">
                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M5 13l4 4L19 7"></path>
                                        </svg>
                                        Mark as read
                                    </>
                                )}
                            </button>
                        )}
                    </div>
                </div>
            </div>
        </div>
    );
};

export default NotificationItem;