// src/services/subscription.ts - Enhanced version with Stripe update integration
import api from "./api";
import ICreateSubscriptionRequest from "../interfaces/ICreateSubscriptionRequest";
import IUpdateSubscriptionRequest from "../interfaces/IUpdateSubscriptionRequest";
import { logApiError } from "../utils/apiErrorHandler";
import { Subscription } from "../types/subscription";
import Transaction from "../types/transaction";

/**
 * Fetches a subscription by Id for a user
 * @param subscriptionId The ID of the subcription
 * @returns Promise with array of subscription objects
 */
export const getSubscription = async (subscriptionId: string): Promise<Subscription> => {
    if (!subscriptionId) {
        console.error("getSubscriptions called with undefined subscriptionId");
        return Promise.reject(new Error("Subscription ID is required"));
    }

    try {
        const subscriptionResult = await api.safeRequest <Subscription>('get', `/Subscription/${subscriptionId}`);

        const subscription: Subscription = {
            ...subscriptionResult.data,
            amount: typeof subscriptionResult.data.amount === 'number' ? subscriptionResult.data.amount : parseFloat(subscriptionResult.data.amount),
            totalInvestments: typeof subscriptionResult.data.totalInvestments === 'number'
                ? subscriptionResult.data.totalInvestments
                : parseFloat(subscriptionResult.data.totalInvestments || '0'),
            isCancelled: Boolean(subscriptionResult.data.isCancelled)
        }

        return subscription;
    } catch (error) {
        logApiError(error, "Fetch Subscription Error");
        throw error;
    }
};

/**
 * Fetches all subscriptions for a user
 * @param userId The ID of the user
 * @returns Promise with array of subscription objects
 */
