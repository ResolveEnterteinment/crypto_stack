import React, { useEffect, useState } from "react";
import { getNotifications, markNotificationAsRead, connectToNotifications } from "../services/notification";
import { useAuth } from "../context/AuthContext";

interface Notification {
    id: string;
    message: string;
    createdAt: string;
    isRead: boolean;
}

const Notifications: React.FC<{ onUpdateUnread: (count: number) => void }> = ({ onUpdateUnread }) => {
    const { user } = useAuth();
    const [notifications, setNotifications] = useState<Notification[]>([]);
    const [newNotification, setNewNotification] = useState<string | null>(null);

    useEffect(() => {
        if (!user || !user.id) return;

        fetchNotifications(user.id);

        const connection = connectToNotifications(user.id, (message) => {
            setNewNotification(message);
            fetchNotifications(user.id);
        });

        return () => {
            connection.stop();
        };
    }, [user]);

    const fetchNotifications = async (id: string) => {
        if (!id) return;

        const data = await getNotifications(id);
        setNotifications(data);

        // Update unread count in the Navbar
        const unreadCount = data.filter((n: { isRead: any; }) => !n.isRead).length;
        onUpdateUnread(unreadCount);
    };

    const handleRead = async (notificationId: string) => {
        await markNotificationAsRead(notificationId);
        fetchNotifications(user!.id);
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
