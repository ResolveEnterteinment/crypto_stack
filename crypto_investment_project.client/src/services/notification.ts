// src/services/notification.ts
import { HubConnection } from "@microsoft/signalr";
import INotification from "../components/Notification/INotification";
import apiClient, { ApiErrorHandler } from "./api";
import { signalRManager, type ConnectionState, type SignalRConnectionConfig } from "./signalRService";

// Type definitions
export interface NotificationPreferences {
    emailEnabled: boolean;
    pushEnabled: boolean;
    smsEnabled: boolean;
    soundEnabled: boolean;
    categories: {
        [key: string]: boolean;
    };
}

export interface NotificationStats {
    totalCount: number;
    unreadCount: number;
    lastReadAt?: Date;
}

// API endpoints
const NOTIFICATION_ENDPOINTS = {
    GET_ONE: (notificationId: string) => `/notification/get/${notificationId}`,
    GET_ALL: () => `/notification/get/all`,
    MARK_READ: (notificationId: string) => `/notification/read/${notificationId}`,
    MARK_ALL_READ: () => `/notification/read/all`,
    DELETE: (notificationId: string) => `/notification/delete/${notificationId}`,
    DELETE_ALL: () => `/notification/delete/all`,
    SUBSCRIBE: () => `/notification/subscribe`,
    UNSUBSCRIBE: () => `/notification/unsubscribe`,
    PREFERENCES: () => `/notification/preferences`,
    HEALTH_CHECK: '/health'
} as const;
class NotificationService {
    /**
     * Fetches notifications with paging support
     * Uses deduplication to prevent multiple simultaneous fetches
     */
    async getNotifications(
        userId: string,
        page: number = 1,
        pageSize: number = 20
    ): Promise<INotification[]> {
        if (!userId) {
            console.error("getNotifications called with undefined userId");
            throw new Error("User ID is required");
        }

        try {
            // Use deduplication to prevent multiple simultaneous fetches
            const response = await apiClient.get<INotification[]>(
                NOTIFICATION_ENDPOINTS.GET_ALL(),
                {
                    dedupe: true,
                    dedupeKey: `notifications-${userId}-${page}-${pageSize}`,
                    priority: 'normal',
                    retryCount: 2 // Limited retries for notifications
                }
            );

            // Validate response
            if (!response.success || !Array.isArray(response.data)) {
                console.warn("Notification API returned unexpected format:", response);
                return [];
            }

            // Sort by date (newest first)
            const sortedNotifications = response.data.sort((a, b) => {
                const dateA = new Date(a.createdAt || 0).getTime();
                const dateB = new Date(b.createdAt || 0).getTime();
                return dateB - dateA;
            });

            return sortedNotifications;
        } catch (error) {
            console.error("Error fetching notifications:", error);

            const apiError = ApiErrorHandler.extractError(error);
            const userMessage = ApiErrorHandler.formatUserMessage(apiError);

            throw new Error(userMessage);
        }
    }

    /**
     * Gets paginated notifications with total count
     */
    async getPaginatedNotifications(
        userId: string,
        page: number = 1,
        pageSize: number = 20
    ): Promise<{ notifications: INotification[]; totalCount: number; totalPages: number }> {
        if (!userId) {
            throw new Error("User ID is required");
        }

        try {
            const response = await apiClient.getPaginated<INotification[]>(
                NOTIFICATION_ENDPOINTS.GET_ALL(),
                page,
                pageSize,
                {
                    dedupe: true,
                    dedupeKey: `notifications-paginated-${userId}-${page}-${pageSize}`,
                    priority: 'normal'
                }
            );

            return {
                notifications: response.data || [],
                totalCount: response.totalCount || 0,
                totalPages: response.totalPages
            };
        } catch (error) {
            const apiError = ApiErrorHandler.extractError(error);
            throw new Error(ApiErrorHandler.formatUserMessage(apiError));
        }
    }

