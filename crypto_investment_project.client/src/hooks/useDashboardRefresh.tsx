import { useState, useCallback } from 'react';

interface RefreshNotification {
    show: boolean;
    message: string;
    type: 'success' | 'error' | 'info';
}

interface UseDashboardRefreshReturn {
    isRefreshing: boolean;
    notification: RefreshNotification;
    refreshData: (refreshFunctions: (() => Promise<void>)[]) => Promise<void>;
    showNotification: (message: string, type?: 'success' | 'error' | 'info') => void;
    hideNotification: () => void;
    handleDataUpdated: (successMessage?: string) => (refreshFunctions: (() => Promise<void>)[]) => Promise<void>;
}

/**
 * Custom hook for managing dashboard data refresh and notifications
 */
export const useDashboardRefresh = (): UseDashboardRefreshReturn => {
    const [isRefreshing, setIsRefreshing] = useState(false);
    const [notification, setNotification] = useState<RefreshNotification>({
        show: false,
        message: '',
        type: 'success'
    });

    /**
     * Refresh multiple data sources in parallel
     */
    const refreshData = useCallback(async (refreshFunctions: (() => Promise<void>)[]): Promise<void> => {
        try {
            setIsRefreshing(true);

            // Execute all refresh functions in parallel
            await Promise.all(refreshFunctions.map(fn => fn()));

            console.log('Data refresh completed successfully');
        } catch (error) {
            console.error('Error during data refresh:', error);
            showNotification('Failed to refresh data. Please try again.', 'error');
            throw error; // Re-throw to allow caller to handle
        } finally {
            setIsRefreshing(false);
        }
    }, []);

    /**
     * Show a notification to the user
     */
    const showNotification = useCallback((message: string, type: 'success' | 'error' | 'info' = 'success') => {
        setNotification({
            show: true,
            message,
            type
        });
    }, []);

    /**
     * Hide the current notification
     */
    const hideNotification = useCallback(() => {
        setNotification(prev => ({
            ...prev,
            show: false
        }));
    }, []);

    /**
     * Handle data updated callback - returns a function that can be passed to child components
     */
    const handleDataUpdated = useCallback((successMessage: string = 'Data updated successfully') => {
        return async (refreshFunctions: (() => Promise<void>)[]): Promise<void> => {
            try {
                await refreshData(refreshFunctions);
                showNotification(successMessage, 'success');
            } catch (error) {
                // Error is already handled in refreshData
                console.error('Failed to handle data update:', error);
            }
        };
    }, [refreshData, showNotification]);

    return {
        isRefreshing,
        notification,
        refreshData,
        showNotification,
        hideNotification,
        handleDataUpdated
    };
};

// Usage example:
/*
// In DashboardPage.tsx
const DashboardPage: React.FC = () => {
    const { user } = useAuth();
    const [subscriptions, setSubscriptions] = useState<Subscription[]>([]);
    const [dashboardData, setDashboardData] = useState<Dashboard | null>(null);
    
    const {
        isRefreshing,
        notification,
        refreshData,
        showNotification,
        hideNotification,
        handleDataUpdated
    } = useDashboardRefresh();

    // Define refresh functions
    const refreshFunctions = [
        async () => {
            const data = await getDashboardData(user!.id);
            setDashboardData(data);
        },
        async () => {
            const subs = await getSubscriptions(user!.id);
            setSubscriptions(subs);
        }
    ];

    // Create the callback for child components
    const onDataUpdated = handleDataUpdated('Dashboard updated with latest payment information');

    const handleCancelSubscription = async (id: string) => {
        try {
            await updateSubscription(id, { isCancelled: true });
            await onDataUpdated(refreshFunctions);
        } catch (err) {
            showNotification('Failed to cancel subscription', 'error');
        }
    };

    return (
        <div>
            // Pass onDataUpdated to subscription cards
            {subscriptions.map(sub => (
                <SubscriptionCard
                    key={sub.id}
                    subscription={sub}
                    onDataUpdated={() => onDataUpdated(refreshFunctions)}
                />
            ))}
            
            // Show notifications
            <Notification
                show={notification.show}
                message={notification.message}
                type={notification.type}
                onClose={hideNotification}
            />
        </div>
    );
};
*/