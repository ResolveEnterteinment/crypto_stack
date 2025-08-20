// src/services/asset.ts
import api from "./api";
import { Asset } from "../types/assetTypes";

let apiFailureCount = 0;
const MAX_API_FAILURES = 3;
let useApiDisabled = false;
const API_RETRY_TIMEOUT = 30000; // 30 seconds

const ENDPOINTS = {
    GET_SUPPORTED: () => `/asset/get/supported`,
    NEW: () => `/asset/admin/new`,
    UPDATE: (assetId: string) => `/asset/admin/update/${assetId}`,
    HEALTH_CHECK: '/asset/health'
} as const;

/**
 * Fetches all available assets for investment
 * @returns Promise with array of asset objects
 */

export const getSupportedAssets = async (): Promise<Asset[]> => {
    // If we've failed too many times, use mock data without trying the API
    if (useApiDisabled) {
        console.warn('API calls temporarily disabled due to repeated failures');
    }

    try {
        const response = await api.get <Asset[]>(ENDPOINTS.GET_SUPPORTED());
        console.log("assetsService::getSupportedAssets => response: ", response)
        // Reset failure counter on success
        apiFailureCount = 0;
        if (response == null || !response.success || response.data == null)
            throw "Failed to get supported networks";

        // Process and validate response
        const assets = response.data ?? [];

        return assets;
    } catch (error: any) {
        // Handle error but add circuit breaker logic
        apiFailureCount++;

        if (apiFailureCount >= MAX_API_FAILURES) {
            useApiDisabled = true;

            // Re-enable API calls after a timeout
            setTimeout(() => {
                useApiDisabled = false;
                apiFailureCount = 0;
                console.info('Re-enabling API calls after timeout');
            }, API_RETRY_TIMEOUT);
        }

        // Log error and return mock data
        console.error('Error fetching available assets:', error);
        throw error;
    }
};