    /**
     * Marks a notification as read
     * Uses idempotency to prevent duplicate marking
     */
    async markNotificationAsRead(
        notificationId: string
    ): Promise<void> {
        if (!notificationId) {
            console.error("markNotificationAsRead called with undefined notificationId");
            throw new Error("Notification ID is required");
        }

        try {
            const response = await apiClient.put(
                NOTIFICATION_ENDPOINTS.MARK_READ(notificationId),
                { isRead: true },
                {
                    idempotencyKey: `mark-read-${notificationId}`,
                    priority: 'low', // Low priority for marking as read
                    retryCount: 3,
                    debounceMs: 500 // Debounce rapid marking
                }
            );

            if (!response.success) {
                // If the notification is already marked as read, don't throw
                if (response.message?.includes('already')) {
                    console.log(`Notification ${notificationId} was already marked as read`);
                    return;
                }

                throw new Error(response.message || 'Failed to mark notification as read');
            }

            console.log(`Successfully marked notification ${notificationId} as read`);
        } catch (error) {
            const apiError = ApiErrorHandler.extractError(error);

            // Don't throw error for 404 - notification might have been deleted
            if (apiError.statusCode === 404) {
                console.log(`Notification ${notificationId} not found - may have been deleted`);
                return;
            }

            const userMessage = ApiErrorHandler.formatUserMessage(apiError);
            throw new Error(userMessage);
        }
    }

    /**
     * Marks all notifications as read for a user
     * Uses throttling to prevent spam
     */
    async markAllAsRead(userId: string): Promise<void> {
        if (!userId) {
            console.error("markAllAsRead called with undefined userId");
            throw new Error("User ID is required");
        }

        try {
            const response = await apiClient.put(
                NOTIFICATION_ENDPOINTS.MARK_ALL_READ(),
                { isRead: true },
                {
                    idempotencyKey: `mark-all-read-${userId}-${Date.now()}`,
                    priority: 'normal',
                    throttleMs: 2000, // Throttle to prevent spam
                    retryCount: 2
                }
            );

            if (!response.success) {
                // If there are no unread notifications, don't throw
                if (response.message?.includes('no unread')) {
                    console.log('No unread notifications to mark');
                    return;
                }

                throw new Error(response.message || 'Failed to mark all notifications as read');
            }

            console.log('Successfully marked all notifications as read');
        } catch (error) {
            const apiError = ApiErrorHandler.extractError(error);

            // Handle specific error cases
            if (apiError.statusCode === 400) {
                console.log('Notifications already processed');
                return;
            }

            const userMessage = ApiErrorHandler.formatUserMessage(apiError);
            throw new Error(userMessage);
        }
    }

    /**
     * Deletes a specific notification
     */
    async deleteNotification(notificationId: string): Promise<void> {
        if (!notificationId) {
            throw new Error("Notification ID is required");
        }

        try {
            const response = await apiClient.delete(
                NOTIFICATION_ENDPOINTS.DELETE(notificationId),
                {
                    idempotencyKey: `delete-${notificationId}`,
                    priority: 'low',
                    retryCount: 1
                }
            );

            if (!response.success) {
                throw new Error(response.message || 'Failed to delete notification');
            }

            console.log(`Successfully deleted notification ${notificationId}`);
        } catch (error) {
            const apiError = ApiErrorHandler.extractError(error);

            // Don't throw error for 404 - notification might already be deleted
            if (apiError.statusCode === 404) {
                console.log(`Notification ${notificationId} already deleted`);
                return;
            }

            const userMessage = ApiErrorHandler.formatUserMessage(apiError);
            throw new Error(userMessage);
        }
    }

    /**
     * Deletes all notifications for a user
     */
    async deleteAllNotifications(userId: string): Promise<void> {
        if (!userId) {
            throw new Error("User ID is required");
        }

        try {
            const response = await apiClient.delete(
                NOTIFICATION_ENDPOINTS.DELETE_ALL(),
                {
                    priority: 'normal',
                    throttleMs: 5000, // Throttle to prevent accidental spam
                    retryCount: 0 // No retry for delete operations
                }
            );

            if (!response.success) {
                throw new Error(response.message || 'Failed to delete all notifications');
            }

            console.log('Successfully deleted all notifications');
        } catch (error) {
            const apiError = ApiErrorHandler.extractError(error);
            const userMessage = ApiErrorHandler.formatUserMessage(apiError);
            throw new Error(userMessage);
        }
    }

