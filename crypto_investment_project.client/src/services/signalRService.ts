// src/services/signalRService.ts
import * as signalR from "@microsoft/signalr";
import { tokenManager } from "./api";

// ==================== Types & Interfaces ====================

export interface SignalRConnectionConfig {
    reconnectAttempts?: number;
    reconnectDelays?: number[];
    enableLogging?: boolean;
    withCredentials?: boolean;
}

export interface ConnectionState {
    status: 'connected' | 'connecting' | 'disconnected' | 'reconnecting';
    lastError: string | null;
    lastErrorTime: Date | null;
    connectionAttempts: number;
    connectionId: string | null;
}

// Generic message handler that accepts any number of arguments
export type MessageHandler = (...args: any[]) => void;

// ==================== SignalR Connection Manager ====================

class SignalRConnectionManager {
    private connections: Map<string, signalR.HubConnection> = new Map();
    private connectionStates: Map<string, ConnectionState> = new Map();
    private messageHandlers: Map<string, Map<string, MessageHandler[]>> = new Map();

    /**
     * Creates or retrieves a SignalR hub connection
     */
    async connect(
        hubName: string,
        hubUrl: string,
        config: SignalRConnectionConfig = {}
    ): Promise<signalR.HubConnection> {
        // Check if connection already exists and is connected
        const existingConnection = this.connections.get(hubName);
        if (existingConnection && existingConnection.state === signalR.HubConnectionState.Connected) {
            console.log(`Reusing existing connection for hub: ${hubName}`);
            return existingConnection;
        }

        // Stop existing connection if it's in a bad state
        if (existingConnection) {
            await this.disconnect(hubName);
        }

        // Initialize connection state
        this.updateConnectionState(hubName, {
            status: 'connecting',
            lastError: null,
            lastErrorTime: null,
            connectionAttempts: 0,
            connectionId: null
        });

        // Get authentication token
        const token = tokenManager.getAccessToken();
        if (!token) {
            console.warn(`No authentication token found for ${hubName} hub connection`);
        }

        // Configure logging level
        const logLevel = config.enableLogging ?
            signalR.LogLevel.Information :
            signalR.LogLevel.Warning;

        // Build connection
        const connectionBuilder = new signalR.HubConnectionBuilder()
            .withUrl(hubUrl, {
                accessTokenFactory: () => token || "",
                transport: signalR.HttpTransportType.WebSockets,
                skipNegotiation: true,
                withCredentials: config.withCredentials !== false
            })
            .configureLogging(logLevel);

        // Configure automatic reconnect
        if (config.reconnectDelays && config.reconnectDelays.length > 0) {
            connectionBuilder.withAutomaticReconnect(config.reconnectDelays);
        } else {
            connectionBuilder.withAutomaticReconnect([0, 2000, 5000, 10000, 15000, 30000]);
        }

        const connection = connectionBuilder.build();

        // Setup event handlers
        this.setupConnectionHandlers(hubName, connection);

        // Store connection
        this.connections.set(hubName, connection);

        // Start connection with retry logic
        await this.startConnection(hubName, connection, config);

        return connection;
    }

    /**
     * Disconnects a hub connection
     */
    async disconnect(hubName: string): Promise<void> {
        const connection = this.connections.get(hubName);
        if (!connection) return;

        try {
            await connection.stop();
            console.log(`Disconnected from ${hubName} hub`);
        } catch (err) {
            console.warn(`Error disconnecting from ${hubName} hub:`, err);
        } finally {
            this.connections.delete(hubName);
            this.messageHandlers.delete(hubName);
            this.updateConnectionState(hubName, {
                status: 'disconnected',
                lastError: null,
                lastErrorTime: null,
                connectionAttempts: 0,
                connectionId: null
            });
        }
    }

    /**
     * Disconnects all hub connections
     */
    async disconnectAll(): Promise<void> {
        const disconnectPromises = Array.from(this.connections.keys()).map(hubName =>
            this.disconnect(hubName)
        );
        await Promise.all(disconnectPromises);
    }

    /**
     * Registers a message handler for a specific event
     */
    on(
        hubName: string,
        eventName: string,
        handler: MessageHandler
    ): () => void {
        const connection = this.connections.get(hubName);
        if (!connection) {
            throw new Error(`No connection found for hub: ${hubName}`);
        }

        // Store handler for re-registration on reconnect
        if (!this.messageHandlers.has(hubName)) {
            this.messageHandlers.set(hubName, new Map());
        }
        const hubHandlers = this.messageHandlers.get(hubName)!;

        if (!hubHandlers.has(eventName)) {
            hubHandlers.set(eventName, []);
        }
        hubHandlers.get(eventName)!.push(handler);

        // Register with SignalR
        connection.on(eventName, handler);

        // Return unsubscribe function
        return () => {
            connection.off(eventName, handler);
            const handlers = hubHandlers.get(eventName);
            if (handlers) {
                const index = handlers.indexOf(handler);
                if (index > -1) {
                    handlers.splice(index, 1);
                }
            }
        };
    }

    /**
     * Removes a specific event handler
     */
    off(hubName: string, eventName: string, handler?: MessageHandler): void {
        const connection = this.connections.get(hubName);
        if (!connection) return;

        if (handler) {
            connection.off(eventName, handler);

            // Remove from stored handlers
            const hubHandlers = this.messageHandlers.get(hubName);
            if (hubHandlers) {
                const handlers = hubHandlers.get(eventName);
                if (handlers) {
                    const index = handlers.indexOf(handler);
                    if (index > -1) {
                        handlers.splice(index, 1);
                    }
                }
            }
        } else {
            // Remove all handlers for this event
            connection.off(eventName);

            const hubHandlers = this.messageHandlers.get(hubName);
            if (hubHandlers) {
                hubHandlers.delete(eventName);
            }
        }
    }

