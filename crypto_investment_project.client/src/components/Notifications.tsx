import React, { useEffect, useState } from "react";
import { getNotifications, markNotificationAsRead, connectToNotifications } from "../services/notification";

interface Notification {
    id: string;
    message: string;
    createdAt: string;
    isRead: boolean;
}

const Notifications: React.FC<{ userId: string }> = ({ userId }) => {
    const [notifications, setNotifications] = useState<Notification[]>([]);
    const [newNotification, setNewNotification] = useState<string | null>(null);

    useEffect(() => {
        fetchNotifications();
        const connection = connectToNotifications(userId, (message) => {
            setNewNotification(message);
            fetchNotifications();
        });

        return () => {
            connection.stop();
        };
    }, []);

    const fetchNotifications = async () => {
        const data = await getNotifications(userId);
        setNotifications(data);
    };

    const handleRead = async (notificationId: string) => {
        await markNotificationAsRead(notificationId);
        fetchNotifications();
    };

    return (
        <div className="absolute top-14 right-4 bg-white shadow-xl rounded-lg w-80 p-4 max-h-96 overflow-auto z-50">
            <h3 className="text-lg font-semibold mb-2">Notifications</h3>
            {newNotification && (
                <div className="bg-blue-100 p-2 rounded text-blue-800">
                    🔔 {newNotification}
                </div>
            )}
            {notifications.length === 0 && <p className="text-gray-500">No notifications</p>}
            {notifications.map((notification) => (
                <div key={notification.id} className={`border-b py-2 ${notification.isRead ? "text-gray-400" : ""}`}>
                    <p>{notification.message}</p>
                    <small className="text-xs text-gray-500">
                        {new Date(notification.createdAt).toLocaleString()}
                    </small>
                    {!notification.isRead && (
                        <button className="text-blue-500 text-sm ml-2" onClick={() => handleRead(notification.id)}>
                            Mark as read
                        </button>
                    )}
                </div>
            ))}
        </div>
    );
};

export default Notifications;
