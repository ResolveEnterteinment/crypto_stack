// src/services/subscription.ts - Enhanced version with Stripe update integration
import Transaction from "../types/transaction";
import api from "./api";

// API endpoints
const ENDPOINTS = {
    GET_BY_CURRENT_USER: (subscriptionId: string) => `/transaction/user`,
    GET_BY_USER: (subscriptionId: string) => `/transaction/admin/user`,
    GET_BY_CURRENT_USER_SUBSCRIPTION: (subscriptionId: string) => `/transaction/subscription/${subscriptionId}`,
    GET_BY_SUBSCRIPTION: (subscriptionId: string) => `/transaction/admin/subscription/${subscriptionId}`,
    HEALTH_CHECK: '/health'
} as const;

/**
 * Gets transaction history for a subscription
 * @param subscriptionId The ID of the subscription
 * @returns Promise with transaction history
 */
export const getBySubscription = async (subscriptionId: string): Promise<Transaction[]> => {
    if (!subscriptionId) {
        console.error("getSubscriptionTransactions called with undefined subscriptionId");
        return Promise.reject(new Error("Subscription ID is required"));
    }

    try {
        const response = await api.get(ENDPOINTS.GET_BY_CURRENT_USER_SUBSCRIPTION(subscriptionId));
        return Array.isArray(response.data) ? response.data : [];
    } catch (error) {
        console.error(`Error fetching transactions for subscription ${subscriptionId}:`, error);
        return [];
    }
};