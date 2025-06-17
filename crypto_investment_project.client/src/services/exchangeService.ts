// src/services/exchangeApi.ts
import api from './api';

export interface MinNotionalResponse {
    [ticker: string]: number;
}

export interface PriceResponse {
    [ticker: string]: number;
}

class ExchangeService {
    /**
     * Get minimum notional value for a single asset
     */
    async getMinNotional(ticker: string, exchange?: string): Promise<number> {
        try {
            const params = new URLSearchParams();
            if (exchange) {
                params.append('exchange', exchange);
            }

            const url = `/Exchange/min-notional/${ticker}${params.toString() ? `?${params.toString()}` : ''}`;
            const response = await api.get(url);

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
                return {};
            }

            const params = new URLSearchParams();
            tickers.forEach(ticker => params.append('tickers', ticker));

            const response = await api.get(`/Exchange/min-notionals?${params.toString()}`);

            return response.data || {};
        } catch (error) {
            console.error('Failed to fetch min notionals for tickers:', tickers, error);
            throw new Error('Unable to fetch minimum order values');
        }
    }

    /**
     * Get current price for a single asset
     */
    async getAssetPrice(ticker: string, exchange?: string): Promise<number> {
        try {
            const params = new URLSearchParams();
            if (exchange) {
                params.append('exchange', exchange);
            }

            const url = `/Exchange/price/${ticker}${params.toString() ? `?${params.toString()}` : ''}`;
            const response = await api.get(url);

            return response.data;
        } catch (error) {
            console.error(`Failed to fetch price for ${ticker}:`, error);
            throw new Error(`Unable to fetch current price for ${ticker}`);
        }
    }

    /**
     * Get current prices for multiple assets
     */
    async getAssetPrices(tickers: string[]): Promise<PriceResponse> {
        try {
            if (tickers.length === 0) {
                return {};
            }

            // Make parallel requests for each ticker since there's no bulk price endpoint
            const pricePromises = tickers.map(async (ticker) => {
                try {
                    const price = await this.getAssetPrice(ticker);
                    return { ticker, price };
                } catch (error) {
                    console.warn(`Failed to fetch price for ${ticker}:`, error);
                    return { ticker, price: null };
                }
            });

            const results = await Promise.allSettled(pricePromises);
            const prices: PriceResponse = {};

            results.forEach((result, index) => {
                if (result.status === 'fulfilled' && result.value.price !== null) {
                    prices[result.value.ticker] = result.value.price;
                }
            });

            return prices;
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
            const response = await api.get('/Exchange/exchanges');
            return response.data || [];
        } catch (error) {
            console.error('Failed to fetch supported exchanges:', error);
            throw new Error('Unable to fetch supported exchanges');
        }
    }
}

export const exchangeService = new ExchangeService();
export default exchangeService;