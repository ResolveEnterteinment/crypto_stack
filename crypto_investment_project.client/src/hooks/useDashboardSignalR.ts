import { useEffect, useRef, useCallback } from 'react';
import { DashboardSignalRService } from '../services/dashboardSignalR';
import { Dashboard } from '../types/dashboardTypes';
import { useAuth } from '../context/AuthContext';

export const useDashboardSignalR = (
    onDashboardUpdate: (dashboard: Dashboard) => void,
    onError?: (error: string) => void
) => {
    const { user, token } = useAuth(); // Now token is available
    const signalRServiceRef = useRef<DashboardSignalRService | null>(null);
    const initializingRef = useRef<boolean>(false);

    const initializeConnection = useCallback(async () => {
        if (!user?.id || !token) {
            console.log('User not authenticated, skipping SignalR connection');
            return;
        }

        // Prevent multiple simultaneous initializations
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
            const service = new DashboardSignalRService(token);

            // Set up event handlers before starting connection
            service.setUpdateHandler(onDashboardUpdate);
            if (onError) {
                service.setErrorHandler(onError);
            }

            // Start connection first
            await service.start();

            // Then subscribe to updates
            await service.subscribe(user.id);

            // Only assign to ref after successful initialization
            signalRServiceRef.current = service;

            console.log('Dashboard SignalR connection established');
        } catch (error) {
            console.error('Failed to initialize dashboard SignalR connection:', error);
            onError?.('Failed to establish real-time connection');

            // Ensure ref is null on failure
            signalRServiceRef.current = null;
        } finally {
            initializingRef.current = false;
        }
    }, [user?.id, token, onDashboardUpdate, onError]);

    const refreshDashboard = useCallback(async () => {
        if (signalRServiceRef.current && signalRServiceRef.current.isConnected()) {
            try {
                await signalRServiceRef.current.refreshDashboard();
            } catch (error) {
                console.error('Failed to refresh dashboard:', error);
                onError?.('Failed to refresh dashboard');
            }
        } else {
            console.warn('SignalR service not connected, cannot refresh dashboard');
        }
    }, [onError]);

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

    // Initialize connection when user changes
    useEffect(() => {
        initializeConnection();

        return () => {
            cleanup();
        };
    }, [initializeConnection, cleanup]);

    return {
        refreshDashboard,
        isConnected: signalRServiceRef.current?.isConnected() ?? false,
        connectionState: signalRServiceRef.current?.getConnectionState() ?? null
    };
};