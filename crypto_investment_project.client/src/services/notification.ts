import * as signalR from "@microsoft/signalr";
import api from "./api";
import INotification from "../components/Notification/INotification";

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
        return data;
    } catch (error) {
        console.error("Error fetching notifications:", error);
        throw error;
    }
};

// Mark notification as read with retry mechanism
export const markNotificationAsRead = async (notificationId: string, maxRetries = 3): Promise<void> => {
    if (!notificationId) {
        console.error("markNotificationAsRead called with undefined notificationId");
        return Promise.reject(new Error("Notification ID is required"));
    }

    let retries = 0;

    const attemptRequest = async (): Promise<void> => {
        try {
            await api.post(`/Notification/read/${notificationId}`);
        } catch (error: any) {
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

            throw error;
        }
    };

    return attemptRequest();
};

// SignalR Connection for real-time notifications
export const connectToNotifications = (userId: string, onNotificationReceived: (message: string) => void): signalR.HubConnection => {
    if (!userId) {
        throw new Error("User ID is required for notification connection");
    }

    // Get the authentication token
    const token = localStorage.getItem("access_token");
    if (!token) {
        console.warn("No authentication token found, notifications may not work correctly");
    }

    // Create connection with retry and reconnect options
    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/api/hubs/notificationHub", {
            accessTokenFactory: () => token || "",
            transport: signalR.HttpTransportType.WebSockets,
            skipNegotiation: true
        })
        .withAutomaticReconnect([0, 2000, 5000, 10000]) // Automatic reconnect with increasing delays
        .configureLogging(signalR.LogLevel.Information)
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
    });

    connection.on("UserFailedAuthentication", () => {
        console.warn("⚠️ Authentication failed for notification connection");

        // Check if token is expired and trigger app-wide auth renewal if needed
        try {
            if (token) {
                const payload = JSON.parse(atob(token.split('.')[1]));
                const expiry = new Date(payload.exp * 1000);

                if (expiry < new Date()) {
                    console.warn("Token expired, consider refreshing authentication");
                    // Here you would typically trigger your app's token refresh logic
                    // For example: authService.refreshToken();
                }
            }
        } catch (e) {
            console.error("Error checking token expiration:", e);
        }
    });

    // Connection state change logging
    connection.onreconnecting((error) => {
        console.warn("Reconnecting to notification hub:", error);
    });

    connection.onreconnected((connectionId) => {
        console.log("Reconnected to notification hub:", connectionId);
    });

    connection.onclose((error) => {
        console.error("Connection closed:", error);
    });

    // Start the connection with retry logic
    const startConnection = async (retryAttempt = 0): Promise<signalR.HubConnection> => {
        try {
            await connection.start();
            console.log("SignalR connection established successfully");

            // Join user group to receive personalized notifications
            try {
                await connection.invoke("JoinUserGroup", userId);
                console.log(`Joined notification group for user ${userId}`);
            } catch (groupError) {
                console.error("Error joining user notification group:", groupError);
            }

            return connection;
        } catch (err) {
            const maxRetries = 5;
            const retryDelayMs = Math.min(1000 * Math.pow(2, retryAttempt), 30000);

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

    // Start the connection
    startConnection().catch(err => {
        console.error("Failed to establish notification connection:", err);
    });

    return connection;
};