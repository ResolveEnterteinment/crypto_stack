// src/types/assetTypes.ts (updated with AssetColors)

// Asset type definitions for the Crypto Investment Project

export type AssetType = 'cryptocurrency' | 'fiat' | 'token';

export interface Asset {
    id: string;
    name: string;
    ticker: string;
    symbol?: string;
    precision: number;
    subunitName: string;
    class: string;
}

// Asset colors for visualization
export const AssetColors: Record<string, string> = {
    BTC: '#F7931A',   // Bitcoin orange
    ETH: '#627EEA',   // Ethereum blue
    USDT: '#26A17B',  // Tether green
    USDC: '#2775CA',  // USD Coin blue
    BNB: '#F3BA2F',   // Binance Coin yellow
    XRP: '#23292F',   // Ripple dark gray
    ADA: '#0033AD',   // Cardano blue
    SOL: '#14F195',   // Solana green
    DOGE: '#C3A634',  // Dogecoin gold
    DOT: '#E6007A',   // Polkadot pink
    MATIC: '#8247E5', // Polygon purple
    AVAX: '#E84142', // Avalanche red
    LINK: '#2A5ADA', // Chainlink blue
    UNI: '#FF007A',  // Uniswap magenta
    ATOM: '#2E3148', // Cosmos dark blue
    LTC: '#BFBBBB',  // Litecoin silver
    BCH: '#8DC351',  // Bitcoin Cash green
    ALGO: '#000000', // Algorand black
    VET: '#15BDFF',  // VeChain light blue
    FTM: '#13B5EC',  // Fantom cyan
    DEFAULT: '#6B7280' // Gray for unknown assets
};

// Helper function to get asset color
export const getAssetColor = (ticker: string): string => {
    return AssetColors[ticker.toUpperCase()] || AssetColors.DEFAULT;
};