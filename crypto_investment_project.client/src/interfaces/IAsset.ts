// src/interfaces/IAsset.ts
export default interface IAsset {
    id: string;
    name: string;
    ticker: string;
    symbol?: string;
    precision: number;
    subunitName: string;
}