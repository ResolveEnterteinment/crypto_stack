// src/services/payment.ts
import api from "./api";


export interface Payment {
    id: string;
    userId: string;
    subscriptionId: string;
    status: string;
    totalAmount: number;
    netAmount: number;
    currency: string;
    createdAt: Date;
    attemptCount?: number;
    lastAttemptAt?: Date;
    nextRetryAt?: Date;
    failureReason?: string;
}

// Payment status enum (should match backend)
export enum PaymentStatus {
    Pending = 'PENDING',
    Completed = 'COMPLETED',
    Failed = 'FAILED',
    Cancelled = 'CANCELLED',
    Paid = 'PAID',
    Filled = 'FILLED'
}

// Data for payment request
export interface PaymentRequestData {
    subscriptionId: string;
    userId: string;
    amount: number;
    currency?: string;
    isRecurring: boolean;
    interval: string;
    returnUrl?: string;
    cancelUrl?: string;
    idempotencyKey?: string;
}

// Payment status response
export interface PaymentStatusResponse {
    status: string;
    totalAmount: number;
    currency: string;
    subscriptionId: string;
    createdAt: string;
}

// Payment cancellation response
export interface PaymentCancelResponse {
    success: boolean;
    message: string;
}

export interface SessionResponse {
    checkoutUrl: string;
    clientSecret: string;
    sessionId: string;
}

export interface SyncPaymentsResponse {
    processedCount: number;
    totalCount: number;
}

// API endpoints
const ENDPOINTS = {
    CREATE_CHECKOUT_SESSION: () => `/payment/create-checkout-session`,
    CANCEL: (paymentId: string) => `/payment/cancel/${paymentId}`,
    RETRY: (paymentId: string) => `/payment/retry/${paymentId}`,
    GET_BY_SUBSCRIPTION: (subscriptionId: string) => `/payment/subscription/${subscriptionId}`,
    GET_PAYMENT_STATUS_BY_SUBSCRIPTION: (subscriptionId: string) => `/payment/status/subscription/${subscriptionId}`,
    FETCH_UPDATE_PAYMENTS_BY_SUBSCRIPTION: (subscriptionId: string) => `/payment/fetch-update/subscription/${subscriptionId}`,
    UPDATE_PAYMENT_METHOD: (subscriptionId: string) => `/payment-methods/update/${subscriptionId}`,
    HEALTH_CHECK: '/payment/health'
} as const;

/**
 * Initiates a payment session with Stripe
 * @param paymentData Payment request data
 * @returns Promise with checkout URL to redirect the user to
 */
export const initiatePayment = async (paymentData: PaymentRequestData): Promise<SessionResponse> => {
    if (!paymentData.subscriptionId || !paymentData.userId) {
        return Promise.reject(new Error('Subscription ID and User ID are required'));
    }

    if (!paymentData.amount || paymentData.amount <= 0) {
        return Promise.reject(new Error('A valid amount is required'));
    }

    try {
        // Generate a unique idempotency key if not provided
        const idempotencyKey = paymentData.idempotencyKey ||
            `payment-${paymentData.subscriptionId}-${Date.now()}-${Math.random().toString(36).substring(2, 15)}`;

        const headers: Record<string, string> = {
            'X-Idempotency-Key': idempotencyKey
        };

        // Build the return URLs with query parameters
        const baseUrl = window.location.origin;
        const returnUrl = paymentData.returnUrl ||
            `${baseUrl}/payment/checkout/success?subscription_id=${paymentData.subscriptionId}&amount=${paymentData.amount}&currency=${paymentData.currency || 'USD'}`;
        const cancelUrl = paymentData.cancelUrl ? paymentData.cancelUrl + `&payment_data=${encodeURIComponent(JSON.stringify(paymentData))}` :
            `${baseUrl}/payment/checkout/cancel?subscription_id=${paymentData.subscriptionId}`;

        const response = await api.post <SessionResponse>(ENDPOINTS.CREATE_CHECKOUT_SESSION(), {
            subscriptionId: paymentData.subscriptionId,
            userId: paymentData.userId,
            amount: paymentData.amount,
            currency: paymentData.currency || 'USD',
            isRecurring: paymentData.isRecurring,
            interval: paymentData.interval,
            returnUrl,
            cancelUrl,
            idempotencyKey
        }, { headers });

        if (response == null || response.data == null || !response.success) {
            throw new Error(response.message || `Failed to create checkout session`);
        }

        return response.data;
    } catch (error: any) {
        console.error('Error initiating payment:', error);
        throw new Error(error.response?.data?.message || error.message || 'Payment initiation failed');
    }
};