export const getSubscriptions = async (userId: string): Promise<Subscription[]> => {
    if (!userId) {
        console.error("getSubscriptions called with undefined userId");
        return Promise.reject(new Error("User ID is required"));
    }

    try {
        const { data } = await api.get(`/Subscription/user/${userId}`);

        const subscriptionsData = data.data ?? [];

        var subscriptions = subscriptionsData.map((subscription: any) => ({
            ...subscription,
            nextDueDate: subscription.nextDueDate ? new Date(subscription.nextDueDate) : null,
            endDate: subscription.endDate ? new Date(subscription.endDate) : null,
            amount: typeof subscription.amount === 'number' ? subscription.amount : parseFloat(subscription.amount),
            totalInvestments: typeof subscription.totalInvestments === 'number'
                ? subscription.totalInvestments
                : parseFloat(subscription.totalInvestments || '0'),
            isCancelled: Boolean(subscription.isCancelled)
        }));

        return subscriptions;
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

        const headers: Record<string, string> = {
            'X-Idempotency-Key': idempotencyKey
        };

        const requestPayload: ICreateSubscriptionRequest = {
            userId: subscriptionData.userId,
            allocations: subscriptionData.allocations.map(allocation => ({
                assetId: allocation.assetId,
                percentAmount: Math.round(allocation.percentAmount)
            })),
            interval: subscriptionData.interval.toUpperCase(),
            amount: subscriptionData.amount,
            currency: subscriptionData.currency,
            endDate: subscriptionData.endDate ? subscriptionData.endDate : null
        };

        console.log("Creating subscription with payload:", JSON.stringify(requestPayload, null, 2));

        const response = await api.post('/Subscription/new', requestPayload, { headers });

        if (!response.data) {
            throw new Error("Server returned empty response");
        }

        let subscriptionId;
        if (response.data.id) {
            subscriptionId = response.data.id;
        } else if (response.data.data && response.data.data.id) {
            subscriptionId = response.data.data.id;
        } else if (response.data.data && typeof response.data.data === 'string') {
            subscriptionId = response.data.data;
        } else if (typeof response.data === 'string') {
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
 * Updates a subscription (backend automatically handles Stripe updates)
 * @param subscriptionId The ID of the subscription to update
 * @param updateFields An object containing the fields to update (amount, endDate, allocations)
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

        const headers: Record<string, string> = {
            'X-Idempotency-Key': idempotencyKey
        };

        // Format update fields properly
        const requestPayload = { ...updateFields };

        if (updateFields.allocations) {
            // Validate allocation percentages sum to 100
            const totalPercentage = updateFields.allocations.reduce(
                (sum, allocation) => sum + allocation.percentAmount,
                0
            );

            if (Math.abs(totalPercentage - 100) > 0.01) {
                throw new Error(`Allocation percentages must sum to 100%. Current total: ${totalPercentage.toFixed(2)}%`);
            }

            requestPayload.allocations = updateFields.allocations;
        }

        if (updateFields.endDate) {
            requestPayload.endDate = updateFields.endDate;
        }

        // Update the subscription - backend will automatically handle Stripe updates for amount/endDate changes
        console.log("Updating subscription with payload:", JSON.stringify(requestPayload, null, 2));
        const response = await api.put(`/Subscription/update/${subscriptionId}`, requestPayload, { headers });

        console.log("Subscription updated successfully (Stripe sync handled automatically for amount/endDate changes)");
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

        const headers: Record<string, string> = {
            'X-Idempotency-Key': idempotencyKey
        };

        const response = await api.post(`/Subscription/cancel/${subscriptionId}`, null, { headers });

        return response.data;
    } catch (error) {
        logApiError(error, "Cancel Subscription Error");
        throw error;
    }
};

/**
 * Pauses a subscription
 * @param subscriptionId The ID of the subscription to pause
 * @returns Promise
 */
export const pauseSubscription = async (subscriptionId: string): Promise<void> => {
    if (!subscriptionId) {
        console.error("pauseSubscription called with undefined subscriptionId");
        return Promise.reject(new Error("Subscription ID is required"));
    }

    try {
        // Generate a unique idempotency key
        const idempotencyKey = `pause-subscription-${subscriptionId}-${Date.now()}-${Math.random().toString(36).substring(2, 15)}`;

        const headers: Record<string, string> = {
            'X-Idempotency-Key': idempotencyKey
        };

        const response = await api.post(`/Subscription/pause/${subscriptionId}`, null, { headers });

        return response.data;
    } catch (error) {
        logApiError(error, "Pause Subscription Error");
        throw error;
    }
};

/**
 * Resumes a paused subscription
 * @param subscriptionId The ID of the subscription to resume
 * @returns Promise
 */
export const resumeSubscription = async (subscriptionId: string): Promise<void> => {
    if (!subscriptionId) {
        console.error("resumeSubscription called with undefined subscriptionId");
        return Promise.reject(new Error("Subscription ID is required"));
    }

    try {
        // Generate a unique idempotency key
        const idempotencyKey = `resume-subscription-${subscriptionId}-${Date.now()}-${Math.random().toString(36).substring(2, 15)}`;

        const headers: Record<string, string> = {
            'X-Idempotency-Key': idempotencyKey
        };

        const response = await api.post(`/Subscription/resume/${subscriptionId}`, null, { headers });

        return response.data;
    } catch (error) {
        logApiError(error, "Resume Subscription Error");
        throw error;
    }
};

/**
 * Gets transaction history for a subscription
 * @param subscriptionId The ID of the subscription
 * @returns Promise with transaction history
 */
export const getTransactions = async (subscriptionId: string): Promise<Transaction[]> => {
    if (!subscriptionId) {
        console.error("getSubscriptionTransactions called with undefined subscriptionId");
        return Promise.reject(new Error("Subscription ID is required"));
    }

    try {
        const response = await api.get(`/Transaction/subscription/${subscriptionId}`);
        return Array.isArray(response.data) ? response.data : [];
    } catch (error) {
        console.error(`Error fetching transactions for subscription ${subscriptionId}:`, error);
        return [];
    }
};