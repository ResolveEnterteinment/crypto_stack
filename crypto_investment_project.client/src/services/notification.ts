import * as signalR from "@microsoft/signalr";
import api from "./api";
import INotification from "../components/Notification/INotification";

// Track the active connection
let activeConnection: signalR.HubConnection | null = null;

// Cache the connection errors for UI display
export const connectionErrors = {
    lastError: null as string | null,
    lastErrorTime: null as Date | null,
    connectionAttempts: 0
};

// Fetch notifications with paging support
export const getNotifications = async (userId: string, page: number = 1, pageSize: number = 20): Promise<INotification[]> => {
    if (!userId) {
        console.error("getNotifications called with undefined userId");
        return Promise.reject(new Error("User ID is required"));
    }

    try {
        const { data } = await api.get(`/Notification/${userId}`, {
            params: { page, pageSize }
        });

        // Ensure we have a valid array
        if (!Array.isArray(data)) {
            console.warn("Notification API did not return an array:", data);
            return [];
        }

        return data;
    } catch (error) {
        console.error("Error fetching notifications:", error);
        throw error;
    }
};

// Mark notification as read with retry mechanism and better error handling
export const markNotificationAsRead = async (notificationId: string, maxRetries = 3): Promise<void> => {
    if (!notificationId) {
        console.error("markNotificationAsRead called with undefined notificationId");
        return Promise.reject(new Error("Notification ID is required"));
    }

    let retries = 0;

    const attemptRequest = async (): Promise<void> => {
        try {
            // Add a request ID to help with debugging
            const requestId = `${Date.now()}-${Math.random().toString(36).substring(2, 10)}`;

            // Skip antiforgery token check for notification operations
            await api.post(`/Notification/read/${notificationId}`, null, {
                headers: {
                    'X-Request-ID': requestId,
                    'X-Skip-Csrf-Check': 'true'
                }
            });
        } catch (error: any) {
            console.warn(`Error marking notification ${notificationId} as read:`,
                error.response?.status,
                error.response?.data || error.message);

            // Retry on network errors or 5xx server errors
            if (
                (error.response && error.response.status >= 500) ||
                error.code === 'ECONNABORTED' ||
                !error.response
            ) {
                if (retries < maxRetries) {
                    retries++;
                    const backoffTime = Math.pow(2, retries) * 300; // Exponential backoff
                    console.log(`Retry ${retries}/${maxRetries} for marking notification as read after ${backoffTime}ms`);

                    return new Promise(resolve => {
                        setTimeout(() => resolve(attemptRequest()), backoffTime);
                    });
                }
            }

            // For 400 errors - the notification might not exist or already be marked as read
            if (error.response?.status === 400) {
                console.log(`Notification ${notificationId} returned 400, treating as already processed`);
                return; // Return without throwing to avoid error propagation
            }

            throw error;
        }
    };

    return attemptRequest();
};

// Handle batch operations more gracefully
export const markAllAsRead = async (notificationIds: string[]): Promise<{ success: boolean, failedIds: string[] }> => {
    if (!notificationIds.length) {
        return { success: true, failedIds: [] };
    }

    const results = await Promise.allSettled(notificationIds.map(id => markNotificationAsRead(id)));

    const failedIds = notificationIds.filter((_, index) => results[index].status === 'rejected');

    return {
        success: failedIds.length === 0,
        failedIds
    };
};

