import React from 'react';

const AssetBalanceCard = ({ balances }) => {
  // Define colors for different cryptocurrencies
  const assetColors = {
    BTC: '#F7931A',
    ETH: '#627EEA',
    USDT: '#26A17B',
    // Add more cryptocurrency colors as needed
  };

  // Fallback color for assets not in the list
  const getAssetColor = (ticker) => {
    return assetColors[ticker] || '#6B7280';
  };

  // Calculate total value to determine proportions
  const calculateTotal = () => {
    if (!balances || !balances.length) return 0;
    return balances.reduce((sum, balance) => sum + balance.total, 0);
  };

  const totalBalance = calculateTotal();

  return (
    <div className="bg-white shadow rounded-lg p-4 h-full">
      <h2 className="text-xl font-semibold mb-4">Asset Holdings</h2>
      
      {balances && balances.length > 0 ? (
        <>
          <div className="mb-4 h-4 bg-gray-200 rounded-full overflow-hidden flex">
            {balances.map((balance, index) => {
              // Calculate percentage width for the bar
              const percentage = (balance.total / totalBalance) * 100;
              return (
                <div
                  key={balance.ticker}
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
          
          <div className="space-y-3">
            {balances.map((balance) => (
              <div key={balance.ticker} className="flex justify-between items-center">
                <div className="flex items-center">
                  <div
                    className="w-3 h-3 rounded-full mr-2"
                    style={{ backgroundColor: getAssetColor(balance.ticker) }}
                  />
                  <span className="font-medium">{balance.assetName} ({balance.ticker})</span>
                </div>
                <div className="font-bold">{balance.total}</div>
              </div>
            ))}
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