    /**
     * Gets notification statistics for a user
     */
    async getNotificationStats(userId: string): Promise<NotificationStats> {
        if (!userId) {
            throw new Error("User ID is required");
        }

        try {
            // Fetch all notifications to calculate stats
            // In production, this should be a dedicated endpoint
            const notifications = await this.getNotifications(userId, 1, 100);

            const unreadCount = notifications.filter(n => !n.isRead).length;
            const lastRead = notifications
                .filter(n => n.isRead)
                .sort((a, b) => new Date(b.readAt || 0).getTime() - new Date(a.readAt || 0).getTime())[0];

            return {
                totalCount: notifications.length,
                unreadCount,
                lastReadAt: lastRead?.readAt ? new Date(lastRead.readAt) : undefined
            };
        } catch (error) {
            const apiError = ApiErrorHandler.extractError(error);
            throw new Error(ApiErrorHandler.formatUserMessage(apiError));
        }
    }

    /**
     * Gets notification preferences for a user
     */
    async getNotificationPreferences(userId: string): Promise<NotificationPreferences> {
        if (!userId) {
            throw new Error("User ID is required");
        }

        try {
            const response = await apiClient.get<NotificationPreferences>(
                NOTIFICATION_ENDPOINTS.PREFERENCES(),
                {
                    dedupe: true,
                    dedupeKey: `notification-preferences-${userId}`,
                    priority: 'low'
                }
            );

            if (!response.success) {
                throw new Error(response.message || 'Failed to get notification preferences');
            }

            return response.data;
        } catch (error) {
            const apiError = ApiErrorHandler.extractError(error);
            throw new Error(ApiErrorHandler.formatUserMessage(apiError));
        }
    }

    /**
     * Updates notification preferences for a user
     */
    async updateNotificationPreferences(
        userId: string,
        preferences: Partial<NotificationPreferences>
    ): Promise<void> {
        if (!userId) {
            throw new Error("User ID is required");
        }

        try {
            const response = await apiClient.put(
                NOTIFICATION_ENDPOINTS.PREFERENCES(),
                preferences,
                {
                    priority: 'normal',
                    throttleMs: 1000, // Prevent rapid updates
                    retryCount: 2
                }
            );

            if (!response.success) {
                throw new Error(response.message || 'Failed to update notification preferences');
            }

            console.log('Successfully updated notification preferences');
        } catch (error) {
            const apiError = ApiErrorHandler.extractError(error);
            throw new Error(ApiErrorHandler.formatUserMessage(apiError));
        }
    }

    /**
     * Subscribe to push notifications
     */
    async subscribeToPushNotifications(userId: string, subscription: PushSubscription): Promise<void> {
        if (!userId) {
            throw new Error("User ID is required");
        }

        try {
            const response = await apiClient.post(
                NOTIFICATION_ENDPOINTS.SUBSCRIBE(),
                subscription,
                {
                    priority: 'high',
                    retryCount: 2
                }
            );

            if (!response.success) {
                throw new Error(response.message || 'Failed to subscribe to push notifications');
            }

            console.log('Successfully subscribed to push notifications');
        } catch (error) {
            const apiError = ApiErrorHandler.extractError(error);
            throw new Error(ApiErrorHandler.formatUserMessage(apiError));
        }
    }

    /**
     * Unsubscribe from push notifications
     */
    async unsubscribeFromPushNotifications(userId: string): Promise<void> {
        if (!userId) {
            throw new Error("User ID is required");
        }

        try {
            const response = await apiClient.post(
                NOTIFICATION_ENDPOINTS.UNSUBSCRIBE(),
                undefined,
                {
                    priority: 'normal',
                    retryCount: 1
                }
            );

            if (!response.success) {
                throw new Error(response.message || 'Failed to unsubscribe from push notifications');
            }

            console.log('Successfully unsubscribed from push notifications');
        } catch (error) {
            const apiError = ApiErrorHandler.extractError(error);
            throw new Error(ApiErrorHandler.formatUserMessage(apiError));
        }
    }

