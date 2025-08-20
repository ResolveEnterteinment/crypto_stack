import { AssetHolding, Dashboard } from "../types/dashboardTypes";
import api from "./api";

// API endpoints
const ENDPOINTS = {
    GET_DASHBOARD: () => `/dashboard`,
    HEALTH_CHECK: '/dashboard/health'
} as const;

/**
 * Fetches dashboard data for a user from the dedicated endpoint
 * @param userId The ID of the user
 * @returns Promise with dashboard data
 */
export const getDashboardData = async (userId: string): Promise<Dashboard> => {
    if (!userId) {
        console.error("getDashboardData called with undefined userId");
        return Promise.reject(new Error("User ID is required"));
    }

    try {
        // Use the dedicated dashboard endpoint
        const response = await api.get<Dashboard>(ENDPOINTS.GET_DASHBOARD());

        if (response == null || response.data == null || !response.success) {
            throw new Error(response.message || 'Failed to get dashboard data');
        }

        return response.data;
    } catch (error) {
        console.error("Error fetching dashboard data:", error);
        throw error;
    }
};