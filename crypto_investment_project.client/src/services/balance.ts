import { Balance } from "../types/balanceTypes";
import api from "./api";

// API endpoints
const ENDPOINTS = {
    GET_ALL: () => `/balance/get/all`,
    GET_BY_ASSET: (ticker: string) => `/balance/get/asset/${ticker}`,
    GET_BY_USER_AND_ASSET: (userId: string, ticker: string) => `/balance/admin/user/${userId}/asset/${ticker}`,
    GET_TOTAL_INVESTMENTS: () => `/balance/totalInvestments`,
    GET_PORTFOLIO_VALUE: () => `/balance/portfolioValue`,
    HEALTH_CHECK: '/balance/health'
} as const;

export const getBalance = async (ticker: string): Promise<Balance> => {
    if (!ticker) {
        throw new Error("Ticker is required to get balance");
    }

    try {
        const response = await api.get<Balance>(ENDPOINTS.GET_BY_ASSET(ticker));
        if (response == null || response.data == null || response.success != true) {
            throw new Error(response.message || `Failed to get balance`);
        }
        return response.data;
    } catch (error) {
        console.error(`Error fetching balance for ${ticker}:`, error);
        throw error;
    }
};

export const getUserBalance = async (userId: string, ticker: string): Promise<Balance> => {
    if (!ticker) {
        throw new Error("Ticker is required to get user balance");
    }

    if (!userId) {
        throw new Error("User ID is required to get user balance");
    }
    try {
        const response = await api.get<Balance>(ENDPOINTS.GET_BY_USER_AND_ASSET(userId, ticker));
        if (response == null || response.data == null || response.success != true) {
            throw new Error(response.message || `Failed to get balance`);
        }
        return response.data;
    } catch (e) {
        console.error(`Error fetching ${ticker} balance for user ID ${userId}:`, e);
        throw e;
    }
    
};

export const getBalances = async (userId: string): Promise<Balance[]>  => {
    if (!userId) {
        throw new Error("User ID is required to get balances");
    }

    const response = await api.get<Balance[]>(ENDPOINTS.GET_ALL());

    if (response == null || response.data == null || response.success != true) {
        throw new Error(response.message || `Failed to get balances`);
    }

    return response.data ?? [];
};

export const getTotalInvestments = async (userId: string) : Promise<number> => {
    if (!userId) {
        throw new Error("User ID is required to get total investments");
    }

    try {
        const response = await api.get<number>(ENDPOINTS.GET_TOTAL_INVESTMENTS());

        if (response == null || response.data == null || response.success != true) {
            throw new Error(response.message || `Failed to get total investments`);
        }

        return response.data;
    } catch (error) {
        console.error(`Error fetching total investments for user ID ${userId}:`, error);
        throw error;
    }
};

export const getPortfolioValue = async (userId: string) : Promise<number> => {
    if (!userId) {
        throw new Error("User ID is required to get portfolio value");
    }

    try {
        const response = await api.get<number>(ENDPOINTS.GET_PORTFOLIO_VALUE());

        if (response == null || response.data == null || response.success != true) {
            throw new Error(response.message || `Failed to get portfolio value`);
        }

        return response.data;
    } catch (error) {
        console.error(`Error fetching portfolio value for user ID ${userId}:`, error);
        throw error;
    }
};