    /**
     * Invokes a method on the hub
     */
    async invoke<T = any>(
        hubName: string,
        methodName: string,
        ...args: any[]
    ): Promise<T> {
        const connection = this.connections.get(hubName);
        if (!connection) {
            throw new Error(`No connection found for hub: ${hubName}`);
        }

        if (connection.state !== signalR.HubConnectionState.Connected) {
            throw new Error(`Hub ${hubName} is not connected`);
        }

        try {
            const result = await connection.invoke<T>(methodName, ...args);
            return result;
        } catch (error) {
            console.error(`Error invoking ${methodName} on ${hubName}:`, error);
            throw error;
        }
    }

    /**
     * Sends a message to the hub (fire and forget)
     */
    async send(
        hubName: string,
        methodName: string,
        ...args: any[]
    ): Promise<void> {
        const connection = this.connections.get(hubName);
        if (!connection) {
            throw new Error(`No connection found for hub: ${hubName}`);
        }

        if (connection.state !== signalR.HubConnectionState.Connected) {
            throw new Error(`Hub ${hubName} is not connected`);
        }

        try {
            await connection.send(methodName, ...args);
        } catch (error) {
            console.error(`Error sending ${methodName} to ${hubName}:`, error);
            throw error;
        }
    }

    /**
     * Gets the current connection state for a hub
     */
    getConnectionState(hubName: string): ConnectionState | null {
        return this.connectionStates.get(hubName) || null;
    }

    /**
     * Gets the SignalR connection for a hub
     */
    getConnection(hubName: string): signalR.HubConnection | null {
        return this.connections.get(hubName) || null;
    }

    /**
     * Checks if a hub is connected
     */
    isConnected(hubName: string): boolean {
        const connection = this.connections.get(hubName);
        return connection?.state === signalR.HubConnectionState.Connected || false;
    }

    /**
     * Gets all active hub names
     */
    getActiveHubs(): string[] {
        return Array.from(this.connections.keys());
    }

    // ==================== Private Methods ====================

    private setupConnectionHandlers(hubName: string, connection: signalR.HubConnection): void {
        // Handle reconnecting
        connection.onreconnecting((error) => {
            console.warn(`${hubName} reconnecting:`, error?.message);
            this.updateConnectionState(hubName, {
                status: 'reconnecting',
                lastError: error?.message || 'Connection lost',
                lastErrorTime: new Date(),
                connectionAttempts: (this.getConnectionState(hubName)?.connectionAttempts || 0) + 1,
                connectionId: null
            });
        });

        // Handle reconnected
        connection.onreconnected((connectionId) => {
            console.log(`${hubName} reconnected with ID:`, connectionId);
            this.updateConnectionState(hubName, {
                status: 'connected',
                lastError: null,
                lastErrorTime: null,
                connectionAttempts: 0,
                connectionId: connectionId || null
            });

            // Re-register all handlers
            this.reregisterHandlers(hubName, connection);
        });

        // Handle close
        connection.onclose((error) => {
            console.log(`${hubName} connection closed:`, error?.message);
            this.updateConnectionState(hubName, {
                status: 'disconnected',
                lastError: error?.message || 'Connection closed',
                lastErrorTime: new Date(),
                connectionAttempts: 0,
                connectionId: null
            });
        });
    }

    private async startConnection(
        hubName: string,
        connection: signalR.HubConnection,
        config: SignalRConnectionConfig
    ): Promise<void> {
        const maxRetries = config.reconnectAttempts || 5;
        const retryDelays = config.reconnectDelays || [0, 2000, 5000, 10000, 30000];
        let retryAttempt = 0;

        const attemptStart = async (): Promise<void> => {
            this.updateConnectionState(hubName, {
                ...this.getConnectionState(hubName)!,
                connectionAttempts: retryAttempt + 1
            });

            try {
                await connection.start();
                const connectionId = connection.connectionId || null;

                console.log(`${hubName} connection established. ID: ${connectionId}`);

                this.updateConnectionState(hubName, {
                    status: 'connected',
                    lastError: null,
                    lastErrorTime: null,
                    connectionAttempts: 0,
                    connectionId
                });
            } catch (err: any) {
                const retryDelayMs = retryDelays[Math.min(retryAttempt, retryDelays.length - 1)];

                this.updateConnectionState(hubName, {
                    status: 'disconnected',
                    lastError: err?.message || 'Failed to connect',
                    lastErrorTime: new Date(),
                    connectionAttempts: retryAttempt + 1,
                    connectionId: null
                });

                if (retryAttempt < maxRetries) {
                    console.warn(
                        `${hubName} connection failed, retrying in ${retryDelayMs / 1000}s... ` +
                        `(Attempt ${retryAttempt + 1}/${maxRetries})`
                    );
                    retryAttempt++;

                    await new Promise(resolve => setTimeout(resolve, retryDelayMs));
                    return attemptStart();
                } else {
                    throw new Error(`Failed to connect to ${hubName} after ${maxRetries} attempts`);
                }
            }
        };

        await attemptStart();
    }

    private reregisterHandlers(hubName: string, connection: signalR.HubConnection): void {
        const handlers = this.messageHandlers.get(hubName);
        if (!handlers) return;

        handlers.forEach((eventHandlers, eventName) => {
            eventHandlers.forEach(handler => {
                connection.on(eventName, handler);
            });
        });

        console.log(`Re-registered ${handlers.size} event handlers for ${hubName}`);
    }

    private updateConnectionState(hubName: string, state: ConnectionState): void {
        this.connectionStates.set(hubName, state);
    }
}

// ==================== Singleton Instance ====================

export const signalRManager = new SignalRConnectionManager();