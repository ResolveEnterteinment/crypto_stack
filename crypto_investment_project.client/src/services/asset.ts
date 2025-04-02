// src/services/asset.ts
import api from "./api";
import IAsset from "../interfaces/IAsset";

/**
 * Fetches all available assets for investment
 * @returns Promise with array of asset objects
 */
export const getAvailableAssets = async (): Promise<IAsset[]> => {
    try {
        const { data } = await api.get('/v1/Asset/available');

        // Process and validate response
        const assets: IAsset[] = Array.isArray(data) ? data.map((asset: any) => ({
            id: asset.id,
            name: asset.name || 'Unknown Asset',
            ticker: asset.ticker,
            description: asset.description,
            type: asset.type || 'CRYPTO',
            exchange: asset.exchange || 'default',
            price: typeof asset.price === 'number' ? asset.price : parseFloat(asset.price || '0'),
            change24h: typeof asset.change24h === 'number' ? asset.change24h : parseFloat(asset.change24h || '0'),
            marketCap: typeof asset.marketCap === 'number' ? asset.marketCap : parseFloat(asset.marketCap || '0'),
            isActive: Boolean(asset.isActive)
        })) : [];

        return assets.filter(asset => asset.isActive);
    } catch (error) {
        console.error('Error fetching available assets:', error);

        // Fallback to mock data for development or when the API is not available
        if (process.env.NODE_ENV === 'development') {
            console.warn('Using mock asset data in development mode');
            return getMockAssets();
        }

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
        const { data } = await api.get(`/v1/Asset/${assetId}`);

        // Process and validate response
        const asset: IAsset = {
            id: data.id,
            name: data.name || 'Unknown Asset',
            ticker: data.ticker,
            description: data.description,
            type: data.type || 'CRYPTO',
            exchange: data.exchange || 'default',
            price: typeof data.price === 'number' ? data.price : parseFloat(data.price || '0'),
            change24h: typeof data.change24h === 'number' ? data.change24h : parseFloat(data.change24h || '0'),
            marketCap: typeof data.marketCap === 'number' ? data.marketCap : parseFloat(data.marketCap || '0'),
            isActive: Boolean(data.isActive)
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
        const { data } = await api.get(`/v1/Exchange/price/${ticker}`);
        return typeof data === 'number' ? data : parseFloat(data);
    } catch (error) {
        console.error(`Error fetching price for ${ticker}:`, error);
        throw error;
    }
};

/**
 * Provides mock asset data for development purposes
 * @returns Array of mock assets
 */
const getMockAssets = (): IAsset[] => {
    return [
        {
            id: "1",
            name: "Bitcoin",
            ticker: "BTC",
            description: "The original cryptocurrency",
            type: "CRYPTO",
            exchange: "Binance",
            price: 45000,
            change24h: 2.5,
            marketCap: 850000000000,
            isActive: true
        },
        {
            id: "2",
            name: "Ethereum",
            ticker: "ETH",
            description: "Programmable blockchain platform",
            type: "CRYPTO",
            exchange: "Binance",
            price: 3200,
            change24h: 1.8,
            marketCap: 380000000000,
            isActive: true
        },
        {
            id: "3",
            name: "Tether",
            ticker: "USDT",
            description: "USD-pegged stablecoin",
            type: "CRYPTO",
            exchange: "Binance",
            price: 1,
            change24h: 0.01,
            marketCap: 68000000000,
            isActive: true
        },
        {
            id: "4",
            name: "USD Coin",
            ticker: "USDC",
            description: "USD-pegged stablecoin by Circle",
            type: "CRYPTO",
            exchange: "Coinbase",
            price: 1,
            change24h: 0.02,
            marketCap: 55000000000,
            isActive: true
        },
        {
            id: "5",
            name: "Binance Coin",
            ticker: "BNB",
            description: "Binance ecosystem token",
            type: "CRYPTO",
            exchange: "Binance",
            price: 520,
            change24h: 3.1,
            marketCap: 80000000000,
            isActive: true
        },
        {
            id: "6",
            name: "Solana",
            ticker: "SOL",
            description: "High-performance blockchain",
            type: "CRYPTO",
            exchange: "FTX",
            price: 150,
            change24h: 5.2,
            marketCap: 52000000000,
            isActive: true
        },
        {
            id: "7",
            name: "Cardano",
            ticker: "ADA",
            description: "Proof-of-stake blockchain platform",
            type: "CRYPTO",
            exchange: "Binance",
            price: 1.2,
            change24h: -1.5,
            marketCap: 41000000000,
            isActive: true
        },
        {
            id: "8",
            name: "Ripple",
            ticker: "XRP",
            description: "Digital payment protocol",
            type: "CRYPTO",
            exchange: "Binance",
            price: 0.8,
            change24h: 0.7,
            marketCap: 39000000000,
            isActive: true
        }
    ];
};