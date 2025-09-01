import { signalRManager, type SignalRConnectionConfig, type ConnectionState } from "./signalRService";
import { FlowDetailDto, BatchOperationResultDto } from "../services/flowService";

const FLOW_HUB = 'flow';

export class FlowSignalRService {
    private hubName = FLOW_HUB;
    private onFlowStatusChanged?: (update: FlowDetailDto) => void;
    private onFlowStateChanged?: (update: FlowDetailDto) => void;
    private onBatchOperationCompleted?: (result: BatchOperationResultDto) => void;
    private onError?: (error: string) => void;
    private unsubscribeHandlers: (() => void)[] = [];
    private config: SignalRConnectionConfig;
    private token?: string;

    constructor(token?: string) {
        this.token = token;
        this.config = {
            enableLogging: process.env.NODE_ENV === 'development',
            reconnectAttempts: 5,
            reconnectDelays: [0, 2000, 5000, 10000, 15000, 30000],
            withCredentials: true
        };
    }

    private setupEventHandlers(): void {
        this.clearEventHandlers();

        // Flow status changed event
        const flowStatusUnsubscribe = signalRManager.on(
            this.hubName,
            "FlowStatusChanged",
            (update: FlowDetailDto) => {
                console.log("Received flow status update:", update);
                this.onFlowStatusChanged?.(update);
            }
        );
        this.unsubscribeHandlers.push(flowStatusUnsubscribe);

        // Flow state changed event
        const flowStateUnsubscribe = signalRManager.on(
            this.hubName,
            "FlowStateChanged",
            (update: FlowDetailDto) => {
                console.log("Received flow state update:", update);
                this.onFlowStateChanged?.(update);
            }
        );
        this.unsubscribeHandlers.push(flowStateUnsubscribe);

        // Batch operation completed event
        const batchOpUnsubscribe = signalRManager.on(
            this.hubName,
            "BatchOperationCompleted",
            (result: BatchOperationResultDto) => {
                console.log("Batch operation completed:", result);
                this.onBatchOperationCompleted?.(result);
            }
        );
        this.unsubscribeHandlers.push(batchOpUnsubscribe);

        // Error event
        const errorUnsubscribe = signalRManager.on(
            this.hubName,
            "FlowError",
            (error: string) => {
                console.error("Flow SignalR error:", error);
                this.onError?.(error);
            }
        );
        this.unsubscribeHandlers.push(errorUnsubscribe);

        // Subscription confirmation
        const confirmationUnsubscribe = signalRManager.on(
            this.hubName,
            "SubscriptionConfirmed",
            (connectionId: string) => {
                console.log("Flow subscription confirmed:", connectionId);
            }
        );
        this.unsubscribeHandlers.push(confirmationUnsubscribe);
    }

    private clearEventHandlers(): void {
        this.unsubscribeHandlers.forEach(unsubscribe => unsubscribe());
        this.unsubscribeHandlers = [];
    }

    async start(): Promise<void> {
        try {
            const apiBaseUrl = import.meta.env.VITE_SIGNALR_BASE_URL ||
                import.meta.env.VITE_API_BASE_URL?.replace('/api', '') ||
                "https://localhost:7144";
            const hubUrl = `${apiBaseUrl}/hubs/flowHub`;

            console.log(`Connecting to flow hub at: ${hubUrl}`);

            await signalRManager.connect(this.hubName, hubUrl, this.config);

            this.setupEventHandlers();

            console.log("Flow SignalR connection started");
        } catch (error) {
            console.error("Error starting flow SignalR connection:", error);
            throw error;
        }
    }

    async subscribe(flowId: string): Promise<void> {
        if (!signalRManager.isConnected(this.hubName)) {
            await this.start();
        }

        if (!signalRManager.isConnected(this.hubName)) {
            throw new Error("Failed to establish SignalR connection");
        }

        try {
            await signalRManager.invoke(this.hubName, "SubscribeToFlow", flowId);
            console.log(`Subscribed to flow updates for flowId ${flowId}`);
        } catch (error) {
            console.error("Error subscribing to flow updates:", error);
            throw error;
        }
    }

    async unsubscribe(flowId: string): Promise<void> {
        if (!signalRManager.isConnected(this.hubName)) {
            return;
        }

        try {
            await signalRManager.invoke(this.hubName, "UnsubscribeFromFlow", flowId);
            console.log(`Unsubscribed from flow updates for flowId ${flowId}`);
        } catch (error) {
            console.error("Error unsubscribing from flow updates:", error);
            throw error;
        }
    }

    setFlowStatusChangedHandler(handler: (update: FlowDetailDto) => void): void {
        this.onFlowStatusChanged = handler;
    }

    setFlowStateChangedHandler(handler: (update: FlowDetailDto) => void): void {
        this.onFlowStateChanged = handler;
    }

    setBatchOperationCompletedHandler(handler: (result: BatchOperationResultDto) => void): void {
        this.onBatchOperationCompleted = handler;
    }

    setErrorHandler(handler: (error: string) => void): void {
        this.onError = handler;
    }

    async stop(): Promise<void> {
        try {
            this.clearEventHandlers();
            await signalRManager.disconnect(this.hubName);
            console.log("Flow SignalR connection stopped");
        } catch (error) {
            console.error("Error stopping flow SignalR connection:", error);
        }
    }

    getConnectionState(): ConnectionState | null {
        return signalRManager.getConnectionState(this.hubName);
    }

    isConnected(): boolean {
        return signalRManager.isConnected(this.hubName);
    }

    async reconnect(): Promise<void> {
        try {
            await this.stop();
            await this.start();
        } catch (error) {
            console.error("Error reconnecting flow SignalR:", error);
            throw error;
        }
    }
}

// Convenience functions for direct hub management

export async function connectToFlowHub(
    flowId: string,
    onFlowStatusChanged: (update: FlowDetailDto) => void,
    onFlowStateChanged: (update: FlowDetailDto) => void,
    onBatchOperationCompleted?: (result: BatchOperationResultDto) => void,
    onError?: (error: string) => void,
    config: SignalRConnectionConfig = {}
): Promise<FlowSignalRService> {
    if (!flowId) {
        throw new Error("Flow ID is required for flow connection");
    }

    console.log("Connecting to flow hub with flowId", flowId);

    const service = new FlowSignalRService();

    service.setFlowStatusChangedHandler(onFlowStatusChanged);
    service.setFlowStateChangedHandler(onFlowStateChanged);
    if (onBatchOperationCompleted) {
        service.setBatchOperationCompletedHandler(onBatchOperationCompleted);
    }
    if (onError) {
        service.setErrorHandler(onError);
    }

    await service.start();
    await service.subscribe(flowId);

    return service;
}

export async function disconnectFromFlowHub(): Promise<void> {
    await signalRManager.disconnect(FLOW_HUB);
    console.log("Disconnected from flow hub");
}

export function getFlowHubState(): ConnectionState | null {
    return signalRManager.getConnectionState(FLOW_HUB);
}

export function isFlowHubConnected(): boolean {
    return signalRManager.isConnected(FLOW_HUB);
}