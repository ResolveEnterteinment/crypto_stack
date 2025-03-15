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
export const connectToNotifications = (userId: string, onNotificationReceived: (message: string) => void) => {
    const connection = new signalR.HubConnectionBuilder()
        .withUrl("https://localhost:7144/notificationHub", {
            skipNegotiation: true, // Avoids "negotiate" error
            transport: signalR.HttpTransportType.WebSockets, // Ensures WebSocket usage
            withCredentials: true // Allows CORS credentials
        })
        .configureLogging(signalR.LogLevel.Information)
        .withAutomaticReconnect()
        .build();

    connection.start()
        .then(() => console.log("Connected to Notification Hub"))
        .catch(err => console.error("Connection failed: ", err));

    connection.on("ReceiveNotification", onNotificationReceived);

    return connection;
};
