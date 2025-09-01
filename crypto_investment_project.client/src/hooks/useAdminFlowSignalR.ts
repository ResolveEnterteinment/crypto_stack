import { useEffect, useRef, useCallback, useState } from 'react';
import { AdminFlowSignalRService } from '../services/adminFlowSignalR';
import { FlowDetailDto, BatchOperationResultDto } from '../services/flowService';
import { useAuth } from '../context/AuthContext';

/**
 * Hook for admin-level flow monitoring
 * Subscribes to ALL flow updates across the system
 */
export const useAdminFlowSignalR = (
    onFlowStatusChanged: (update: FlowDetailDto) => void,
    onBatchOperationCompleted?: (result: BatchOperationResultDto) => void,
    onError?: (error: string) => void
) => {
    const { token } = useAuth();
    const signalRServiceRef = useRef<AdminFlowSignalRService | null>(null);
    const initializingRef = useRef<boolean>(false);

    const [connectionState, setConnectionState] = useState<{
        isConnected: boolean;
        isConnecting: boolean;
        error: string | null;
    }>({
        isConnected: false,
        isConnecting: false,
        error: null
    });

    const initializeConnection = useCallback(async () => {
        if (!token) {
            console.log('Not authenticated, skipping admin SignalR connection');
            return;
        }

        if (initializingRef.current) {
            console.log('Admin SignalR initialization already in progress');
            return;
        }

        initializingRef.current = true;
        setConnectionState(prev => ({ ...prev, isConnecting: true, error: null }));

        try {
            // Clean up existing connection if any
            if (signalRServiceRef.current) {
                await signalRServiceRef.current.stop();
                signalRServiceRef.current = null;
            }

            // Create new service instance
            const service = new AdminFlowSignalRService(token);

            // Set up event handlers
            service.setFlowStatusChangedHandler(onFlowStatusChanged);

            if (onBatchOperationCompleted) {
                service.setBatchOperationCompletedHandler(onBatchOperationCompleted);
            }

            if (onError) {
                service.setErrorHandler(onError);
            }

            // Start connection and join admin group
            await service.start();
            await service.joinAdminGroup();

            signalRServiceRef.current = service;

            setConnectionState({
                isConnected: true,
                isConnecting: false,
                error: null
            });

            console.log('Admin Flow SignalR connection established');
        } catch (error) {
            console.error('Failed to initialize admin flow SignalR connection:', error);
            const errorMessage = error instanceof Error ? error.message : 'Connection failed';

            setConnectionState({
                isConnected: false,
                isConnecting: false,
                error: errorMessage
            });

            onError?.(errorMessage);
        } finally {
            initializingRef.current = false;
        }
    }, [token, onFlowStatusChanged, onBatchOperationCompleted, onError]);

    const disconnect = useCallback(async () => {
        if (signalRServiceRef.current) {
            try {
                await signalRServiceRef.current.stop();
            } catch (error) {
                console.error('Error stopping admin SignalR connection:', error);
            } finally {
                signalRServiceRef.current = null;
                setConnectionState({
                    isConnected: false,
                    isConnecting: false,
                    error: null
                });
            }
        }
    }, []);

    const reconnect = useCallback(async () => {
        await disconnect();
        await initializeConnection();
    }, [disconnect, initializeConnection]);

    // Initialize connection on mount
    useEffect(() => {
        initializeConnection();

        return () => {
            disconnect();
        };
    }, [token]); // Reconnect when token changes

    return {
        ...connectionState,
        reconnect,
        disconnect
    };
};