    /**
     * Helper function to check server availability
     */
    async checkServerAvailability(): Promise<boolean> {
        try {
            const response = await apiClient.get(
                NOTIFICATION_ENDPOINTS.HEALTH_CHECK,
                {
                    skipAuth: true,
                    skipCsrf: true,
                    retryCount: 0,
                    dedupe: false
                }
            );

            return response.success;
        } catch (error) {
            console.warn("Server health check failed:", error);
            return false;
        }
    }
}

// Create singleton instance
export const notificationService = new NotificationService();

// ==================== SignalR Notification Hub Functions ====================

const NOTIFICATION_HUB = 'notification';

/**
 * Connects to the notification hub
 */
export async function connectToNotificationHub(
    userId: string,
    onNotificationReceived: (message: string) => void,
    config: SignalRConnectionConfig = {}
): Promise<HubConnection> {
    if (!userId) {
        throw new Error("User ID is required for notification connection");
    }

    // Get hub URL from environment
    const apiBaseUrl = import.meta.env.VITE_SIGNALR_BASE_URL ||
        import.meta.env.VITE_API_BASE_URL?.replace('/api', '') ||
        "https://localhost:7144";
    const hubUrl = `${apiBaseUrl}/hubs/notificationHub`;

    console.log(`Connecting to notification hub at: ${hubUrl}`);

    // Connect to hub using generic SignalR manager
    const connection = await signalRManager.connect(NOTIFICATION_HUB, hubUrl, config);

    // Register notification handler
    signalRManager.on(NOTIFICATION_HUB, "ReceiveNotification", (targetUserId: string, message: string) => {
        // Only process notifications for this user
        if (targetUserId === userId) {
            console.log(`🔔 New notification received: ${message}`);
            onNotificationReceived(message);
        }
    });

    // Register connection success handler
    signalRManager.on(NOTIFICATION_HUB, "Connected", (connectionId: string) => {
        console.log(`✅ Connected to Notification Hub with ID: ${connectionId}`);
    });

    // Join user group for personalized notifications
    try {
        await signalRManager.invoke(NOTIFICATION_HUB, "JoinUserGroup", userId);
        console.log(`Joined notification group for user ${userId}`);
    } catch (error) {
        console.error("Error joining user notification group:", error);
        // Don't fail the connection just because group joining failed
        // The server might still send broadcasts
    }

    return connection;
}

/**
 * Disconnects from the notification hub
 */
export async function disconnectFromNotificationHub(): Promise<void> {
    await signalRManager.disconnect(NOTIFICATION_HUB);
    console.log("Disconnected from notification hub");
}

/**
 * Gets the notification hub connection state
 */
export function getNotificationHubState(): ConnectionState | null {
    return signalRManager.getConnectionState(NOTIFICATION_HUB);
}

/**
 * Checks if notification hub is connected
 */
export function isNotificationHubConnected(): boolean {
    return signalRManager.isConnected(NOTIFICATION_HUB);
}

/**
 * Sends a notification to a specific user (admin only)
 */
export async function sendNotificationToUser(
    targetUserId: string,
    message: string,
    type?: string
): Promise<void> {
    if (!isNotificationHubConnected()) {
        throw new Error("Not connected to notification hub");
    }

    try {
        await signalRManager.invoke(
            NOTIFICATION_HUB,
            "SendNotificationToUser",
            targetUserId,
            message,
            type
        );
        console.log(`Sent notification to user ${targetUserId}`);
    } catch (error) {
        console.error("Error sending notification:", error);
        throw error;
    }
}

/**
 * Broadcasts a notification to all connected users (admin only)
 */
export async function broadcastNotification(
    message: string,
    type?: string
): Promise<void> {
    if (!isNotificationHubConnected()) {
        throw new Error("Not connected to notification hub");
    }

    try {
        await signalRManager.invoke(
            NOTIFICATION_HUB,
            "BroadcastNotification",
            message,
            type
        );
        console.log("Broadcasted notification to all users");
    } catch (error) {
        console.error("Error broadcasting notification:", error);
        throw error;
    }
}

// Export types
export type {
    ConnectionState, INotification,
    SignalRConnectionConfig
};
