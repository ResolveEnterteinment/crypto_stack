// src/components/Subscription/AssetAllocationStep.jsx
import React, { useState } from 'react';

// Define asset colors for visualization
const ASSET_COLORS = {
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
    // Default color for others
    DEFAULT: '#6B7280' // Gray
};

const AssetAllocationStep = ({ formData, updateFormData, availableAssets }) => {
    // Local state
    const [selectedAssetId, setSelectedAssetId] = useState('');
    const [percentAmount, setPercentAmount] = useState(100);
    const [localError, setLocalError] = useState(null);

    // Get allocations from form data
    const { allocations } = formData;

    // Calculate remaining percentage
    const usedPercentage = allocations.reduce((sum, allocation) => sum + allocation.percentAmount, 0);
    const remainingPercentage = 100 - usedPercentage;

    // Get an asset by ID
    const getAssetById = (id) => {
        return availableAssets.find(asset => asset.id === id);
    };

    // Get color for asset based on ticker
    const getAssetColor = (ticker) => {
        return ASSET_COLORS[ticker] || ASSET_COLORS.DEFAULT;
    };

    // Handle adding a new asset allocation
    const handleAddAllocation = () => {
        if (!selectedAssetId) {
            setLocalError('Please select an asset');
            return;
        }

        if (percentAmount <= 0 || percentAmount > remainingPercentage) {
            setLocalError(`Please enter a valid percentage between 1 and ${remainingPercentage}%`);
            return;
        }

        // Check if asset already allocated
        const existingAllocation = allocations.find(a => a.assetId === selectedAssetId);
        if (existingAllocation) {
            setLocalError('This asset is already in your allocation. Edit or remove it first.');
            return;
        }

        const selectedAsset = getAssetById(selectedAssetId);
        if (selectedAsset) {
            const newAllocation = {
                assetId: selectedAsset.id,
                assetName: selectedAsset.name,
                assetTicker: selectedAsset.ticker,
                percentAmount: percentAmount
            };

            updateFormData('allocations', [...allocations, newAllocation]);

            // Reset form
            setSelectedAssetId('');
            setPercentAmount(remainingPercentage - percentAmount);
            setLocalError(null);
        }
    };

    // Handle removing an allocation
    const handleRemoveAllocation = (assetId) => {
        const updatedAllocations = allocations.filter(a => a.assetId !== assetId);
        updateFormData('allocations', updatedAllocations);
    };

    // Handle updating an allocation percentage
    const handleUpdateAllocation = (assetId, newPercentage) => {
        if (newPercentage <= 0) {
            return;
        }

        // Calculate how much this would change the total
        const currentAllocation = allocations.find(a => a.assetId === assetId);
        if (!currentAllocation) return;

        const percentageDiff = newPercentage - currentAllocation.percentAmount;

        // Check if this would exceed 100%
        if (usedPercentage + percentageDiff > 100) {
            setLocalError('Total allocation cannot exceed 100%');
            return;
        }

        const updatedAllocations = allocations.map(allocation =>
            allocation.assetId === assetId
                ? { ...allocation, percentAmount: newPercentage }
                : allocation
        );

        updateFormData('allocations', updatedAllocations);
        setLocalError(null);
    };

    // Handle distributing remaining percentage evenly
    const handleDistributeEvenly = () => {
        if (allocations.length === 0) return;

        const percentPerAsset = 100 / allocations.length;
        const updatedAllocations = allocations.map(allocation => ({
            ...allocation,
            percentAmount: percentPerAsset
        }));

        updateFormData('allocations', updatedAllocations);
    };

    // Filter out already allocated assets from selection
    const filteredAssets = availableAssets.filter(
        asset => !allocations.some(allocation => allocation.assetId === asset.id)
    );

    return (
        <div className="space-y-6">
            <div>
                <h2 className="text-2xl font-semibold text-gray-800 mb-4">Choose Your Asset Allocation</h2>
                <p className="text-gray-600 mb-6">
                    Specify which cryptocurrencies to include in your portfolio and their allocation percentages.
                </p>
            </div>

            {/* Error message */}
            {localError && (
                <div className="bg-red-100 border border-red-400 text-red-700 px-4 py-3 rounded mb-4">
                    <p>{localError}</p>
                </div>
            )}

            {/* Current allocations display */}
            {allocations.length > 0 && (
                <div className="mb-6">
                    <div className="flex justify-between mb-2">
                        <h3 className="font-medium">Current Allocation</h3>
                        <button
                            type="button"
                            onClick={handleDistributeEvenly}
                            className="text-sm text-blue-600 hover:text-blue-800"
                        >
                            Distribute Evenly
                        </button>
                    </div>

                    {/* Visual representation of allocation */}
                    <div className="mb-4 h-6 bg-gray-200 rounded-full overflow-hidden flex">
                        {allocations.map(allocation => (
                            <div
                                key={allocation.assetId}
                                style={{
                                    width: `${allocation.percentAmount}%`,
                                    backgroundColor: getAssetColor(allocation.assetTicker)
                                }}
                                className="h-full"
                                title={`${allocation.assetTicker}: ${allocation.percentAmount.toFixed(1)}%`}
                            />
                        ))}
                    </div>

                    {/* Allocation list with edit controls */}
                    <div className="space-y-2">
                        {allocations.map(allocation => (
                            <div key={allocation.assetId} className="flex items-center justify-between bg-gray-50 p-3 rounded-lg">
                                <div className="flex items-center">
                                    <div
                                        className="w-4 h-4 rounded-full mr-2"
                                        style={{ backgroundColor: getAssetColor(allocation.assetTicker) }}
                                    />
                                    <span className="font-medium">{allocation.assetName} ({allocation.assetTicker})</span>
                                </div>

                                <div className="flex items-center space-x-4">
                                    <div className="flex items-center">
                                        <input
                                            type="number"
                                            min="1"
                                            max="100"
                                            value={allocation.percentAmount}
                                            onChange={(e) => handleUpdateAllocation(allocation.assetId, parseFloat(e.target.value))}
                                            className="w-16 p-1 border border-gray-300 rounded text-center"
                                        />
                                        <span className="ml-1">%</span>
                                    </div>

                                    <button
                                        type="button"
                                        onClick={() => handleRemoveAllocation(allocation.assetId)}
                                        className="text-red-500 hover:text-red-700"
                                        aria-label="Remove allocation"
                                    >
                                        <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 18L18 6M6 6l12 12"></path>
                                        </svg>
                                    </button>
                                </div>
                            </div>
                        ))}
                    </div>

                    <div className="mt-2 text-sm">
                        <span className="font-medium">Remaining:</span> {remainingPercentage.toFixed(1)}%
                    </div>
                </div>
            )}

            {/* Add new allocation form */}
            <div className="border-t pt-4">
                <h3 className="font-medium mb-2">Add Asset</h3>

                <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                    {/* Asset selector */}
                    <div className="md:col-span-2">
                        <select
                            value={selectedAssetId}
                            onChange={(e) => setSelectedAssetId(e.target.value)}
                            className="w-full p-3 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:outline-none"
                            disabled={remainingPercentage <= 0 || filteredAssets.length === 0}
                        >
                            <option value="">Select an asset</option>
                            {filteredAssets.map(asset => (
                                <option key={asset.id} value={asset.id}>
                                    {asset.name} ({asset.ticker})
                                </option>
                            ))}
                        </select>
                    </div>

                    {/* Percentage input */}
                    <div className="flex">
                        <input
                            type="number"
                            min="1"
                            max={remainingPercentage}
                            value={percentAmount}
                            onChange={(e) => setPercentAmount(parseFloat(e.target.value))}
                            className="w-full p-3 border border-gray-300 rounded-l-lg focus:ring-2 focus:ring-blue-500 focus:outline-none"
                            disabled={remainingPercentage <= 0 || !selectedAssetId}
                        />
                        <div className="bg-gray-100 px-3 flex items-center border-y border-r border-gray-300 rounded-r-lg">
                            %
                        </div>
                    </div>
                </div>

                <button
                    type="button"
                    onClick={handleAddAllocation}
                    disabled={remainingPercentage <= 0 || !selectedAssetId}
                    className={`mt-3 ${remainingPercentage <= 0 || !selectedAssetId
                        ? 'bg-gray-300 cursor-not-allowed'
                        : 'bg-blue-600 hover:bg-blue-700'
                        } text-white py-2 px-4 rounded-lg transition-colors`}
                >
                    Add Asset
                </button>
            </div>

            {/* Tips for allocation */}
            <div className="bg-blue-50 p-4 rounded-lg mt-6">
                <h4 className="font-medium text-blue-700 mb-2">Tips for Asset Allocation</h4>
                <ul className="list-disc list-inside text-sm text-blue-700 space-y-1">
                    <li>Consider diversifying across different types of cryptocurrencies</li>
                    <li>Higher allocations to stable coins may reduce volatility</li>
                    <li>Your total allocation must equal 100%</li>
                    <li>Review historical performance to inform your investment strategy</li>
                </ul>
            </div>

            {/* No assets warning */}
            {availableAssets.length === 0 && (
                <div className="bg-yellow-100 border border-yellow-400 text-yellow-700 px-4 py-3 rounded">
                    <p>No assets are currently available for allocation.</p>
                </div>
            )}
        </div>
    );
}
export default AssetAllocationStep;