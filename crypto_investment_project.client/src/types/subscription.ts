import { ReactNode } from 'react';

export const SubscriptionStatus: Record<string, string> = {
    ACTIVE: 'ACTIVE',
    CANCELLED: 'CANCELLED',
    PENDING: 'PENDING',
    SUSPENDED: 'SUSPENDED'
}

export type SubscriptionStatusType = typeof SubscriptionStatus[keyof typeof SubscriptionStatus];

export const SubscriptionState = {
    IDLE: 'IDLE',
    PENDING_CHECKOUT: 'PENDING_CHECKOUT',
    PENDING_PAYMENT: 'PENDING_PAYMENT',
    PROCESSING_INVOICE: 'PROCESSING_INVOICE',
    ACQUIRING_ASSETS: 'ACQUIRING_ASSETS'
} as const;

export type SubscriptionStateType = typeof SubscriptionState[keyof typeof SubscriptionState];

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
    status: SubscriptionStatusType;  // ACTIVE, PENDING, SUSPENDED, CANCELLED
    state: SubscriptionStateType; //IDLE, PENDING_CHECKOUT, PENDING_PAYMENT, PROCESSING_INVOICE, ACQUIRING_ASSETS
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

export interface StateConfig {
    color: string;
    borderColor: string;
    gradientStart: string;
    gradientEnd: string;
    text: string;
    badgeStatus: string;
    showPulse: boolean;
    showProgress: boolean;
    icon: ReactNode;
    description: string | null;
    progressPercent: number;
}