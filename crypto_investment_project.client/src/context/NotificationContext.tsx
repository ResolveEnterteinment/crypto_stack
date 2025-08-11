// src/context/NotificationContext.tsx
import React, { createContext, useContext, useEffect, useState, useCallback, ReactNode } from 'react';
import { HubConnection, HubConnectionState } from '@microsoft/signalr';
import { useAuth } from './AuthContext';
import INotification from '../components/Notification/INotification';
import {
    getNotifications,
    markNotificationAsRead,
    markAllAsRead,
    connectToNotifications,
    forceReconnect,
    connectionErrors
} from '../services/notification';

// Type definitions
type ConnectionStatus = 'connected' | 'connecting' | 'disconnected';

interface NotificationContextType {
    notifications: INotification[];
    unreadCount: number;
    isLoading: boolean;
    error: string | null;
    connectionStatus: ConnectionStatus;
    lastConnectionError: string | null;
    refreshNotifications: () => Promise<void>;
    markAsRead: (notificationId: string) => Promise<void>;
    markAllNotificationsAsRead: () => Promise<void>;
    reconnect: () => Promise<void>;
}

// Create context with default values
const NotificationContext = createContext<NotificationContextType>({
    notifications: [],
    unreadCount: 0,
    isLoading: false,
    error: null,
    connectionStatus: 'disconnected',
    lastConnectionError: null,
    refreshNotifications: async () => { },
    markAsRead: async () => { },
    markAllNotificationsAsRead: async () => { },
    reconnect: async () => { }
});

// Provider component
export const NotificationProvider: React.FC<{ children: ReactNode }> = ({ children }) => {
    // State management
    const [notifications, setNotifications] = useState<INotification[]>([]);
    const [unreadCount, setUnreadCount] = useState(0);
    const [isLoading, setIsLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [connectionStatus, setConnectionStatus] = useState<ConnectionStatus>('disconnected');
    const [lastConnectionError, setLastConnectionError] = useState<string | null>(null);

    // References
    const hubConnectionRef = React.useRef<HubConnection | null>(null);
    const connectionCheckInterval = React.useRef<NodeJS.Timeout | null>(null);

    const { user } = useAuth();

    // Update connection status based on current state
    const updateConnectionStatus = useCallback(() => {
        if (!hubConnectionRef.current) {
            setConnectionStatus('disconnected');
            return;
        }

        const currentState = hubConnectionRef.current.state;

        if (currentState === HubConnectionState.Connected) {
            setConnectionStatus('connected');
            setLastConnectionError(null);
        } else if (
            currentState === HubConnectionState.Connecting ||
            currentState === HubConnectionState.Reconnecting
        ) {
            setConnectionStatus('connecting');
        } else {
            setConnectionStatus('disconnected');
            setLastConnectionError(connectionErrors.lastError);
        }
    }, []);

    // Fetch notifications
    const refreshNotifications = useCallback(async () => {
        if (!user?.id) return;

        setIsLoading(true);
        setError(null);

        try {
            const data = await getNotifications(user.id);
            setNotifications(data);

            // Update unread count
            const unreadNotificationCount = data.filter(n => !n.isRead).length;
            setUnreadCount(unreadNotificationCount);
        } catch (err) {
            console.error("Failed to fetch notifications:", err);
            setError("Unable to load notifications");
        } finally {
            setIsLoading(false);
        }
    }, [user?.id]);

    // Mark notification as read
    const markAsRead = useCallback(async (notificationId: string) => {
        try {
            await markNotificationAsRead(notificationId);

            // Update local state
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
            // We handle this silently to avoid disrupting the user experience
        }
    }, []);

    // Mark all notifications as read
    const markAllNotificationsAsRead = useCallback(async () => {
        const unreadNotifications = notifications.filter(n => !n.isRead);

        if (unreadNotifications.length === 0) return;

        try {
            const result = await markAllAsRead();

            // Update local state - mark all as read
            setNotifications(prev =>
                prev.map(notification => ({ ...notification, isRead: true }))
            );
            setUnreadCount(0);
        } catch (err) {
            console.error("Failed to mark all notifications as read:", err);
            setError("Failed to mark all as read. Please try again.");
            setTimeout(() => setError(null), 3000);
        }
    }, [notifications]);

    // Handle new notification received from SignalR
    const handleNewNotification = useCallback((message: string) => {
        // Play notification sound if browser allows
        try {
            const audio = new Audio('/notification-sound.mp3');
            audio.play().catch(e => console.log('Could not play notification sound', e));
        } catch (error) {
            // Ignore audio errors
        }

        // Refetch notifications to get the new one
        refreshNotifications();

        // Show browser notification if permission granted
        if (Notification.permission === "granted") {
            new Notification("New Notification", {
                body: message,
                icon: '/notification-icon.png'
            });
        }
    }, [refreshNotifications]);

    // Force reconnection
    const reconnect = useCallback(async () => {
        setConnectionStatus('connecting');
        setLastConnectionError(null);

        try {
            if (user?.id) {
                const connection = await forceReconnect(user.id, handleNewNotification);
                hubConnectionRef.current = connection;
                updateConnectionStatus();
            }
        } catch (err) {
            console.error("Manual reconnection failed:", err);
            setConnectionStatus('disconnected');
            setLastConnectionError("Reconnection failed. Please try again later.");
        }
    }, [user?.id, handleNewNotification, updateConnectionStatus]);

    // Initialize SignalR connection
    useEffect(() => {
        if (!user?.id) return;

        // Clean up previous connection
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
        refreshNotifications();

        // Request notification permission
        if (Notification.permission !== 'granted' && Notification.permission !== 'denied') {
            Notification.requestPermission();
        }

        // Setup connection monitoring
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
    }, [user?.id, handleNewNotification, refreshNotifications, updateConnectionStatus]);

    // Context value
    const value = {
        notifications,
        unreadCount,
        isLoading,
        error,
        connectionStatus,
        lastConnectionError,
        refreshNotifications,
        markAsRead,
        markAllNotificationsAsRead,
        reconnect
    };

    return (
        <NotificationContext.Provider value= { value } >
        { children }
        </NotificationContext.Provider>
  );
};

// Custom hook to use the notification context
export const useNotifications = () => {
    const context = useContext(NotificationContext);

    if (context === undefined) {
        throw new Error('useNotifications must be used within a NotificationProvider');
    }

    return context;
};