import React, { useState, useEffect, useRef, useCallback } from "react";
import {
    getNotifications,
    markNotificationAsRead,
    connectToNotifications,
    connectionErrors,
    markAllAsRead,
    forceReconnect
} from "../../services/notification";
import { useAuth } from "../../context/AuthContext";
import { BellIcon } from '@heroicons/react/24/outline';
import NotificationItem from "./NotificationItem";
import INotification from "./INotification";
import { HubConnection, HubConnectionState } from "@microsoft/signalr";

const NotificationPanel: React.FC = () => {
    const [showNotifications, setShowNotifications] = useState(false);
    const [notifications, setNotifications] = useState<INotification[]>([]);
    const [unreadCount, setUnreadCount] = useState(0);
    const [isLoading, setIsLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [connectionStatus, setConnectionStatus] = useState<'connected' | 'disconnected' | 'connecting'>('disconnected');
    const [showConnectionDetails, setShowConnectionDetails] = useState(false);

    const notificationRef = useRef<HTMLDivElement>(null);
    const hubConnectionRef = useRef<HubConnection | null>(null);
    const connectionCheckInterval = useRef<NodeJS.Timeout | null>(null);

    const { user } = useAuth();

    // Fetch notifications from the API
    const fetchNotifications = useCallback(async () => {
        if (!user?.id) return;

        setIsLoading(true);
        setError(null);

        try {
            const data = await getNotifications(user.id);
            setNotifications(data);

            // Update unread count
            const unreadNotifications = data.filter((n: INotification) => !n.isRead).length;
            setUnreadCount(unreadNotifications);
        } catch (err) {
            console.error("Failed to fetch notifications:", err);
            setError("Unable to load notifications");
        } finally {
            setIsLoading(false);
        }
    }, [user?.id]);

    // Handle notification marking as read
    const handleRead = async (notificationId: string) => {
        try {
            await markNotificationAsRead(notificationId);

            // Update local state for immediate UI update
            setNotifications(prev =>
                prev.map(notification =>
                    notification.id === notificationId
                        ? { ...notification, isRead: true }
                        : notification
                )
            );

            // Update unread count
            setUnreadCount(prev => Math.max(0, prev - 1));
        } catch (err) {
            console.error("Failed to mark notification as read:", err);
            // We don't show UI errors for individual notification marking
        }
    };

    // Handle new notification received from SignalR
    const handleNewNotification = useCallback((message: string) => {
        // Refetch notifications to get the new one with proper formatting
        fetchNotifications();

        // Show browser notification if permission granted
        if (Notification.permission === "granted") {
            new Notification("New Notification", { body: message });
        }
    }, [fetchNotifications]);

    // Update connection status based on actual connection state
    const updateConnectionStatus = useCallback(() => {
        if (!hubConnectionRef.current) {
            setConnectionStatus('disconnected');
            return;
        }

        const currentState = hubConnectionRef.current.state;

        if (currentState === HubConnectionState.Connected) {
            setConnectionStatus('connected');
        } else if (currentState === HubConnectionState.Connecting ||
            currentState === HubConnectionState.Reconnecting) {
            setConnectionStatus('connecting');
        } else {
            setConnectionStatus('disconnected');
        }
    }, []);

    // Handle SignalR connection
    useEffect(() => {
        if (!user?.id) return;

        // Clear previous connection if exists
        if (hubConnectionRef.current) {
            hubConnectionRef.current.stop().catch(err =>
                console.warn("Error stopping previous connection:", err)
            );
        }

        setConnectionStatus('connecting');

        // Connect to SignalR hub
        const connection = connectToNotifications(user.id, handleNewNotification);
        hubConnectionRef.current = connection;

        // Initial fetch of notifications
        fetchNotifications();

        // Request notification permission
        if (Notification.permission !== 'granted' && Notification.permission !== 'denied') {
            Notification.requestPermission();
        }

        // Setup a periodic status check to ensure UI always reflects actual state
        if (connectionCheckInterval.current) {
            clearInterval(connectionCheckInterval.current);
        }

        connectionCheckInterval.current = setInterval(() => {
            updateConnectionStatus();
        }, 3000);

        // Cleanup function
        return () => {
            if (connectionCheckInterval.current) {
                clearInterval(connectionCheckInterval.current);
                connectionCheckInterval.current = null;
            }

            if (hubConnectionRef.current) {
                hubConnectionRef.current.stop().catch(err =>
                    console.warn("Error stopping connection during cleanup:", err)
                );
                hubConnectionRef.current = null;
            }
        };
    }, [user?.id, handleNewNotification, fetchNotifications, updateConnectionStatus]);

    // Handle outside clicks to close the notification panel
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

    // Mark all as read functionality with improved error handling
    const handleMarkAllAsRead = async () => {
        if (notifications.length === 0 || unreadCount === 0) return;

        const unreadNotifications = notifications.filter(n => !n.isRead);

        try {
            // Use the batch operation function
            const result = await markAllAsRead(unreadNotifications.map(n => n.id));

            if (result.failedIds.length > 0) {
                console.warn(`Failed to mark ${result.failedIds.length} notifications as read`);
            }

            // Update local state - mark all as read regardless of individual failures
            setNotifications(prev =>
                prev.map(notification => ({ ...notification, isRead: true }))
            );

            setUnreadCount(0);
        } catch (err) {
            console.error("Failed to mark all notifications as read:", err);
            setError("Failed to mark all as read. Please try again.");
            setTimeout(() => setError(null), 3000);
        }
    };

    // Handle manual reconnection
    const handleReconnect = async () => {
        setConnectionStatus('connecting');
        setError(null);

        try {
            if (user?.id) {
                await forceReconnect(user.id, handleNewNotification);
                // The connection status will be updated automatically via the interval
            }
        } catch (err) {
            console.error("Manual reconnection failed:", err);
            setError("Reconnection failed. Please try again later.");
            setConnectionStatus('disconnected');
        }
    };

    return (
        <div ref={notificationRef} className="relative">
            <button
                type="button"
                onClick={() => setShowNotifications(!showNotifications)}
                className="relative rounded-full bg-gray-800 p-1 text-gray-400 hover:text-white focus:ring-2 focus:ring-white focus:ring-offset-2 focus:ring-offset-gray-800 focus:outline-none"
                aria-label="View notifications"
            >
                <BellIcon aria-hidden="true" className="h-6 w-6" />
                {unreadCount > 0 && (
                    <span className="absolute top-0 right-0 inline-flex items-center justify-center px-2 py-1 text-xs font-bold leading-none transform translate-x-1/2 -translate-y-1/2 rounded-full bg-red-500 text-white">
                        {unreadCount > 99 ? '99+' : unreadCount}
                    </span>
                )}
            </button>

            {showNotifications && (
                <div className="absolute top-14 right-4 bg-white shadow-xl rounded-lg w-80 p-4 max-h-96 overflow-auto z-50 border border-gray-200 dark:border-gray-700">
                    <div className="flex justify-between items-center mb-2">
                        <h3 className="text-lg font-semibold">Notifications</h3>
                        {unreadCount > 0 && (
                            <button
                                onClick={handleMarkAllAsRead}
                                className="text-xs text-blue-600 hover:text-blue-800"
                            >
                                Mark all as read
                            </button>
                        )}
                    </div>

                    {/* Connection status indicator */}
                    <div className="mb-2">
                        <div
                            onClick={() => setShowConnectionDetails(!showConnectionDetails)}
                            className={`flex items-center text-xs cursor-pointer ${connectionStatus === 'connected'
                                    ? 'text-green-600'
                                    : connectionStatus === 'connecting'
                                        ? 'text-yellow-600'
                                        : 'text-red-600'
                                }`}
                        >
                            <div className={`h-2 w-2 rounded-full mr-1 ${connectionStatus === 'connected'
                                    ? 'bg-green-600 animate-pulse'
                                    : connectionStatus === 'connecting'
                                        ? 'bg-yellow-600 animate-pulse'
                                        : 'bg-red-600'
                                }`}></div>
                            {connectionStatus === 'connected'
                                ? 'Connected'
                                : connectionStatus === 'connecting'
                                    ? 'Connecting...'
                                    : 'Disconnected'}

                            <svg
                                className={`w-3 h-3 ml-1 transition-transform ${showConnectionDetails ? 'rotate-180' : ''}`}
                                fill="none"
                                stroke="currentColor"
                                viewBox="0 0 24 24"
                            >
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M19 9l-7 7-7-7" />
                            </svg>
                        </div>

                        {/* Connection error details */}
                        {showConnectionDetails && (
                            <div className="mt-1 text-xs">
                                {connectionStatus !== 'connected' && connectionErrors.lastError && (
                                    <div className="bg-red-50 p-2 rounded text-red-700 mb-2">
                                        <p><strong>Error:</strong> {connectionErrors.lastError}</p>
                                        {connectionErrors.lastErrorTime && (
                                            <p><strong>Time:</strong> {connectionErrors.lastErrorTime.toLocaleTimeString()}</p>
                                        )}
                                        <p><strong>Attempts:</strong> {connectionErrors.connectionAttempts}</p>

                                        <button
                                            onClick={handleReconnect}
                                            className="mt-1 bg-red-100 hover:bg-red-200 text-red-800 text-xs px-2 py-1 rounded"
                                        >
                                            Retry Connection
                                        </button>
                                    </div>
                                )}

                                {connectionStatus === 'connected' && (
                                    <p className="text-green-600">
                                        Real-time notifications are working properly.
                                    </p>
                                )}
                            </div>
                        )}
                    </div>

                    {/* Notification error message */}
                    {error && (
                        <div className="bg-red-100 text-red-700 p-2 rounded mb-2 text-xs">
                            {error}
                        </div>
                    )}

                    <div className="divide-y divide-gray-200">
                        {isLoading ? (
                            <div className="py-4 text-center text-gray-500">
                                <svg className="animate-spin h-5 w-5 mx-auto mb-2" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                                    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                                    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                                </svg>
                                Loading notifications...
                            </div>
                        ) : notifications.length === 0 ? (
                            <div className="py-6 text-center text-gray-500">
                                <svg className="h-10 w-10 mx-auto mb-2 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9"></path>
                                </svg>
                                No notifications yet
                            </div>
                        ) : (
                            notifications.map((notification) => (
                                <NotificationItem
                                    key={notification.id}
                                    data={notification}
                                    handleRead={handleRead}
                                />
                            ))
                        )}
                    </div>

                    {/* Connection status footer */}
                    <div className="mt-3 pt-2 border-t border-gray-100 text-xs text-gray-500 flex justify-between items-center">
                        <span>
                            {connectionStatus === 'connected'
                                ? 'Live updates enabled'
                                : 'Live updates unavailable'}
                        </span>

                        <button
                            onClick={fetchNotifications}
                            className="text-blue-600 hover:text-blue-800"
                        >
                            Refresh
                        </button>
                    </div>
                </div>
            )}
        </div>
    );
};

export default NotificationPanel;