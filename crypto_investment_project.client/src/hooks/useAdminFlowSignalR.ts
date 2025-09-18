import { useEffect, useRef, useState } from 'react';
import { AdminFlowSignalRService, type StepStatusUpdateDto } from '../services/adminFlowSignalR';
import type { FlowDetailDto, BatchOperationResultDto } from '../services/flowService';

export function useAdminFlowSignalR(
    onFlowStatusChanged?: (update: FlowDetailDto) => void,
    onStepStatusChanged?: (update: StepStatusUpdateDto) => void,
    onError?: (error: string) => void
) {
    const [isConnected, setIsConnected] = useState(false);
    const serviceRef = useRef<AdminFlowSignalRService>();

    useEffect(() => {
        const service = new AdminFlowSignalRService();
        serviceRef.current = service;

        const startConnection = async () => {
            try {
                // Set up handlers before starting
                if (onFlowStatusChanged) {
                    service.setFlowStatusChangedHandler(onFlowStatusChanged);
                }

                if (onStepStatusChanged) {
                    service.setStepStatusChangedHandler(onStepStatusChanged);
                }

                if (onError) {
                    service.setErrorHandler(onError);
                }

                await service.start();
                await service.joinAdminGroup();
                setIsConnected(true);
            } catch (error) {
                console.error('Failed to start admin flow SignalR connection:', error);
                setIsConnected(false);
                onError?.('Failed to connect to flow updates');
            }
        };

        startConnection();

        return () => {
            service.stop();
            setIsConnected(false);
        };
    }, [onFlowStatusChanged, onStepStatusChanged, onError]);

    const reconnect = async () => {
        if (serviceRef.current) {
            try {
                await serviceRef.current.stop();
                await serviceRef.current.start();
                await serviceRef.current.joinAdminGroup();
                setIsConnected(true);
            } catch (error) {
                console.error('Failed to reconnect admin flow SignalR:', error);
                setIsConnected(false);
                onError?.('Failed to reconnect to flow updates');
            }
        }
    };

    return {
        isConnected,
        reconnect
    };
}