// SignalR Connection for real-time notifications with better error handling
export const connectToNotifications = (userId: string, onNotificationReceived: (message: string) => void): signalR.HubConnection => {
    if (!userId) {
        throw new Error("User ID is required for notification connection");
    }

    // If there's an existing connection for this user and it's not in a failed state, return it
    if (activeConnection && activeConnection.state !== signalR.HubConnectionState.Disconnected) {
        console.log("Reusing existing SignalR connection");
        return activeConnection;
    }

    // Get the authentication token
    const token = localStorage.getItem("access_token");
    if (!token) {
        console.warn("No authentication token found, notifications may not work correctly");
    }

    // Get API base URL from environment variable or api config
    const apiBaseUrl = api.defaults.baseURL || "https://localhost:7144";
    const hubUrl = `${apiBaseUrl}/hubs/notificationHub`.replace('/api', '');

    console.log("Connecting to SignalR hub at:", hubUrl);

    // Create connection with better settings for reliability
    const connection = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl, {
            accessTokenFactory: () => token || "",
            transport: signalR.HttpTransportType.WebSockets,
            skipNegotiation: true
        })
        .withAutomaticReconnect([0, 2000, 5000, 10000, 15000, 30000])
        .configureLogging(signalR.LogLevel.Warning)
        .build();

    // Set up event handlers
    connection.on("ReceiveNotification", (targetUserId, message) => {
        // Only process notifications meant for this user
        if (targetUserId === userId) {
            console.log(`🔔 New notification received: ${message}`);
            onNotificationReceived(message);
        }
    });

    connection.on("Connected", (connectionId) => {
        console.log(`✅ Connected to Notification Hub with ID: ${connectionId}`);
        connectionErrors.lastError = null;
        connectionErrors.connectionAttempts = 0;
    });

    // Connection state change handlers
    connection.onreconnecting((error) => {
        console.warn("SignalR reconnecting:", error?.message || "Unknown reason");
        connectionErrors.lastError = error?.message || "Connection lost, attempting to reconnect";
        connectionErrors.lastErrorTime = new Date();
    });

    connection.onreconnected((connectionId) => {
        console.log("SignalR reconnected with ID:", connectionId);
        connectionErrors.lastError = null;

        // Re-join user group after reconnection
        connection.invoke("JoinUserGroup", userId)
            .catch(err => console.error("Error rejoining user group after reconnection:", err));
    });

    connection.onclose((error) => {
        console.log("SignalR connection closed:", error?.message || "Unknown reason");
        connectionErrors.lastError = error?.message || "Connection closed";
        connectionErrors.lastErrorTime = new Date();
        activeConnection = null;
    });

    // Start the connection with robust retry logic
    const startConnection = async (retryAttempt = 0): Promise<signalR.HubConnection> => {
        connectionErrors.connectionAttempts = retryAttempt + 1;
        try {
            await connection.start();
            console.log("SignalR connection established successfully");
            activeConnection = connection;
            connectionErrors.lastError = null;

            // Join user group for personalized notifications
            try {
                await connection.invoke("JoinUserGroup", userId);
                console.log(`Joined notification group for user ${userId}`);
            } catch (groupError) {
                console.error("Error joining user notification group:", groupError);
                // Don't fail the connection just because group joining failed
            }

            return connection;
        } catch (err: any) {
            const maxRetries = 5;
            const retryDelayMs = Math.min(1000 * Math.pow(2, retryAttempt), 30000);
            connectionErrors.lastError = err?.message || "Failed to connect";
            connectionErrors.lastErrorTime = new Date();

            if (retryAttempt < maxRetries) {
                console.warn(`Connection failed, retrying in ${retryDelayMs / 1000}s... (Attempt ${retryAttempt + 1}/${maxRetries})`);

                return new Promise(resolve => {
                    setTimeout(() => resolve(startConnection(retryAttempt + 1)), retryDelayMs);
                });
            } else {
                console.error("SignalR connection failed after multiple attempts:", err);
                throw err;
            }
        }
    };

    // Start connection asynchronously 
    startConnection().catch(err => {
        console.error("Failed to establish notification connection:", err);
    });

    return connection;
};

// Helper function to check server availability
export const checkServerAvailability = async (): Promise<boolean> => {
    try {
        // Try a simple health check endpoint, or fallback to a HEAD request
        const response = await api.head("/health", {
            timeout: 3000
        });
        return response.status >= 200 && response.status < 300;
    } catch (error) {
        console.warn("Server health check failed:", error);
        return false;
    }
};

// Force reconnection when needed
export const forceReconnect = async (userId: string, onNotificationReceived: (message: string) => void): Promise<signalR.HubConnection | null> => {
    if (activeConnection) {
        try {
            await activeConnection.stop();
            console.log("Stopped existing connection for reconnect");
        } catch (err) {
            console.warn("Error stopping connection:", err);
        }
        activeConnection = null;
    }

    // Check if server is available before attempting reconnect
    const isServerAvailable = await checkServerAvailability();
    if (!isServerAvailable) {
        console.warn("Server is not available, delaying reconnection attempt");
        return null;
    }

    console.log("Forcing new connection attempt");
    connectionErrors.connectionAttempts = 0;
    connectionErrors.lastError = null;

    return connectToNotifications(userId, onNotificationReceived);
};