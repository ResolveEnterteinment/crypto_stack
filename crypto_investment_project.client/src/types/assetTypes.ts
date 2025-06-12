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