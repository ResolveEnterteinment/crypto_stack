// Interfaces for the component
export interface Subscription {
    id: string;
    createdAt: string;
    userId: string;
    allocations: Allocation[];
    interval: string;
    amount: number;
    currency: string;
    lastPayment: string;
    nextDueDate: string;
    endDate: string;
    totalInvestments: number;
    status: string;
    isCancelled: boolean;
}

export interface Allocation {
    assetId: string;
    assetName: string;
    assetTicker: string;
    percentAmount: number;
}

export interface SubscriptionCardProps {
    subscription: Subscription;
    onEdit: (id: string) => void;
    onCancel: (id: string) => void;
    onViewHistory: (id: string) => void;
}

export const SubscriptionStatus: Record<string, string> = {
    ACTIVE: 'ACTIVE',
    CANCELLED: 'CANCELLED',
    PENDING: 'PENDING',
    SUSPENDED: 'SUSPENDED'
}