/**
 * Retrieves the status of a payment for a subscription
 * @param subscriptionId The ID of the subscription
 * @returns Promise with payment status
 */
export const getSubscriptionPaymentStatus = async (subscriptionId: string): Promise<PaymentStatusResponse> => {
    if (!subscriptionId) {
        throw new Error("Subscription Id is required")
    }

    try {
        const response = await api.get<PaymentStatusResponse>(ENDPOINTS.GET_PAYMENT_STATUS_BY_SUBSCRIPTION(subscriptionId));

        if (response == null || response.data == null || !response.success) {
            throw new Error(response.message || `Failed to get payment status`);
        }

        return response.data;
    } catch (error: any) {
        console.error(`Error fetching payment status for subscription ${subscriptionId}:`, error);
        throw error;
    }
};

/**
 * Retrieves payments for a subscription
 * @param subscriptionId The ID of the subscription
 * @returns Promise with payments
 */
export const getSubscriptionPayments = async (subscriptionId: string): Promise<Payment[]> => {
    if (!subscriptionId) {
        throw new Error('Subscription ID is required');
    }

    try {
        const response = await api.get<Payment[]>(ENDPOINTS.GET_BY_SUBSCRIPTION(subscriptionId));

        if (response == null || response.data == null || !response.success) {
            throw new Error(response.message || `Failed to get payments`);
        }

        return response.data;
    } catch (error: any) {

        console.error(`Error fetching payments for subscription ${subscriptionId}:`, error);
        throw error;
    }
};

/**
 * Retrieves the status of a payment for a subscription
 * @param subscriptionId The ID of the subscription
 * @returns Promise with payment status
 */
export const syncPayments = async (subscriptionId: string): Promise<SyncPaymentsResponse> => {
    if (!subscriptionId) {
        return Promise.reject(new Error('Subscription ID is required'));
    }

    try {
        const response = await api.get<SyncPaymentsResponse>(ENDPOINTS.FETCH_UPDATE_PAYMENTS_BY_SUBSCRIPTION(subscriptionId));

        if (response == null || response.data == null || !response.success) {
            throw new Error(response.message || `Failed to sync payments`);
        }

        return response.data;
    } catch (error: any) {
        console.error(`Error syncronizing payments for subscription ${subscriptionId}:`, error);
        throw error;
    }
};

/**
 * Cancels a pending payment
 * @param paymentId The ID of the payment
 * @returns Promise with cancellation result
 */
export const retryPayment = async (paymentId: string): Promise<PaymentCancelResponse> => {
    if (!paymentId) {
        return Promise.reject(new Error('Payment ID is required'));
    }

    try {

        const response = await api.post<PaymentCancelResponse>(ENDPOINTS.RETRY(paymentId));

        if (response == null || response.data == null || !response.success) {
            throw new Error(response.message || `Failed to cancel payment`);
        }

        return response.data;
    } catch (error) {
        console.error(`Error cancelling payment ${paymentId}:`, error);
        throw error;
    }
};

/**
 * Cancels a pending payment
 * @param paymentId The ID of the payment
 * @returns Promise with cancellation result
 */
export const cancelPayment = async (paymentId: string): Promise<PaymentCancelResponse> => {
    if (!paymentId) {
        return Promise.reject(new Error('Payment ID is required'));
    }

    try {

        const response = await api.post<PaymentCancelResponse>(ENDPOINTS.CANCEL(paymentId));

        if (response == null || response.data == null || !response.success) {
            throw new Error(response.message || `Failed to cancel payment`);
        }

        return response.data;
    } catch (error) {
        console.error(`Error cancelling payment ${paymentId}:`, error);
        throw error;
    }
};

/**
* Updates payment method of a subscription
* @param subscripitonId The ID of the susbcripition
* @returns Promise with cancellation result
*/
export const updatePaymentMethod = async (subscripitonId: string): Promise<SessionResponse> => {
    if (!subscripitonId) {
        return Promise.reject(new Error('Subscription ID is required'));
    }

    try {

        const response = await api.post<SessionResponse>(ENDPOINTS.UPDATE_PAYMENT_METHOD(subscripitonId));

        if (response == null || response.data == null || !response.success) {
            throw new Error(response.message || `Failed to update payment method`);
        }

        return response.data;
    } catch (error) {
        console.error(`Error updating payment method for subscription ${subscripitonId}:`, error);
        throw error;
    }
};