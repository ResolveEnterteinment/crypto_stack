// src/context/NotificationContext.tsx
import React, { createContext, useContext, useEffect, useState, useCallback, ReactNode, useRef } from 'react';
import { HubConnection } from '@microsoft/signalr';
import { useAuth } from './AuthContext';
import INotification from '../components/Notification/INotification';
import { notificationService } from '../services/notification';
import {
    connectToNotificationHub,
    disconnectFromNotificationHub,
    getNotificationHubState,
    isNotificationHubConnected,
    type SignalRConnectionConfig,
    type ConnectionState as SignalRConnectionState
} from '../services/notification';

// Type definitions
type ConnectionStatus = 'connected' | 'connecting' | 'disconnected' | 'reconnecting';

interface NotificationContextType {
    notifications: INotification[];
    unreadCount: number;
    isLoading: boolean;
    error: string | null;
    connectionStatus: ConnectionStatus;
    lastConnectionError: string | null;
    connectionAttempts: number;
    refreshNotifications: () => Promise<void>;
    markAsRead: (notificationId: string) => Promise<void>;
    markAllNotificationsAsRead: () => Promise<void>;
    deleteNotification: (notificationId: string) => Promise<void>;
    deleteAllNotifications: () => Promise<void>;
    reconnect: () => Promise<void>;
    clearError: () => void;
}

// Default context values
const defaultContextValue: NotificationContextType = {
    notifications: [],
    unreadCount: 0,
    isLoading: false,
    error: null,
    connectionStatus: 'disconnected',
    lastConnectionError: null,
    connectionAttempts: 0,
    refreshNotifications: async () => { },
    markAsRead: async () => { },
    markAllNotificationsAsRead: async () => { },
    deleteNotification: async () => { },
    deleteAllNotifications: async () => { },
    reconnect: async () => { },
    clearError: () => { }
};

// Create context
const NotificationContext = createContext<NotificationContextType>(defaultContextValue);

