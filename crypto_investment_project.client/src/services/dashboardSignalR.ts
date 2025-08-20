import { signalRManager, type SignalRConnectionConfig, type ConnectionState } from "./signalRService";
import { Dashboard } from "../types/dashboardTypes";

const DASHBOARD_HUB = 'dashboard';

export class DashboardSignalRService {
    private hubName = DASHBOARD_HUB;
    private onDashboardUpdate?: (dashboard: Dashboard) => void;
    private onError?: (error: string) => void;
    private userId?: string;
    private unsubscribeHandlers: (() => void)[] = [];
    private config: SignalRConnectionConfig;
    private token?: string;

    constructor(token?: string) {
        this.token = token; // Store the token for later use
        // Configuration for the dashboard hub
        this.config = {
            enableLogging: process.env.NODE_ENV === 'development',
            reconnectAttempts: 5,
            reconnectDelays: [0, 2000, 5000, 10000, 15000, 30000],
            withCredentials: true
        };
    }

    private setupEventHandlers(): void {
        // Clear any existing handlers
        this.clearEventHandlers();

        // Register dashboard update handler
        const dashboardUpdateUnsubscribe = signalRManager.on(
            this.hubName,
            "DashboardUpdate",
            (dashboard: Dashboard) => {
                console.log("Received dashboard update:", dashboard);
                this.onDashboardUpdate?.(dashboard);
            }
        );
        this.unsubscribeHandlers.push(dashboardUpdateUnsubscribe);

        // Register error handler
        const errorUnsubscribe = signalRManager.on(
            this.hubName,
            "DashboardError",
            (error: string) => {
                console.error("Dashboard SignalR error:", error);
                this.onError?.(error);
            }
        );
        this.unsubscribeHandlers.push(errorUnsubscribe);

        // Register subscription confirmation handler
        const confirmationUnsubscribe = signalRManager.on(
            this.hubName,
            "SubscriptionConfirmed",
            (connectionId: string) => {
                console.log("Dashboard subscription confirmed:", connectionId);
            }
        );
        this.unsubscribeHandlers.push(confirmationUnsubscribe);
    }

    private clearEventHandlers(): void {
        // Unsubscribe from all handlers
        this.unsubscribeHandlers.forEach(unsubscribe => unsubscribe());
        this.unsubscribeHandlers = [];
    }

    async start(): Promise<void> {
        try {
            // Get hub URL from environment
            const apiBaseUrl = import.meta.env.VITE_SIGNALR_BASE_URL ||
                import.meta.env.VITE_API_BASE_URL?.replace('/api', '') ||
                "https://localhost:7144";
            const hubUrl = `${apiBaseUrl}/hubs/dashboardHub`;

            console.log(`Connecting to dashboard hub at: ${hubUrl}`);

            // Connect to hub using the centralized SignalR manager
            await signalRManager.connect(this.hubName, hubUrl, this.config);

            // Setup event handlers after connection is established
            this.setupEventHandlers();

            console.log("Dashboard SignalR connection started");
        } catch (error) {
            console.error("Error starting dashboard SignalR connection:", error);
            throw error;
        }
    }

    async subscribe(userId: string): Promise<void> {
        this.userId = userId;

        // Ensure connection is established
        if (!signalRManager.isConnected(this.hubName)) {
            await this.start();
        }

        // Verify connection is now established
        if (!signalRManager.isConnected(this.hubName)) {
            throw new Error("Failed to establish SignalR connection");
        }

        try {
            await signalRManager.invoke(this.hubName, "SubscribeToUpdates", userId);
            console.log(`Subscribed to dashboard updates for user ${userId}`);
        } catch (error) {
            console.error("Error subscribing to dashboard updates:", error);
            throw error;
        }
    }

    async refreshDashboard(): Promise<void> {
        if (!signalRManager.isConnected(this.hubName)) {
            console.warn("SignalR connection not available for manual refresh");
            return;
        }

        try {
            await signalRManager.invoke(this.hubName, "RefreshDashboard");
            console.log("Dashboard refresh requested");
        } catch (error) {
            console.error("Error refreshing dashboard:", error);
            throw error;
        }
    }

    setUpdateHandler(handler: (dashboard: Dashboard) => void): void {
        this.onDashboardUpdate = handler;
    }

    setErrorHandler(handler: (error: string) => void): void {
        this.onError = handler;
    }

    async stop(): Promise<void> {
        try {
            // Clear event handlers first
            this.clearEventHandlers();

            // Disconnect from the hub
            await signalRManager.disconnect(this.hubName);

            console.log("Dashboard SignalR connection stopped");
        } catch (error) {
            console.error("Error stopping dashboard SignalR connection:", error);
        }
    }

    getConnectionState(): ConnectionState | null {
        return signalRManager.getConnectionState(this.hubName);
    }

    isConnected(): boolean {
        return signalRManager.isConnected(this.hubName);
    }

    // Re-establish connection and re-subscribe if needed
    async reconnect(): Promise<void> {
        try {
            await this.stop();
            await this.start();

            if (this.userId) {
                await this.subscribe(this.userId);
            }
        } catch (error) {
            console.error("Error reconnecting dashboard SignalR:", error);
            throw error;
        }
    }
}

// Export convenience functions for direct hub management
export async function connectToDashboardHub(
    userId: string,
    onDashboardUpdate: (dashboard: Dashboard) => void,
    onError?: (error: string) => void,
    config: SignalRConnectionConfig = {}
): Promise<DashboardSignalRService> {
    if (!userId) {
        throw new Error("User ID is required for dashboard connection");
    }

    console.log("Connecting to dashboard hub with userId ", userId);

    const service = new DashboardSignalRService();

    // Set handlers
    service.setUpdateHandler(onDashboardUpdate);
    if (onError) {
        service.setErrorHandler(onError);
    }

    // Start connection and subscribe
    await service.start();
    await service.subscribe(userId);

    return service;
}

export async function disconnectFromDashboardHub(): Promise<void> {
    await signalRManager.disconnect(DASHBOARD_HUB);
    console.log("Disconnected from dashboard hub");
}

export function getDashboardHubState(): ConnectionState | null {
    return signalRManager.getConnectionState(DASHBOARD_HUB);
}

export function isDashboardHubConnected(): boolean {
    return signalRManager.isConnected(DASHBOARD_HUB);
}