// src/services/asset.ts
import api from "./api";
import IAsset from "../interfaces/IAsset";

let apiFailureCount = 0;
const MAX_API_FAILURES = 3;
let useApiDisabled = false;
const API_RETRY_TIMEOUT = 30000; // 30 seconds

/**
 * Fetches all available assets for investment
 * @returns Promise with array of asset objects
 */

export const getSupportedAssets = async (): Promise<IAsset[]> => {
    // If we've failed too many times, use mock data without trying the API
    if (useApiDisabled) {
        console.warn('API calls temporarily disabled due to repeated failures');
    }

    try {
        const response = await api.get('/Asset/supported');

        // Reset failure counter on success
        apiFailureCount = 0;

        // Process and validate response
        const assets: IAsset[] = Array.isArray(response.data) ? response.data.map((asset: IAsset) => ({
            id: asset.id,
            name: asset.name,
            ticker: asset.ticker,
            symbol: asset.symbol,
            precision: asset.precision,
            subunitName: asset.subunitName
        })) : [];

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
/**
 * Fetches an individual asset by ID
 * @param assetId The ID of the asset
 * @returns Promise with the asset object
 */
export const getAssetById = async (assetId: string): Promise<IAsset> => {
    if (!assetId) {
        return Promise.reject(new Error('Asset ID is required'));
    }

    try {
        const { data } = await api.get(`/Asset/${assetId}`);

        // Process and validate response
        const asset: IAsset = {
            id: data.id,
            name: data.name || 'Unknown Asset',
            ticker: data.ticker || 'N/A',
            symbol: data.symbol,
            precision: data.precision || 18,
            subunitName: data.subunitName || null
        };

        return asset;
    } catch (error) {
        console.error(`Error fetching asset ${assetId}:`, error);
        throw error;
    }
};

/**
 * Fetches the current price of an asset
 * @param ticker The ticker symbol of the asset
 * @returns Promise with the current price
 */
export const getAssetPrice = async (ticker: string): Promise<number> => {
    if (!ticker) {
        return Promise.reject(new Error('Ticker is required'));
    }

    try {
        const { data } = await api.get(`/Exchange/price/${ticker}`);
        return typeof data === 'number' ? data : parseFloat(data);
    } catch (error) {
        console.error(`Error fetching price for ${ticker}:`, error);
        throw error;
    }
};