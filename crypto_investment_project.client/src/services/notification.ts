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
    const token = localStorage.getItem("access_token");
    console.log("JWT Token:", token);
    if (token) {
        const payload = JSON.parse(atob(token.split(".")[1]));
        console.log("JWT Payload:", payload);
    }
    const hubUrl = `https://localhost:7144/hubs/notificationHub${token ? `?access_token=${token}` : ""}`;
    console.log("SignalR URL:", hubUrl);
    const connection = new signalR.HubConnectionBuilder()
        .withUrl(hubUrl, {
            transport: signalR.HttpTransportType.WebSockets,
            // Remove accessTokenFactory since we're appending manually
        })
        .configureLogging(signalR.LogLevel.Information)
        .withAutomaticReconnect()
        .build();

    connection.start()
        .then(() => {
            console.log("✅ Connected to Notification Hub");
        })
        .catch(err => console.error("❌ Connection failed: ", err));

    connection.on("ReceiveNotification", (userId, message) => {
        console.log(`🔔 New notification for ${userId}: ${message}`);
        onNotificationReceived(message);
    });
    connection.on("UserConnected", (connectionId) => {
        console.log(`Connected with id: ${connectionId}`);
    });
    connection.on("UserFailedAuthentication", (connectionId) => {
        console.warn(`User unauthorized: ${connectionId}`);
    });
    connection.onclose(err => console.error("Connection closed:", err));

    return connection;
};
