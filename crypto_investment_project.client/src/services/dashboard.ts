// src/services/dashboard.ts
import api from "./api";

export interface IBalance {
    assetId?: string;
    name?: string;
    ticker: string;
    symbol?: string;
    available: number;
    locked: number;
    total: number;
    value: number;
}

export interface IDashboardData {
    assetHoldings: IBalance[];
    portfolioValue: number;
    totalInvestments: number;
}

/**
 * Fetches dashboard data for a user from the dedicated endpoint
 * @param userId The ID of the user
 * @returns Promise with dashboard data
 */
export const getDashboardData = async (userId: string): Promise<IDashboardData> => {
    if (!userId) {
        console.error("getDashboardData called with undefined userId");
        return Promise.reject(new Error("User ID is required"));
    }

    try {
        // Use the dedicated dashboard endpoint
        const response = await api.get(`/Dashboard/user/${userId}`);

        // Ensure we have proper typing and default values
        const dashboardData: IDashboardData = {
            assetHoldings: Array.isArray(response.data?.assetHoldings)
                ? response.data.assetHoldings
                : [],
            portfolioValue: typeof response.data?.portfolioValue === 'number'
                ? response.data.portfolioValue
                : 0,
            totalInvestments: typeof response.data?.totalInvestments === 'number'
                ? response.data.totalInvestments
                : 0
        };

        return dashboardData;
    } catch (error) {
        console.error("Error fetching dashboard data:", error);
        throw error;
    }
};