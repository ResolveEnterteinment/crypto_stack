// src/interfaces/IAsset.ts
export default interface IAsset {
    id: string;
    name: string;
    ticker: string;
    description?: string;
    type: string;
    exchange: string;
    price?: number;
    change24h?: number;
    marketCap?: number;
    isActive: boolean;
}