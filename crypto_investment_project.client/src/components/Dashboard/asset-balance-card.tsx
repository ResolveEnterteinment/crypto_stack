// src/components/Dashboard/asset-balance-card.tsx
import React from 'react';
import { IBalance } from '../../services/dashboard';

interface AssetBalanceCardProps {
    balances: IBalance[];
}

const AssetBalanceCard: React.FC<AssetBalanceCardProps> = ({ balances }) => {
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
    const getAssetColor = (ticker: string): string => {
        return assetColors[ticker] || '#6B7280'; // Gray fallback
    };

    // Calculate total value to determine proportions
    const calculateTotal = (): number => {
        if (!balances || !balances.length) return 0;
        return balances.reduce((sum, balance) => sum + balance.value, 0);
    };

    const totalValue = calculateTotal();

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

            {balances && balances.length > 0 ? (
                <>
                    {/* Bar chart visualization */}
                    <div className="mb-4 h-4 bg-gray-200 rounded-full overflow-hidden flex">
                        {balances.map((balance) => {
                            // Calculate percentage width for the bar
                            const percentage = (balance.value / totalValue) * 100;
                            return (
                                <div
                                    key={balance.assetId}
                                    style={{
                                        width: `${percentage}%`,
                                        backgroundColor: getAssetColor(balance.ticker)
                                    }}
                                    className="h-full"
                                    title={`${balance.ticker}: ${percentage.toFixed(1)}%`}
                                />
                            );
                        })}
                    </div>

                    {/* List of balances */}
                    <div className="space-y-3">
                        {balances.map((balance) => (
                            <div key={balance.ticker} className="flex justify-between items-center">
                                <div className="flex items-center">
                                    <div
                                        className="w-3 h-3 rounded-full mr-2"
                                        style={{ backgroundColor: getAssetColor(balance.ticker) }}
                                    />
                                    <span className="font-medium">
                                        {(balance.name || balance.ticker) &&
                                            <span className="text-gray-500 ml-1">{balance.name}({balance.ticker})</span>
                                        }
                                    </span>
                                </div>
                                <div className="font-bold">{formatAmount(balance.total)}</div>
                                <div className="font-bold">${balance.value.toFixed(2)}</div>
                            </div>
                        ))}
                    </div>

                    {/* Total */}
                    <div className="mt-4 pt-3 border-t border-gray-200">
                        <div className="flex justify-between items-center">
                            <span className="font-medium">Total Assets</span>
                            <span className="font-bold">{balances.length} tokens</span>
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