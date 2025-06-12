import { Asset } from "./assetTypes";

export interface Balance {
    id: string;
    available: number;
    locked: number;
    total: number;
    value: number;
    asset: Asset;
    lastUpdated: Date;
}

export interface PortfolioBalance {
  balances: Balance[];
  totalValueUSD: number;
    updatedAt: Date;
}