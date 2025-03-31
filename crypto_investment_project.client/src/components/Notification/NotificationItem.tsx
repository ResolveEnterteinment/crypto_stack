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

        // If less than a day, show relative time
        if (diff < 24 * 60 * 60 * 1000) {
            const hours = Math.floor(diff / (60 * 60 * 1000));

            if (hours < 1) {
                const minutes = Math.floor(diff / (60 * 1000));
                return minutes < 1 ? 'Just now' : `${minutes}m ago`;
            }

            return `${hours}h ago`;
        }

        // If less than a week, show day of week
        if (diff < 7 * 24 * 60 * 60 * 1000) {
            return date.toLocaleDateString(undefined, { weekday: 'long' });
        }

        // Otherwise show full date
        return date.toLocaleDateString();
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

    // Determine icon based on notification type (you can extend this based on your app's notification types)
    const getNotificationIcon = () => {
        // This is a simple example - you would want to parse the message or use a type field
        if (data.message.toLowerCase().includes('payment')) {
            return (
                <div className="bg-green-100 rounded-full p-2 flex-shrink-0">
                    <svg className="h-5 w-5 text-green-600" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8c-1.657 0-3 .895-3 2s1.343 2 3 2 3 .895 3 2-1.343 2-3 2m0-8c1.11 0 2.08.402 2.599 1M12 8V7m0 1v8m0 0v1m0-1c-1.11 0-2.08-.402-2.599-1M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                    </svg>
                </div>
            );
        }

        if (data.message.toLowerCase().includes('subscription')) {
            return (
                <div className="bg-blue-100 rounded-full p-2 flex-shrink-0">
                    <svg className="h-5 w-5 text-blue-600" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9" />
                    </svg>
                </div>
            );
        }

        // Default icon
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
            className={`py-3 transition-all duration-200 hover:bg-gray-50 ${!data.isRead ? 'border-l-4 border-blue-500 pl-3' : 'pl-4'}`}
        >
            <div className="flex gap-3 items-start">
                {getNotificationIcon()}

                <div className="flex-1 min-w-0">
                    <p className={`text-sm ${data.isRead ? 'text-gray-500' : 'text-gray-800 font-medium'}`}>
                        {data.message}
                    </p>
                    <div className="flex items-center justify-between mt-1">
                        <span className="text-xs text-gray-500">
                            {formatDate(data.createdAt)}
                        </span>

                        {!data.isRead && (
                            <button
                                className={`text-xs font-medium text-blue-600 hover:text-blue-800 transition-colors flex items-center ${isMarking ? 'opacity-50 cursor-not-allowed' : ''}`}
                                onClick={onMarkAsRead}
                                disabled={isMarking}
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
                                    'Mark as read'
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