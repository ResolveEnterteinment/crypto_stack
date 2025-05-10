// Interfaces for the component
export interface PaymentData {
    id: string;
    userId: string;
    subscriptionId: string;
    currency: string;
    totalAmount: number;
    status: string;
    createdAt: string;
    failureReason?: string;
    attemptCount?: number;
    nextRetryAt?: string;
}