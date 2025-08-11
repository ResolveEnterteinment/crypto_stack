import * as signalR from "@microsoft/signalr";
import { Dashboard } from "../types/dashboardTypes";

export class DashboardSignalRService {
    private connection: signalR.HubConnection | null = null;
    private onDashboardUpdate?: (dashboard: Dashboard) => void;
    private onError?: (error: string) => void;

    constructor(token?: string) {
        const connectionBuilder = new signalR.HubConnectionBuilder()
            .withUrl("/hubs/dashboardHub", {
                accessTokenFactory: () => token || localStorage.getItem('authToken') || ""
            })
            .withAutomaticReconnect();

        this.connection = connectionBuilder.build();
        this.setupEventHandlers();
    }

    private setupEventHandlers() {
        if (!this.connection) return;

        this.connection.on("DashboardUpdate", (dashboard: Dashboard) => {
            console.log("Received dashboard update:", dashboard);
            this.onDashboardUpdate?.(dashboard);
        });

        this.connection.on("DashboardError", (error: string) => {
            console.error("Dashboard SignalR error:", error);
            this.onError?.(error);
        });

        this.connection.on("SubscriptionConfirmed", (connectionId: string) => {
            console.log("Dashboard subscription confirmed:", connectionId);
        });

        // Handle connection state changes
        this.connection.onreconnecting(() => {
            console.log("Dashboard SignalR reconnecting...");
        });

        this.connection.onreconnected(() => {
            console.log("Dashboard SignalR reconnected");
        });

        this.connection.onclose(() => {
            console.log("Dashboard SignalR connection closed");
        });
    }

    async start(): Promise<void> {
        if (!this.connection) return;

        try {
            await this.connection.start();
            console.log("Dashboard SignalR connection started");
        } catch (error) {
            console.error("Error starting dashboard SignalR connection:", error);
            throw error;
        }
    }

    async subscribe(userId: string): Promise<void> {
        if (!this.connection || this.connection.state !== signalR.HubConnectionState.Connected) {
            await this.start();
        }

        if (!this.connection) {
            console.error("Failed to establish SignalR connection");
            return;
        }

        try {
            await this.connection.invoke("SubscribeToUpdates", userId);
        } catch (error) {
            console.error("Error subscribing to dashboard updates:", error);
            throw error;
        }
    }

    async refreshDashboard(): Promise<void> {
        if (!this.connection || this.connection.state !== signalR.HubConnectionState.Connected) {
            console.warn("SignalR connection not available for manual refresh");
            return;
        }

        try {
            await this.connection.invoke("RefreshDashboard");
        } catch (error) {
            console.error("Error refreshing dashboard:", error);
            throw error;
        }
    }

    setUpdateHandler(handler: (dashboard: Dashboard) => void) {
        this.onDashboardUpdate = handler;
    }

    setErrorHandler(handler: (error: string) => void) {
        this.onError = handler;
    }

    async stop(): Promise<void> {
        if (this.connection) {
            await this.connection.stop();
        }
    }

    getConnectionState(): signalR.HubConnectionState | null {
        return this.connection?.state || null;
    }
}