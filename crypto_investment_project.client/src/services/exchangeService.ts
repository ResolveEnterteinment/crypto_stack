// src/services/exchangeApi.ts
import api from './api';

export interface MinNotionalResponse {
    [ticker: string]: number;
}

export interface PriceResponse {
    [ticker: string]: number;
}

// API endpoints
const ENDPOINTS = {
    GET_PRICE: (ticker: string) => `/exchange/price/${ticker}`,
    GET_PRICES: () => `/exchange/price`,
    GET_MIN_NOTIONAL: (ticker: string) => `/exchange/min-notional/${ticker}`,
    GET_MIN_NOTIONALS: () => `/exchange/min-notionals`,
    GET_SUPPORTED_EXCHANGES: () => `/exchange/supported`,
    HEALTH_CHECK: '/exchange/health'
} as const;

class ExchangeService {
    /**
     * Get minimum notional value for a single asset
     */
    async getMinNotional(ticker: string): Promise<MinNotionalResponse> {
        try {
            const response = await api.get <MinNotionalResponse>(ENDPOINTS.GET_MIN_NOTIONAL(ticker));

            return response.data;
        } catch (error) {
            console.error(`Failed to fetch min notional for ${ticker}:`, error);
            throw new Error(`Unable to fetch minimum order value for ${ticker}`);
        }
    }

    /**
     * Get minimum notional values for multiple assets
     */
    async getMinNotionals(tickers: string[]): Promise<MinNotionalResponse> {
        try {
            if (tickers.length === 0) {
                return {}
;
            }

            // Build query parameters manually to avoid array bracket format
            const queryParams = new URLSearchParams();
            tickers.forEach(ticker => {
                queryParams.append('tickers', ticker);
            });

            const response = await api.get<MinNotionalResponse>(
                `${ENDPOINTS.GET_MIN_NOTIONALS()}?${queryParams.toString()}`
            );

            return response.data || {};
        } catch (error) {
            console.error('Failed to fetch min notionals for tickers:', tickers, error);
            throw new Error('Unable to fetch minimum order values');
        }
    }

    /**
     * Get current price for a single asset
     */
    async getAssetPrice(ticker: string): Promise<number> {
        try {

            const response = await api.get<number>(ENDPOINTS.GET_PRICE(ticker));

            return response.data;
        } catch (error) {
            console.error(`Failed to fetch price for ${ticker}:`, error);
            throw new Error(`Unable to fetch current price for ${ticker}`);
        }
    }

    /**
     * Get current prices for multiple assets
     */
    async getAssetPrices(tickers: string[]): Promise<PriceResponse[]> {
        try {
            if (tickers.length === 0) {
                return [];
            }

            // Build query parameters manually to avoid array bracket format
            const queryParams = new URLSearchParams();
            tickers.forEach(ticker => {
                queryParams.append('tickers', ticker);
            });

            const response = await api.get<MinNotionalResponse[]>(
                `${ENDPOINTS.GET_MIN_NOTIONALS()}?${queryParams.toString()}`
            );

            if (response == null || response.data == null || !response.success) {
                throw new Error(response.message || 'Failed to fetch asset prices');
            }

            return response.data;
        } catch (error) {
            console.error('Failed to fetch asset prices:', error);
            throw new Error('Unable to fetch current asset prices');
        }
    }

    /**
     * Get supported exchanges
     */
    async getSupportedExchanges(): Promise<string[]> {
        try {
            const response = await api.get<string[]>(ENDPOINTS.GET_SUPPORTED_EXCHANGES());

            if (response == null || !response.success) {
                throw new Error(response.message || 'Failed to get KYC status');
            }

            return response.data || [];
        } catch (error) {
            console.error('Failed to fetch supported exchanges:', error);
            throw new Error('Unable to fetch supported exchanges');
        }
    }
}

export default new ExchangeService();