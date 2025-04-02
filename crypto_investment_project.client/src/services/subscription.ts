// src/services/subscription.ts
import api from "./api";
import ISubscription from "../interfaces/ISubscription";
import ITransaction from "../interfaces/ITransaction";
import ICreateSubscriptionRequest from "../interfaces/ICreateSubscriptionRequest";
import IUpdateSubscriptionRequest from "../interfaces/IUpdateSubscriptionRequest";

/**
 * Fetches all subscriptions for a user
 * @param userId The ID of the user
 * @returns Promise with array of subscription objects
 */
export const getSubscriptions = async (userId: string): Promise<ISubscription[]> => {
    if (!userId) {
        console.error("getSubscriptions called with undefined userId");
        return Promise.reject(new Error("User ID is required"));
    }

    try {
        const { data } = await api.post(`/Subscription/user/${userId}`);

        // Process dates and ensure proper typing
        const processedData = Array.isArray(data) ? data.map((subscription: any) => ({
            ...subscription,
            nextDueDate: new Date(subscription.nextDueDate),
            endDate: new Date(subscription.endDate),
            // Ensure amount is a number
            amount: typeof subscription.amount === 'number' ? subscription.amount : parseFloat(subscription.amount),
            // Ensure totalInvestments is a number
            totalInvestments: typeof subscription.totalInvestments === 'number'
                ? subscription.totalInvestments
                : parseFloat(subscription.totalInvestments),
            // Ensure isCancelled is a boolean
            isCancelled: Boolean(subscription.isCancelled)
        })) : [];

        return processedData;
    } catch (error) {
        console.error("Error fetching subscriptions:", error);
        throw error;
    }
};

/**
 * Gets a single subscription by ID
 * @param subscriptionId The ID of the subscription
 * @returns Promise with the subscription object
 */
export const getSubscription = async (subscriptionId: string): Promise<ISubscription> => {
    if (!subscriptionId) {
        console.error("getSubscription called with undefined subscriptionId");
        return Promise.reject(new Error("Subscription ID is required"));
    }

    try {
        const response = await api.get(`/Subscription/${subscriptionId}`);

        // Process dates and ensure proper typing
        const subscription = response.data;
        return {
            ...subscription,
            nextDueDate: new Date(subscription.nextDueDate),
            endDate: new Date(subscription.endDate),
            amount: typeof subscription.amount === 'number' ? subscription.amount : parseFloat(subscription.amount),
            totalInvestments: typeof subscription.totalInvestments === 'number'
                ? subscription.totalInvestments
                : parseFloat(subscription.totalInvestments),
            isCancelled: Boolean(subscription.isCancelled)
        };
    } catch (error) {
        console.error(`Error fetching subscription ${subscriptionId}:`, error);
        throw error;
    }
};

/*
* Fetches all transaction of a subscription
* @param subscriptionId The ID of the subscription
* @returns Promise with array of transaction objects
*/
export const getTransactions = async (subscriptionId: string): Promise<ITransaction[]> => {
    if (!subscriptionId) {
        console.error("getTransactions called with undefined subscriptionId");
        return Promise.reject(new Error("Subscription ID is required"));
    }

    try {
        const { data } = await api.post(`/Transaction/subscription/${subscriptionId}`);

        // Process dates and ensure proper typing
        const processedData = Array.isArray(data) ? data.map((transaction: ITransaction) => ({
            ...transaction,
            createdAt: new Date(transaction.createdAt),
            // Ensure quantity is a number
            quantity: typeof transaction.quantity === 'number'
                ? transaction.quantity
                : parseFloat(transaction.quantity),
            // Ensure quantity is a number
            quoteQuantity: typeof transaction.quoteQuantity === 'number'
                ? transaction.quoteQuantity
                : parseFloat(transaction.quoteQuantity)
        })) : [];

        return processedData;
    } catch (error) {
        console.error("Error fetching transactions:", error);
        throw error;
    }
};

/**
 * Updates a subscription
 * @param subscriptionId The ID of the subscription to update
 * @param updateFields An object containing the fields to update
 * @returns Promise
 */
