// Interfaces for the component
export interface Subscription {
    id: string;
    createdAt: string;
    userId: string;
    allocations: Allocation[];
    interval: string;
    amount: number;
    currency: string;
    nextDueDate: Date | string;
    endDate: Date | string;
    totalInvestments: number;
    status: string;
    isCancelled: boolean;
}

export interface Allocation {
    assetId: string;
    assetName: string;
    ticker: string;
    percentAmount: number;
}

export interface SubscriptionCardProps {
    subscription: Subscription;
    onEdit: (id: string) => void;
    onCancel: (id: string) => void;
    onViewHistory: (id: string) => void;
}