// src/services/subscription.ts - Fixed version
import api from "./api";
import ISubscription from "../interfaces/ISubscription";
import ITransaction from "../interfaces/ITransaction";
import ICreateSubscriptionRequest from "../interfaces/ICreateSubscriptionRequest";
import IUpdateSubscriptionRequest from "../interfaces/IUpdateSubscriptionRequest";
import { logApiError } from "../utils/apiErrorHandler";

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
        const { data } = await api.get(`/Subscription/user/${userId}`);

        // Process dates and ensure proper typing
        const processedData = Array.isArray(data) ? data.map((subscription: any) => ({
            ...subscription,
            nextDueDate: subscription.nextDueDate ? new Date(subscription.nextDueDate) : null,
            endDate: subscription.endDate ? new Date(subscription.endDate) : null,
            // Ensure amount is a number
            amount: typeof subscription.amount === 'number' ? subscription.amount : parseFloat(subscription.amount),
            // Ensure totalInvestments is a number
            totalInvestments: typeof subscription.totalInvestments === 'number'
                ? subscription.totalInvestments
                : parseFloat(subscription.totalInvestments || '0'),
            // Ensure isCancelled is a boolean
            isCancelled: Boolean(subscription.isCancelled)
        })) : [];

        return processedData;
    } catch (error) {
        logApiError(error, "Fetch Subscriptions Error");
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

        // Fix: Format request payload to match the server's expected format
        const requestPayload = {
            userId: subscriptionData.userId,
            // Fix: Ensure allocations follow the expected format
            allocations: subscriptionData.allocations.map(allocation => ({
                assetId: allocation.assetId,
                percentAmount: Math.round(allocation.percentAmount) // Convert to integer if needed
            })),
            interval: subscriptionData.interval.toUpperCase(), // Ensure uppercase for constants
            amount: subscriptionData.amount, // Send as decimal
            currency: subscriptionData.currency,
            endDate: subscriptionData.endDate ? subscriptionData.endDate.toISOString() : null
        };

        // Log the request for debugging
        console.log("Creating subscription with payload:", JSON.stringify(requestPayload, null, 2));

        const response = await api.post('/Subscription/new', requestPayload, { headers });

        if (!response.data) {
            throw new Error("Server returned empty response");
        }

        // Handle different response formats
        let subscriptionId;
        if (response.data.id) {
            subscriptionId = response.data.id;
        } else if (response.data.data && response.data.data.id) {
            subscriptionId = response.data.data.id;
        } else if (typeof response.data === 'string') {
            // Some APIs return the ID directly as a string
            subscriptionId = response.data;
        } else {
            console.error("Unexpected response format:", response.data);
            throw new Error("Failed to extract subscription ID from response");
        }

        return subscriptionId;
    } catch (error) {
        logApiError(error, "Create Subscription Error");
        throw error;
    }
};

/**
 * Updates a subscription
 * @param subscriptionId The ID of the subscription to update
 * @param updateFields An object containing the fields to update
 * @returns Promise
 */
export const updateSubscription = async (subscriptionId: string, updateFields: IUpdateSubscriptionRequest): Promise<void> => {
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

        // Fix: Format update fields properly
        const requestPayload = { ...updateFields };

        if (updateFields.allocations) {
            requestPayload.allocations = updateFields.allocations.map(allocation => ({
                assetId: allocation.assetId,
                percentAmount: Math.round(allocation.percentAmount)
            }));
        }

        if (updateFields.interval) {
            requestPayload.interval = updateFields.interval.toUpperCase();
        }

        if (updateFields.endDate) {
            requestPayload.endDate = updateFields.endDate.toISOString();
        }

        const response = await api.put(`/Subscription/update/${subscriptionId}`, requestPayload, { headers });

        return response.data;
    } catch (error) {
        logApiError(error, "Update Subscription Error");
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
        logApiError(error, "Cancel Subscription Error");
        throw error;
    }
};

/**
 * Gets transaction history for a subscription
 * @param subscriptionId The ID of the subscription
 * @returns Promise with transaction history
 */
export const getTransactions = async (subscriptionId: string): Promise<ITransaction[]> => {
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