export const updateSubscription = async (
    subscriptionId: string,
    updateFields: IUpdateSubscriptionRequest
): Promise<void> => {
    if (!subscriptionId) {
        console.error("updateSubscription called with undefined subscriptionId");
        return Promise.reject(new Error("Subscription ID is required"));
    }

    try {
        // Generate a unique idempotency key
        const idempotencyKey = `update-subscription-${subscriptionId}-${Date.now()}-${Math.random().toString(36).substring(2, 15)}`;

        // Get CSRF token if available
        const csrfToken = document.querySelector('meta[name="csrf-token"]')?.getAttribute('content');

        const headers: Record<string, string> = {
            'X-Idempotency-Key': idempotencyKey
        };

        if (csrfToken) {
            headers['X-CSRF-TOKEN'] = csrfToken;
        }

        const response = await api.put(`/Subscription/update/${subscriptionId}`, updateFields, { headers });

        return response.data;
    } catch (error) {
        console.error(`Error updating subscription ${subscriptionId}:`, error);
        throw error;
    }
};

/**
 * Creates a new subscription
 * @param subscriptionData The subscription data
 * @returns Promise with the created subscription ID
 */
export const createSubscription = async (subscriptionData: ICreateSubscriptionRequest): Promise<string> => {
    try {
        // Validate input
        if (!subscriptionData.userId) {
            throw new Error("User ID is required");
        }

        if (!subscriptionData.allocations || subscriptionData.allocations.length === 0) {
            throw new Error("At least one allocation is required");
        }

        if (!subscriptionData.interval) {
            throw new Error("Interval is required");
        }

        if (!subscriptionData.amount || subscriptionData.amount <= 0) {
            throw new Error("Amount must be greater than 0");
        }

        if (!subscriptionData.currency) {
            throw new Error("Currency is required");
        }

        // Validate allocation percentages sum to 100
        const totalPercentage = subscriptionData.allocations.reduce(
            (sum, allocation) => sum + allocation.percentAmount,
            0
        );

        if (Math.abs(totalPercentage - 100) > 0.01) {
            throw new Error("Allocation percentages must sum to 100%");
        }

        // Generate a unique idempotency key
        const idempotencyKey = `create-subscription-${subscriptionData.userId}-${Date.now()}-${Math.random().toString(36).substring(2, 15)}`;

        // Get CSRF token if available
        const csrfToken = document.querySelector('meta[name="csrf-token"]')?.getAttribute('content');

        const headers: Record<string, string> = {
            'X-Idempotency-Key': idempotencyKey
        };

        if (csrfToken) {
            headers['X-CSRF-TOKEN'] = csrfToken;
        }

        const response = await api.post('/Subscription/new', subscriptionData, { headers });

        return response.data.id;
    } catch (error) {
        console.error('Error creating subscription:', error);
        throw error;
    }
};

/**
 * Cancels a subscription
 * @param subscriptionId The ID of the subscription to cancel
 * @returns Promise
 */
export const cancelSubscription = async (subscriptionId: string): Promise<void> => {
    if (!subscriptionId) {
        console.error("cancelSubscription called with undefined subscriptionId");
        return Promise.reject(new Error("Subscription ID is required"));
    }

    try {
        // Generate a unique idempotency key
        const idempotencyKey = `cancel-subscription-${subscriptionId}-${Date.now()}-${Math.random().toString(36).substring(2, 15)}`;

        // Get CSRF token if available
        const csrfToken = document.querySelector('meta[name="csrf-token"]')?.getAttribute('content');

        const headers: Record<string, string> = {
            'X-Idempotency-Key': idempotencyKey
        };

        if (csrfToken) {
            headers['X-CSRF-TOKEN'] = csrfToken;
        }

        const response = await api.post(`/Subscription/cancel/${subscriptionId}`, null, { headers });

        return response.data;
    } catch (error) {
        console.error(`Error cancelling subscription ${subscriptionId}:`, error);
        throw error;
    }
};

/**
 * Gets transaction history for a subscription
 * @param subscriptionId The ID of the subscription
 * @returns Promise with transaction history
 */
export const getSubscriptionTransactions = async (subscriptionId: string): Promise<any[]> => {
    if (!subscriptionId) {
        console.error("getSubscriptionTransactions called with undefined subscriptionId");
        return Promise.reject(new Error("Subscription ID is required"));
    }

    try {
        const response = await api.post(`/Transaction/subscription/${subscriptionId}`);
        return Array.isArray(response.data) ? response.data : [];
    } catch (error) {
        console.error(`Error fetching transactions for subscription ${subscriptionId}:`, error);
        return []; // Return empty array instead of throwing to gracefully handle this optional feature
    }
};