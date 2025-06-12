// src/components/Dashboard/asset-balance-card.tsx
import React from 'react';
import { useNavigate } from "react-router-dom";
import { AssetHolding } from '../../types/dashboardTypes';

interface AssetBalanceCardProps {
    assetHoldings: AssetHolding[];
}

const AssetBalanceCard: React.FC<AssetBalanceCardProps> = ({ assetHoldings }) => {
    const navigate = useNavigate();

    // Define colors for different cryptocurrencies
    const assetColors: Record<string, string> = {
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
        // Add more cryptocurrency colors as needed
    };

    // Fallback color for assets not in the list
    const getAssetColor = (ticker: string | undefined): string => {
        if (!ticker) return '#6B7280'; // Gray fallback if ticker is undefined
        return assetColors[ticker] || '#6B7280'; // Gray fallback
    };

    // Calculate total value to determine proportions
    const calculateTotal = (): number => {
        if (!assetHoldings || !assetHoldings.length) return 0;
        return assetHoldings.reduce((sum, holding) => sum + holding.value, 0);
    };

    const totalValue = calculateTotal();

    // Handle withdraw click
    const handleWithdraw = (assetId: string | undefined) => {
        if (assetId == null || assetId == undefined)
            console.error("Invalid asset id");
        navigate(`/withdraw`);
    };

    // Format number with appropriate precision
    const formatAmount = (amount: number): string => {
        // For small amounts (like Bitcoin), show more decimals
        if (amount < 0.1) {
            return amount.toFixed(6);
        }
        // For medium amounts
        else if (amount < 1000) {
            return amount.toFixed(2);
        }
        // For large amounts, format with commas
        else {
            return amount.toLocaleString('en-US', {
                minimumFractionDigits: 2,
                maximumFractionDigits: 2
            });
        }
    };

    return (
        <div className="bg-white shadow rounded-lg p-4 h-full">
            <h2 className="text-xl font-semibold mb-4">Asset Holdings</h2>

            {assetHoldings && assetHoldings.length > 0 ? (
                <>
                    {/* Bar chart visualization */}
                    <div className="mb-4 h-4 bg-gray-200 rounded-full overflow-hidden flex">
                        {assetHoldings.map((holding) => {
                            // Calculate percentage width for the bar
                            const percentage = (holding.value / totalValue) * 100;
                            return (
                                <div
                                    key={holding.id}
                                    style={{
                                        width: `${percentage}%`,
                                        backgroundColor: getAssetColor(holding.ticker)
                                    }}
                                    className="h-full"
                                    title={`${holding.ticker || 'Unknown'}: ${percentage.toFixed(1)}%`}
                                />
                            );
                        })}
                    </div>

                    {/* List of balances */}
                    <div className="space-y-3">
                        {assetHoldings.map((holding) => (
                            <div key={holding.id} className="flex justify-between items-center">
                                <div className="flex items-center">
                                    <div
                                        className="w-3 h-3 rounded-full mr-2"
                                        style={{ backgroundColor: getAssetColor(holding.ticker) }}
                                    />
                                    <span className="font-medium">
                                        <span className="text-gray-500 ml-1">
                                            {holding.name || 'Unknown'}
                                            ({holding.ticker || 'N/A'})
                                        </span>
                                    </span>
                                </div>
                                <div className="font-bold">{formatAmount(holding.total)}</div>
                                <div className="font-bold">${holding.value.toFixed(2)}</div>
                                <div className="font-bold">
                                    <button
                                        onClick={() => handleWithdraw(holding.id)}
                                        className="flex-1 bg-red-100 hover:bg-red-200 text-red-700 py-2 px-4 rounded-md text-sm font-medium transition-colors"
                                    >
                                        Withdraw
                                    </button>
                                </div>
                            </div>
                        ))}
                    </div>

                    {/* Total */}
                    <div className="mt-4 pt-3 border-t border-gray-200">
                        <div className="flex justify-between items-center">
                            <span className="font-medium">Total Assets</span>
                            <span className="font-bold">{assetHoldings.length} tokens</span>
                        </div>
                    </div>
                    <div className="mt-4 pt-3 border-t border-gray-200">
                        <div className="flex justify-between items-center">
                            <span className="font-medium">Actions</span>
                        </div>
                    </div>
                </>
            ) : (
                <div className="flex justify-center items-center h-32 text-gray-400">
                    No assets found
                </div>
            )}
        </div>
    );
};

export default AssetBalanceCard;