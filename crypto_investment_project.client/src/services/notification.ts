import * as signalR from "@microsoft/signalr";
import api from "./api";

export const getNotifications = async (userId: string) => {
    const { data } = await api.get(`/notification/${userId}`);
    return data;
};

export const markNotificationAsRead = async (notificationId: string) => {
    await api.post(`/notification/read/${notificationId}`);
};

// SignalR Connection Setup
export const connectToNotifications = (userId: string, onNotificationReceived: (message: string) => void) => {
    const connection = new signalR.HubConnectionBuilder()
        .withUrl("http://localhost:5000/notificationHub") // Adjust URL based on your API
        .configureLogging(signalR.LogLevel.Information)
        .withAutomaticReconnect()
        .build();

    connection.start()
        .then(() => console.log("Connected to Notification Hub"))
        .catch(err => console.error("Connection failed: ", err));

    connection.on("ReceiveNotification", onNotificationReceived);

    return connection;
};
