import React, { useState, useEffect, useRef } from "react";
import { getNotifications, markNotificationAsRead, connectToNotifications } from "../../services/notification";
import { useAuth } from "../../context/AuthContext";
import { BellIcon } from '@heroicons/react/24/outline';
import NotificationItem from "./NotificationItem";
import INotification from "./INotification";

const NotificationPanel: React.FC<{}> = ({ }) => {
    const [showNotifications, setShowNotifications] = useState(false);
    const notificationRef = useRef<HTMLDivElement>(null); // ✅ Reference for outside click detection
    const { user } = useAuth();
    const [notifications, setNotifications] = useState<INotification[]>([]);
    const [_, setNewNotification] = useState<string | null>(null);
    const [unreadCount, setUnreadCount] = useState(0);

    useEffect(() => {
        if (!user || !user.id) return;

        fetchNotifications(user.id);

        const connection = connectToNotifications(user.id, (message) => {
            console.log("Received a new notification: ", message);
            setNewNotification(message);
            fetchNotifications(user.id);
        });

        return () => {
            connection.stop();
        };
    }, [user]);

    // ✅ Close notifications when clicking outside
    useEffect(() => {
        const handleClickOutside = (event: MouseEvent) => {
            if (notificationRef.current && !notificationRef.current.contains(event.target as Node)) {
                setShowNotifications(false);
            }
        };

        document.addEventListener("mousedown", handleClickOutside);
        return () => {
            document.removeEventListener("mousedown", handleClickOutside);
        };
    }, []);

    const fetchNotifications = async (id: string) => {
        if (!id) return;

        const data = await getNotifications(id);
        setNotifications(data);

        // Update unread count in the Navbar
        const unreadCount = data.filter((n: { isRead: any; }) => !n.isRead).length;
        setUnreadCount(unreadCount);
    };

    const handleRead = async (notificationId: string) => {
        await markNotificationAsRead(notificationId);
        fetchNotifications(user!.id);
    };

    return (
        <div ref={notificationRef} className="relative">
            <button
                type="button"
                onClick={() => setShowNotifications(!showNotifications)}
                className="relative rounded-full bg-gray-800 p-1 text-gray-400 hover:text-white focus:ring-2 focus:ring-white focus:ring-offset-2 focus:ring-offset-gray-800 focus:outline-none"
            >
                <span className="sr-only">View notifications</span>
                <BellIcon aria-hidden="true" className="size-6" />
                {unreadCount > 0 && (
                    <span className="absolute top-0 right-0 flex h-3 w-3 items-center justify-center rounded-full bg-red-500 text-xs font-bold text-white">
                        {unreadCount}
                    </span>
                )}
            </button>

            {showNotifications && <div className="absolute top-14 right-4 bg-white shadow-xl rounded-lg w-80 p-4 max-h-96 overflow-auto z-50">
                <h3 className="text-lg font-semibold mb-2">Notifications</h3>
                {notifications.length === 0 && <p className="text-gray-500">No notifications</p>}
                {notifications.map((notification) => (
                    <NotificationItem data={notification} handleRead={handleRead} />
                ))}
            </div>}
        </div>
    );
};

export default NotificationPanel;
