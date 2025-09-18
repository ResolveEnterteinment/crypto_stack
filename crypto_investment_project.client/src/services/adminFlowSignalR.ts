import { signalRManager, type SignalRConnectionConfig, type ConnectionState } from "./signalRService";
import { FlowDetailDto, BatchOperationResultDto, StepResultDto } from "../services/flowService";

const FLOW_HUB = 'flow';

// New interface for step status updates
interface StepStatusUpdateDto {
    flowId: string;
    stepName: string;
    stepStatus: string;
    stepResult?: StepResultDto;
    currentStepIndex: number;
    currentStepName: string;
    flowStatus: string;
    timestamp: string;
}

/**
 * SignalR service for admin-level flow monitoring
 * Subscribes to all flow updates across the system
 */
export class AdminFlowSignalRService {
    private hubName = FLOW_HUB;
    private onFlowStatusChanged?: (update: FlowDetailDto) => void;
    private onStepStatusChanged?: (update: StepStatusUpdateDto) => void;
    private onBatchOperationCompleted?: (result: BatchOperationResultDto) => void;
    private onError?: (error: string) => void;
    private unsubscribeHandlers: (() => void)[] = [];
    private config: SignalRConnectionConfig;

    constructor() {
        this.config = {
            enableLogging: process.env.NODE_ENV === 'development',
            reconnectAttempts: 10, // More attempts for admin connection
            reconnectDelays: [0, 2000, 5000, 10000, 15000, 30000, 60000], // Longer delays
            withCredentials: true
        };
    }

    private setupEventHandlers(): void {
        this.clearEventHandlers();

        // Listen for all flow status changes
        const flowStatusUnsubscribe = signalRManager.on(
            this.hubName,
            "FlowStatusChanged",
            (update: FlowDetailDto) => {
                console.log("Admin received flow status update:", update);
                this.onFlowStatusChanged?.(update);
            }
        );
        this.unsubscribeHandlers.push(flowStatusUnsubscribe);

        // NEW: Listen for step status changes
        const stepStatusUnsubscribe = signalRManager.on(
            this.hubName,
            "StepStatusChanged",
            (update: StepStatusUpdateDto) => {
                console.log("Admin received step status update:", update);
                this.onStepStatusChanged?.(update);
            }
        );
        this.unsubscribeHandlers.push(stepStatusUnsubscribe);

        // Batch operation completed event
        const batchOpUnsubscribe = signalRManager.on(
            this.hubName,
            "BatchOperationCompleted",
            (result: BatchOperationResultDto) => {
                console.log("Admin received batch operation result:", result);
                this.onBatchOperationCompleted?.(result);
            }
        );
        this.unsubscribeHandlers.push(batchOpUnsubscribe);

        // Error event
        const errorUnsubscribe = signalRManager.on(
            this.hubName,
            "FlowError",
            (flowId: string, error: string) => {
                console.error("Admin received flow error:", flowId, error);
                this.onError?.(error);
            }
        );
        this.unsubscribeHandlers.push(errorUnsubscribe);

        // Admin group join confirmation
        const adminJoinUnsubscribe = signalRManager.on(
            this.hubName,
            "AdminGroupJoined",
            () => {
                console.log("Successfully joined flow-admins group");
            }
        );
        this.unsubscribeHandlers.push(adminJoinUnsubscribe);
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

            console.log(`Admin connecting to flow hub at: ${hubUrl}`);

            await signalRManager.connect(this.hubName, hubUrl, this.config);

            this.setupEventHandlers();

            console.log("Admin Flow SignalR connection started");
        } catch (error) {
            console.error("Error starting admin flow SignalR connection:", error);
            throw error;
        }
    }

    async joinAdminGroup(): Promise<void> {
        if (!signalRManager.isConnected(this.hubName)) {
            throw new Error("Not connected to flow hub");
        }

        try {
            // The FlowHub automatically adds admin users to the flow-admins group on connection
            // But we can also explicitly join if needed
            console.log("Admin automatically joined flow-admins group on connection");
        } catch (error) {
            console.error("Error joining admin group:", error);
            // Non-fatal - the server-side OnConnectedAsync should handle this
        }
    }

    setFlowStatusChangedHandler(handler: (update: FlowDetailDto) => void): void {
        this.onFlowStatusChanged = handler;
    }

    setStepStatusChangedHandler(handler: (update: StepStatusUpdateDto) => void): void {
        this.onStepStatusChanged = handler;
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
            console.log("Admin Flow SignalR connection stopped");
        } catch (error) {
            console.error("Error stopping admin flow SignalR connection:", error);
        }
    }

    getConnectionState(): ConnectionState | null {
        return signalRManager.getConnectionState(this.hubName);
    }

    isConnected(): boolean {
        return signalRManager.isConnected(this.hubName);
    }
}

// Export the type for use in components
export type { StepStatusUpdateDto };