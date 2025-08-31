import { useEffect, useRef, useCallback } from 'react';
import { FlowSignalRService } from '../services/flowSignalR';
import { FlowDetailDto, BatchOperationResultDto } from '../services/flowService';
import { useAuth } from '../context/AuthContext';

export const useFlowSignalR = (
    flowId: string | null,
    onFlowStatusChanged: (update: FlowDetailDto) => void,
    onBatchOperationCompleted?: (result: BatchOperationResultDto) => void,
    onError?: (error: string) => void
) => {
    const { token } = useAuth();
    const signalRServiceRef = useRef<FlowSignalRService | null>(null);
    const initializingRef = useRef<boolean>(false);

    const initializeConnection = useCallback(async () => {
        if (!flowId || !token) {
            console.log('No flowId or not authenticated, skipping SignalR connection');
            return;
        }

        if (initializingRef.current) {
            console.log('SignalR initialization already in progress');
            return;
        }

        initializingRef.current = true;

        try {
            // Clean up existing connection if any
            if (signalRServiceRef.current) {
                await signalRServiceRef.current.stop();
                signalRServiceRef.current = null;
            }

            // Create new service instance with token
            const service = new FlowSignalRService(token);

            // Set up event handlers before starting connection
            service.setFlowStatusChangedHandler(onFlowStatusChanged);
            if (onBatchOperationCompleted) {
                service.setBatchOperationCompletedHandler(onBatchOperationCompleted);
            }
            if (onError) {
                service.setErrorHandler(onError);
            }

            // Start connection first
            await service.start();

            // Then subscribe to updates for the flow
            await service.subscribe(flowId);

            // Only assign to ref after successful initialization
            signalRServiceRef.current = service;

            console.log('Flow SignalR connection established');
        } catch (error) {
            console.error('Failed to initialize flow SignalR connection:', error);
            onError?.('Failed to establish real-time connection');
            signalRServiceRef.current = null;
        } finally {
            initializingRef.current = false;
        }
    }, [flowId, token, onFlowStatusChanged, onBatchOperationCompleted, onError]);

    const cleanup = useCallback(async () => {
        if (signalRServiceRef.current) {
            try {
                await signalRServiceRef.current.stop();
            } catch (error) {
                console.error('Error during SignalR cleanup:', error);
            } finally {
                signalRServiceRef.current = null;
            }
        }
        initializingRef.current = false;
    }, []);

    // Initialize connection when flowId changes
    useEffect(() => {
        if (flowId) {
            initializeConnection();
        }

        return () => {
            cleanup();
        };
    }, [flowId, initializeConnection, cleanup]);

    return {
        isConnected: signalRServiceRef.current?.isConnected() ?? false,
        connectionState: signalRServiceRef.current?.getConnectionState() ?? null
    };
};