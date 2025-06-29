// src/services/payment.ts
import api from "./api";

// Payment status enum (should match backend)
export enum PaymentStatus {
    Pending = 'Pending',
    Completed = 'Completed',
    Failed = 'Failed',
    Cancelled = 'Cancelled'
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

// Response from payment creation endpoint
export interface StripePaymentResponse {
    success: boolean;
    message: string;
    checkoutUrl: string;
    clientSecret?: string;
}

// Payment status response
export interface PaymentStatusResponse {
    id: string;
    status: PaymentStatus;
    amount: number;
    currency: string;
    subscriptionId: string;
    createdAt: string;
    updatedAt: string;
}

// Payment cancellation response
export interface PaymentCancelResponse {
    success: boolean;
    message: string;
}

/**
 * Initiates a payment session with Stripe
 * @param paymentData Payment request data
 * @returns Promise with checkout URL to redirect the user to
 */
export const initiatePayment = async (paymentData: PaymentRequestData): Promise<string> => {
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

        const response = await api.post('/Payment/create-checkout-session', {
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

        const responseData: StripePaymentResponse = response.data;

        if (!responseData.success || !responseData.checkoutUrl) {
            throw new Error(responseData.message || 'Failed to create payment session');
        }

        return responseData.checkoutUrl;
    } catch (error: any) {
        console.error('Error initiating payment:', error);
        throw new Error(error.response?.data?.message || error.message || 'Payment initiation failed');
    }
};

/**
 * Retrieves the status of a payment
 * @param paymentId The ID of the payment
 * @returns Promise with payment status
 */
export const getPaymentStatus = async (paymentId: string): Promise<PaymentStatusResponse> => {
    if (!paymentId) {
        return Promise.reject(new Error('Payment ID is required'));
    }

    try {
        const { data } = await api.get(`/Payment/status/${paymentId}`);
        return data;
    } catch (error) {
        console.error(`Error fetching payment status for ${paymentId}:`, error);
        throw error;
    }
};

/**
 * Retrieves the status of a payment for a subscription
 * @param subscriptionId The ID of the subscription
 * @returns Promise with payment status
 */
export const getSubscriptionPaymentStatus = async (subscriptionId: string): Promise<PaymentStatusResponse | null> => {
    if (!subscriptionId) {
        return Promise.reject(new Error('Subscription ID is required'));
    }

    try {
        const { data } = await api.get(`/Payment/subscription/${subscriptionId}/status`);
        return data;
    } catch (error: any) {
        // If 404, the subscription might not have a payment yet
        if (error.response?.status === 404) {
            return null;
        }
        console.error(`Error fetching payment status for subscription ${subscriptionId}:`, error);
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
        const headers: Record<string, string> = {};
        /*
        // Get CSRF token if available
        const csrfToken = document.querySelector('meta[name="csrf-token"]')?.getAttribute('content');

        if (csrfToken) {
            headers['X-CSRF-TOKEN'] = csrfToken;
        }
        */
        const { data } = await api.post(`/Payment/cancel/${paymentId}`, null, { headers });
        return data;
    } catch (error) {
        console.error(`Error cancelling payment ${paymentId}:`, error);
        throw error;
    }
};