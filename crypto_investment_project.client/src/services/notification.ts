import * as signalR from "@microsoft/signalr";
import api from "./api";

export const getNotifications = async (userId: string) => {
    if (!userId) {
        console.error("getNotifications called with undefined userId");
        return [];
    }
    const { data } = await api.get(`/Notification/${userId}`);
    return data;
};

export const markNotificationAsRead = async (notificationId: string) => {
    await api.post(`/Notification/read/${notificationId}`);
};

// SignalR Connection Setup
export const connectToNotifications = (_: string, onNotificationReceived: (message: string) => void) => {
    const token = localStorage.getItem("access_token"); // Get JWT token from storage
    console.log("JWT Token:", token); // Log raw token
    if (token) {
        const payload = JSON.parse(atob(token.split(".")[1]));
        console.log("JWT Payload:", payload);
    }
    const connection = new signalR.HubConnectionBuilder()
        .withUrl("https://localhost:7144/hubs/notificationHub", {
            //skipNegotiation: true,
            transport: signalR.HttpTransportType.WebSockets,
            accessTokenFactory: () => token || "", // Attach token
            withCredentials: true
        })
        .configureLogging(signalR.LogLevel.Information)
        .withAutomaticReconnect()
        .build();

    connection.start()
        .then(() => {
            console.log("✅ Connected to Notification Hub");

            connection.on("ReceiveNotification", (userId, message) => {
                console.log(`🔔 New notification for ${userId}: ${message}`);
                onNotificationReceived(message);
            });
            connection.on("UserConnected", (connectionId) => {
                console.log(`Connected to Notification Hub with id: ${connectionId}`);
            });
            connection.on("UserFailedAuthentication", (connectionId) => {
                console.warn(`User unauthorized: ${connectionId}`);
            });
            connection.onclose(err => console.error("Connection closed:", err));
        })
        .catch(err => console.error("❌ Connection failed: ", err));

    return connection;
};
