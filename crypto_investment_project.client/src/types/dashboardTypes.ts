export interface PortfolioSummary {
  totalValue: number;
  totalProfitLoss: number;
  profitLossPercentage: number;
  currency: string;
}

export interface Dashboard {
    assetHoldings: AssetHolding[];
    portfolioValue: number;
    totalInvestments: number;
}

export interface AssetHolding {
    id: string;
    name: string;
    ticker: string;
    symbol: string;
    available: number;
    locked: number;
    total: number;
    value: number;
}