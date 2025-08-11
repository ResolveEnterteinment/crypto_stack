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

    const initializeConnection = useCallback(async () => {
        if (!user?.id || !token) {
            console.log('User not authenticated, skipping SignalR connection');
            return;
        }

        try {
            // Create new service instance with token
            signalRServiceRef.current = new DashboardSignalRService(token);

            // Set up event handlers
            signalRServiceRef.current.setUpdateHandler(onDashboardUpdate);
            if (onError) {
                signalRServiceRef.current.setErrorHandler(onError);
            }

            // Start connection and subscribe
            await signalRServiceRef.current.start();
            await signalRServiceRef.current.subscribe(user.id);

            console.log('Dashboard SignalR connection established');
        } catch (error) {
            console.error('Failed to initialize dashboard SignalR connection:', error);
            onError?.('Failed to establish real-time connection');
        }
    }, [user?.id, token, onDashboardUpdate, onError]);

    const refreshDashboard = useCallback(async () => {
        if (signalRServiceRef.current) {
            await signalRServiceRef.current.refreshDashboard();
        }
    }, []);

    const cleanup = useCallback(async () => {
        if (signalRServiceRef.current) {
            await signalRServiceRef.current.stop();
            signalRServiceRef.current = null;
        }
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
        isConnected: signalRServiceRef.current !== null
    };
};