// Provider component
export const NotificationProvider: React.FC<{ children: ReactNode }> = ({ children }) => {
    // State management
    const [notifications, setNotifications] = useState<INotification[]>([]);
    const [unreadCount, setUnreadCount] = useState(0);
    const [isLoading, setIsLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [connectionStatus, setConnectionStatus] = useState<ConnectionStatus>('disconnected');
    const [lastConnectionError, setLastConnectionError] = useState<string | null>(null);
    const [connectionAttempts, setConnectionAttempts] = useState(0);

    // References
    const hubConnectionRef = useRef<HubConnection | null>(null);
    const connectionCheckInterval = useRef<NodeJS.Timeout | null>(null);
    const refreshDebounceTimer = useRef<NodeJS.Timeout | null>(null);
    const notificationSoundRef = useRef<HTMLAudioElement | null>(null);
    const errorClearTimer = useRef<NodeJS.Timeout | null>(null);

    const { user } = useAuth();

    // Initialize notification sound
    useEffect(() => {
        try {
            notificationSoundRef.current = new Audio('/notification-sound.mp3');
            notificationSoundRef.current.load(); // Preload
        } catch (error) {
            console.log('Could not initialize notification sound');
        }
    }, []);

    // Clear error after timeout
    const clearError = useCallback(() => {
        setError(null);
        if (errorClearTimer.current) {
            clearTimeout(errorClearTimer.current);
            errorClearTimer.current = null;
        }
    }, []);

    // Auto-clear errors after 5 seconds
    useEffect(() => {
        if (error) {
            errorClearTimer.current = setTimeout(() => {
                clearError();
            }, 5000);
            return () => {
                if (errorClearTimer.current) {
                    clearTimeout(errorClearTimer.current);
                }
            };
        }
    }, [error, clearError]);

    // Update connection status based on SignalR state
    const updateConnectionStatus = useCallback(() => {
        const state : SignalRConnectionState | null = getNotificationHubState();

        if (!state) {
            setConnectionStatus('disconnected');
            setConnectionAttempts(0);
            return;
        }

        setConnectionStatus(state.status);
        setLastConnectionError(state.lastError);
        setConnectionAttempts(state.connectionAttempts);
    }, []);

    // Fetch notifications with improved error handling
    const refreshNotifications = useCallback(async () => {
        if (!user?.id) {
            console.log("No user ID available for fetching notifications");
            return;
        }

        // Clear any existing debounce timer
        if (refreshDebounceTimer.current) {
            clearTimeout(refreshDebounceTimer.current);
        }

        // Debounce rapid refresh calls
        refreshDebounceTimer.current = setTimeout(async () => {
            setIsLoading(true);
            setError(null);

            try {
                const fetchedNotifications = await notificationService.getNotifications(
                    user.id,
                    1,  // page
                    50  // pageSize - fetch more at once to reduce API calls
                );

                setNotifications(fetchedNotifications);

                // Calculate unread count
                const unread = fetchedNotifications.filter(n => !n.isRead).length;
                setUnreadCount(unread);

                console.log(`Fetched ${fetchedNotifications.length} notifications (${unread} unread)`);
            } catch (err: any) {
                console.error("Error fetching notifications:", err);

                // Don't show error for network issues if we have cached data
                if (notifications.length > 0 && err.message?.includes('network')) {
                    console.log("Using cached notifications due to network issue");
                } else {
                    setError(err.message || "Failed to fetch notifications");
                }
            } finally {
                setIsLoading(false);
            }
        }, 300); // 300ms debounce
    }, [user?.id, notifications.length]);

    // Mark notification as read with optimistic update
    const markAsRead = useCallback(async (notificationId: string) => {
        if (!notificationId) {
            console.error("Notification ID is required");
            return;
        }

        // Optimistic update
        setNotifications(prev =>
            prev.map(n =>
                n.id === notificationId ? { ...n, isRead: true } : n
            )
        );
        setUnreadCount(prev => Math.max(0, prev - 1));

        try {
            await notificationService.markNotificationAsRead(notificationId);
            console.log(`Marked notification ${notificationId} as read`);
        } catch (err: any) {
            console.error("Error marking notification as read:", err);

            // Revert optimistic update on error
            setNotifications(prev =>
                prev.map(n =>
                    n.id === notificationId ? { ...n, isRead: false } : n
                )
            );
            setUnreadCount(prev => prev + 1);

            setError(err.message || "Failed to mark notification as read");
        }
    }, []);

    // Mark all notifications as read with optimistic update
    const markAllNotificationsAsRead = useCallback(async () => {
        if (!user?.id || notifications.length === 0) {
            return;
        }

        // Store original state for rollback
        const originalNotifications = [...notifications];
        const originalUnreadCount = unreadCount;

        // Optimistic update
        setNotifications(prev =>
            prev.map(n => ({ ...n, isRead: true }))
        );
        setUnreadCount(0);

        try {
            await notificationService.markAllAsRead(user.id);
            console.log("Marked all notifications as read");

            // Refresh to sync with server state
            await refreshNotifications();
        } catch (err: any) {
            console.error("Error marking all notifications as read:", err);

            // Revert optimistic update on error
            setNotifications(originalNotifications);
            setUnreadCount(originalUnreadCount);

            setError(err.message || "Failed to mark all notifications as read");
        }
    }, [user?.id, notifications, unreadCount, refreshNotifications]);

    // Delete a notification
    const deleteNotification = useCallback(async (notificationId: string) => {
        if (!notificationId) {
            console.error("Notification ID is required");
            return;
        }

        // Optimistic update
        const notificationToDelete = notifications.find(n => n.id === notificationId);
        setNotifications(prev => prev.filter(n => n.id !== notificationId));
        if (notificationToDelete && !notificationToDelete.isRead) {
            setUnreadCount(prev => Math.max(0, prev - 1));
        }

        try {
            await notificationService.deleteNotification(notificationId);
            console.log(`Deleted notification ${notificationId}`);
        } catch (err: any) {
            console.error("Error deleting notification:", err);

            // Revert optimistic update on error
            if (notificationToDelete) {
                setNotifications(prev => [...prev, notificationToDelete].sort((a, b) =>
                    new Date(b.createdAt || 0).getTime() - new Date(a.createdAt || 0).getTime()
                ));
                if (!notificationToDelete.isRead) {
                    setUnreadCount(prev => prev + 1);
                }
            }

            setError(err.message || "Failed to delete notification");
        }
    }, [notifications]);

    // Delete all notifications
    const deleteAllNotifications = useCallback(async () => {
        if (!user?.id || notifications.length === 0) {
            return;
        }

        // Store original state for rollback
        const originalNotifications = [...notifications];
        const originalUnreadCount = unreadCount;

        // Optimistic update
        setNotifications([]);
        setUnreadCount(0);

        try {
            await notificationService.deleteAllNotifications(user.id);
            console.log("Deleted all notifications");
        } catch (err: any) {
            console.error("Error deleting all notifications:", err);

            // Revert optimistic update on error
            setNotifications(originalNotifications);
            setUnreadCount(originalUnreadCount);

            setError(err.message || "Failed to delete all notifications");
        }
    }, [user?.id, notifications, unreadCount]);

    // Play notification sound
    const playNotificationSound = useCallback(() => {
        if (notificationSoundRef.current) {
            notificationSoundRef.current.play().catch(e => {
                console.log('Could not play notification sound:', e);
            });
        }
    }, []);

    // Show browser notification
    const showBrowserNotification = useCallback((message: string) => {
        if (Notification.permission === "granted") {
            try {
                const notification = new Notification("New Notification", {
                    body: message,
                    icon: '/notification-icon.png',
                    badge: '/notification-badge.png',
                    tag: 'crypto-investment-notification',
                    requireInteraction: false
                });

                // Auto-close after 5 seconds
                setTimeout(() => notification.close(), 5000);

                // Handle click to focus window
                notification.onclick = () => {
                    window.focus();
                    notification.close();
                };
            } catch (err) {
                console.log('Could not show browser notification:', err);
            }
        }
    }, []);

    // Handle new notification received from SignalR
    const handleNewNotification = useCallback((message: string) => {
        console.log(`🔔 New notification received: ${message}`);

        // Play sound
        playNotificationSound();

        // Show browser notification
        showBrowserNotification(message);

        // Refresh notifications list
        refreshNotifications();
    }, [refreshNotifications, playNotificationSound, showBrowserNotification]);

    // Force reconnection with error handling
    const reconnect = useCallback(async () => {
        if (!user?.id) {
            setError("User not authenticated");
            return;
        }

        setConnectionStatus('connecting');
        setLastConnectionError(null);

        try {
            // Disconnect existing connection
            await disconnectFromNotificationHub();

            // Configuration for the connection
            const config: SignalRConnectionConfig = {
                reconnectAttempts: 5,
                reconnectDelays: [0, 2000, 5000, 10000, 30000],
                enableLogging: import.meta.env.DEV
            };

            // Connect to hub
            const connection = await connectToNotificationHub(
                user.id,
                handleNewNotification,
                config
            );

            hubConnectionRef.current = connection;
            updateConnectionStatus();
            console.log("Manual reconnection successful");
        } catch (err: any) {
            console.error("Manual reconnection failed:", err);
            setConnectionStatus('disconnected');
            setLastConnectionError(err.message || "Reconnection failed. Please try again later.");
            setError("Could not reconnect to notification service. Please refresh the page.");
        }
    }, [user?.id, handleNewNotification, updateConnectionStatus]);

    // Initialize SignalR connection
    useEffect(() => {
        if (!user?.id) {
            // Clean up if user logs out
            disconnectFromNotificationHub().catch(err =>
                console.warn("Error disconnecting from notification hub:", err)
            );
            setNotifications([]);
            setUnreadCount(0);
            setConnectionStatus('disconnected');
            return;
        }

        // Configuration for the connection
        const config: SignalRConnectionConfig = {
            reconnectAttempts: 5,
            reconnectDelays: [0, 2000, 5000, 10000, 30000],
            enableLogging: import.meta.env.DEV
        };

        // Connect to SignalR hub
        let connectionPromise: Promise<HubConnection>;

        const initConnection = async () => {
            setConnectionStatus('connecting');

            try {
                const connection = await connectToNotificationHub(
                    user.id,
                    handleNewNotification,
                    config
                );
                hubConnectionRef.current = connection;
                updateConnectionStatus();
            } catch (err) {
                console.error("Failed to connect to notification hub:", err);
                setConnectionStatus('disconnected');
                setLastConnectionError("Failed to connect to notification service");
            }
        };

        initConnection();

        // Initial fetch of notifications
        refreshNotifications();

        // Request notification permission
        if ('Notification' in window &&
            Notification.permission !== 'granted' &&
            Notification.permission !== 'denied') {
            Notification.requestPermission().then(permission => {
                console.log('Notification permission:', permission);
            });
        }

        // Setup connection monitoring
        if (connectionCheckInterval.current) {
            clearInterval(connectionCheckInterval.current);
        }

        connectionCheckInterval.current = setInterval(() => {
            updateConnectionStatus();

            // Auto-reconnect if disconnected and not already trying
            const state = getNotificationHubState();
            if (state?.status === 'disconnected' && state.connectionAttempts < 5) {
                console.log('Auto-reconnecting to notification hub...');
                reconnect();
            }
        }, 5000); // Check every 5 seconds

        // Cleanup function
        return () => {
            if (connectionCheckInterval.current) {
                clearInterval(connectionCheckInterval.current);
                connectionCheckInterval.current = null;
            }

            if (refreshDebounceTimer.current) {
                clearTimeout(refreshDebounceTimer.current);
                refreshDebounceTimer.current = null;
            }

            if (errorClearTimer.current) {
                clearTimeout(errorClearTimer.current);
                errorClearTimer.current = null;
            }

            disconnectFromNotificationHub().catch(err =>
                console.warn("Error disconnecting from notification hub during cleanup:", err)
            );
        };
    }, [user?.id, handleNewNotification, refreshNotifications, updateConnectionStatus, reconnect]);

    // Context value
    const value: NotificationContextType = {
        notifications,
        unreadCount,
        isLoading,
        error,
        connectionStatus,
        lastConnectionError,
        connectionAttempts,
        refreshNotifications,
        markAsRead,
        markAllNotificationsAsRead,
        deleteNotification,
        deleteAllNotifications,
        reconnect,
        clearError
    };

    return (
        <NotificationContext.Provider value={value}>
            {children}
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

// Export types for external use
export type {
    NotificationContextType,
    ConnectionStatus
};