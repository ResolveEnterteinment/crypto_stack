import React, { useState, useEffect, useMemo } from 'react';
import { AssetColors, getAssetColor as getColor } from '../../types/assetTypes';
import { Allocation } from '../../types/subscription';
import { EnhancedAsset } from './AssetAllocationStep';
import { FeeBreakdown } from '../../utils/feeCalculation';

interface AssetAllocationFormProps {
    availableAssets: EnhancedAsset[];
    allocations: Allocation[];
    onChange: (allocations: Allocation[]) => void;
    totalInvestmentAmount: number; // NET investment amount (after fees)
    grossInvestmentAmount?: number; // GROSS investment amount (before fees) - for display
    feeBreakdown?: FeeBreakdown; // Complete fee breakdown - for display
    isLoading?: boolean;
    error?: string | null;
}

interface AllocationValidation {
    isValid: boolean;
    minPercentageNeeded: number;
    minDollarAmount: number;
    currentNetDollarAmount: number;
    currentGrossDollarAmount: number;
    errorMessage?: string;
    warningMessage?: string;
}

const AssetAllocationForm: React.FC<AssetAllocationFormProps> = ({
    availableAssets,
    allocations,
    onChange,
    totalInvestmentAmount, // This is now the NET amount after fees
    grossInvestmentAmount,
    feeBreakdown,
    isLoading = false,
    error = null
}) => {
    const [selectedAssetId, setSelectedAssetId] = useState<string>('');
    const [percentAmount, setPercentAmount] = useState<number>(100);
    const [localError, setLocalError] = useState<string | null>(null);

    // Use NET amount for all calculations (this prevents the min notional validation issue)
    const netInvestmentAmount = totalInvestmentAmount;
    const displayGrossAmount = grossInvestmentAmount || totalInvestmentAmount;

    // Reset percent amount when allocations change
    useEffect(() => {
        const usedPercentage = allocations.reduce((sum, allocation) => sum + allocation.percentAmount, 0);
        const remaining = 100 - usedPercentage;
        if (remaining > 0) {
            setPercentAmount(Math.min(remaining, percentAmount));
        }
    }, [allocations]);

    // Calculate remaining percentage
    const usedPercentage = allocations.reduce((sum, allocation) => sum + allocation.percentAmount, 0);
    const remainingPercentage = 100 - usedPercentage;

    // Get color for asset based on ticker
    const getAssetColor = (ticker: string): string => {
        return getColor(ticker);
    };

    // Get an asset by ID
    const getAssetById = (id: string): EnhancedAsset | undefined => {
        return availableAssets.find(asset => asset.id === id);
    };

    // Calculate NET dollar amount from percentage (this is what actually gets invested)
    const calculateNetDollarAmount = (percentage: number): number => {
        return (percentage / 100) * netInvestmentAmount;
    };

    // Calculate GROSS dollar amount from percentage (for display purposes)
    const calculateGrossDollarAmount = (percentage: number): number => {
        return (percentage / 100) * displayGrossAmount;
    };

    // Calculate minimum percentage needed for an asset based on NET amount
    const calculateMinimumPercentage = (asset: EnhancedAsset): number => {
        if (!asset.minNotional || netInvestmentAmount <= 0) return 0;
        return Math.ceil((asset.minNotional / netInvestmentAmount) * 100); // Round up to 2 decimals
    };

    // Validate allocation against minimum notional (using NET amounts)
    const validateAllocation = (assetId: string, percentage: number): AllocationValidation => {
        const asset = getAssetById(assetId);
        if (!asset) {
            return {
                isValid: false,
                minPercentageNeeded: 0,
                minDollarAmount: 0,
                currentNetDollarAmount: 0,
                currentGrossDollarAmount: 0,
                errorMessage: 'Asset not found'
            };
        }

        const currentNetDollarAmount = calculateNetDollarAmount(percentage);
        const currentGrossDollarAmount = calculateGrossDollarAmount(percentage);
        const minNotional = asset.minNotional || 0;
        const minPercentageNeeded = calculateMinimumPercentage(asset);

        // Handle case where min notional data is not available
        if (asset.minNotional === undefined) {
            return {
                isValid: true, // Allow allocation but show warning
                minPercentageNeeded: 0,
                minDollarAmount: 0,
                currentNetDollarAmount,
                currentGrossDollarAmount,
                warningMessage: `Minimum order requirements for ${asset.ticker} could not be verified. Your order may be rejected if it's below the exchange minimum.`
            };
        }

        // 🔧 CRITICAL: Validate against NET amount (what actually gets invested)
        const isValid = minNotional === 0 || currentNetDollarAmount >= minNotional;

        return {
            isValid,
            minPercentageNeeded,
            minDollarAmount: minNotional,
            currentNetDollarAmount,
            currentGrossDollarAmount,
            errorMessage: !isValid ? 
                `Minimum order value for ${asset.ticker} is $${minNotional.toFixed(2)} but your net allocation is only $${currentNetDollarAmount.toFixed(2)} (${minPercentageNeeded.toFixed(2)}% of net investment needed)` 
                : undefined
        };
    };

    // Get validation for all current allocations
    const allocationValidations = useMemo(() => {
        return allocations.map(allocation => ({
            allocation,
            validation: validateAllocation(allocation.assetId, allocation.percentAmount)
        }));
    }, [allocations, netInvestmentAmount, availableAssets]);

    // Check if there are any validation errors or warnings
    const hasValidationErrors = allocationValidations.some(av => !av.validation.isValid);
    const hasValidationWarnings = allocationValidations.some(av => av.validation.warningMessage);

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

        // Validate minimum notional using NET amount
        const validation = validateAllocation(selectedAssetId, percentAmount);
        if (!validation.isValid) {
            setLocalError(validation.errorMessage || 'Allocation does not meet minimum requirements');
            return;
        }

        const selectedAsset = getAssetById(selectedAssetId);
        if (selectedAsset) {
            const newAllocation: Allocation = {
                assetId: selectedAsset.id,
                assetName: selectedAsset.name,
                assetTicker: selectedAsset.ticker,
                percentAmount: percentAmount
            };

            onChange([...allocations, newAllocation]);

            // Reset form
            setSelectedAssetId('');
            const newRemaining = remainingPercentage - percentAmount;
            setPercentAmount(newRemaining > 0 ? newRemaining : 0);
            setLocalError(null);
        }
    };

    // Handle removing an allocation
    const handleRemoveAllocation = (assetId: string) => {
        const updatedAllocations = allocations.filter(a => a.assetId !== assetId);
        onChange(updatedAllocations);
    };

    // Handle updating an allocation percentage
    const handleUpdateAllocation = (assetId: string, newPercentage: number) => {
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

        // Validate minimum notional for new percentage using NET amount
        const validation = validateAllocation(assetId, newPercentage);
        if (!validation.isValid) {
            setLocalError(validation.errorMessage || 'Allocation does not meet minimum requirements');
            return;
        }

        const updatedAllocations = allocations.map(allocation =>
            allocation.assetId === assetId
                ? { ...allocation, percentAmount: newPercentage }
                : allocation
        );

        onChange(updatedAllocations);
        setLocalError(null);
    };

    // Handle distributing remaining percentage evenly with minimum notional checks
    const handleDistributeEvenly = () => {
        if (allocations.length === 0) return;

        // Calculate base percentage per asset
        const basePercentPerAsset = Math.floor((100 / allocations.length) * 100) / 100;
        let remainder = 100 - (basePercentPerAsset * allocations.length);

        // Check if any assets would violate minimum notional with base percentage
        const assetsNeedingMinimum: { allocation: Allocation; minPercent: number }[] = [];

        allocations.forEach(allocation => {
            const asset = getAssetById(allocation.assetId);
            if (asset && asset.minNotional !== undefined) {
                const minPercent = calculateMinimumPercentage(asset);
                if (minPercent > basePercentPerAsset) {
                    assetsNeedingMinimum.push({ allocation, minPercent });
                }
            }
        });

        // If some assets need more than base percentage, redistribute
        if (assetsNeedingMinimum.length > 0) {
            const totalMinimumNeeded = assetsNeedingMinimum.reduce((sum, item) => sum + item.minPercent, 0);
            const remainingForOthers = 100 - totalMinimumNeeded;
            const otherAssetsCount = allocations.length - assetsNeedingMinimum.length;

            if (remainingForOthers < 0 || (otherAssetsCount > 0 && remainingForOthers / otherAssetsCount < 1)) {
                setLocalError('Cannot distribute evenly while meeting minimum order requirements. Please adjust manually.');
                return;
            }

            const percentForOthers = otherAssetsCount > 0 ? remainingForOthers / otherAssetsCount : 0;

            const updatedAllocations = allocations.map(allocation => {
                const needsMinimum = assetsNeedingMinimum.find(item => item.allocation.assetId === allocation.assetId);
                return {
                    ...allocation,
                    percentAmount: needsMinimum ? needsMinimum.minPercent : percentForOthers
                };
            });

            onChange(updatedAllocations);
        } else {
            // Standard even distribution
            const updatedAllocations = allocations.map((allocation, index) => ({
                ...allocation,
                percentAmount: index === allocations.length - 1
                    ? basePercentPerAsset + remainder
                    : basePercentPerAsset
            }));

            onChange(updatedAllocations);
        }
    };

    // Filter out already allocated assets from selection
    const filteredAssets = availableAssets.filter(
        asset => !allocations.some(allocation => allocation.assetId === asset.id)
    );

    // Get suggested minimum percentage for selected asset
    const selectedAssetMinPercentage = useMemo(() => {
        if (!selectedAssetId) return 0;
        const asset = getAssetById(selectedAssetId);
        return asset ? calculateMinimumPercentage(asset) : 0;
    }, [selectedAssetId, netInvestmentAmount]);

    // Auto-adjust percentage input when asset selection changes
    useEffect(() => {
        if (selectedAssetMinPercentage > percentAmount && selectedAssetMinPercentage <= remainingPercentage) {
            setPercentAmount(selectedAssetMinPercentage);
        }
    }, [selectedAssetMinPercentage]);

    // Get statistics about min notional data availability
    const assetsWithMinNotional = availableAssets.filter(a => a.minNotional !== undefined).length;
    const assetsWithoutMinNotional = availableAssets.length - assetsWithMinNotional;

    // If loading, show spinner
    if (isLoading) {
        return (
            <div className="flex justify-center items-center h-40">
                <div className="animate-spin w-12 h-12 border-4 border-blue-500 border-t-transparent rounded-full"></div>
                <p className="ml-3 text-gray-600">Loading asset allocation form...</p>
            </div>
        );
    }

    // If error from parent, show error message
    if (error) {
        return (
            <div className="bg-red-50 border-l-4 border-red-500 p-4 mb-4">
                <div className="flex">
                    <div className="flex-shrink-0">
                        <svg className="h-5 w-5 text-red-500" viewBox="0 0 20 20" fill="currentColor">
                            <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clipRule="evenodd" />
                        </svg>
                    </div>
                    <div className="ml-3">
                        <p className="text-sm leading-5 text-red-700">
                            {error}
                        </p>
                    </div>
                </div>
            </div>
        );
    }

    return (
        <div className="space-y-6">
            {/* Fee impact notice as clickable info icon */}
            {feeBreakdown && feeBreakdown.totalFees > 0 && (
                <>
                    <div className="flex justify-between items-center">
                        <div className="text-sm text-gray-600">
                            <div>Total Investment: <span className="font-semibold">${displayGrossAmount.toFixed(2)}</span></div>
                            <div>Fees: <span className="font-semibold">${feeBreakdown && feeBreakdown.totalFees.toFixed(2)}</span></div>
                            <div className="flex items-center">
                                Net Amount: <span className="font-semibold text-green-600 mr-1">${netInvestmentAmount.toFixed(2)}</span>
                                <div className="relative inline-block">
                                    <button 
                                        type="button"
                                        onClick={() => {
                                            const infoPopup = document.getElementById('net-trading-info-popup');
                                            if (infoPopup) {
                                                infoPopup.classList.toggle('hidden');
                                            }
                                        }}
                                        onMouseEnter={() => {
                                                        const infoPopup = document.getElementById('net-trading-info-popup');
                                                        if (infoPopup) {
                                                            infoPopup.classList.remove('hidden');
                                                        }
                                                    }}
                                        onMouseLeave={() => {
                                                        const infoPopup = document.getElementById('net-trading-info-popup');
                                                        if (infoPopup) {
                                                            infoPopup.classList.add('hidden');
                                                        }
                                                    }}
                                        className="inline-flex items-center text-green-500 hover:text-green-600 focus:outline-none"
                                        aria-label="Net trading information"
                                    >
                                        <svg className="h-4 w-4" viewBox="0 0 20 20" fill="currentColor">
                                            <path fillRule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7-4a1 1 0 11-2 0 1 1 0 012 0zM9 9a1 1 0 000 2v3a1 1 0 001 1h1a1 1 0 100-2v-3a1 1 0 00-1-1H9z" clipRule="evenodd" />
                                        </svg>
                                    </button>
                                    <div id="net-trading-info-popup" className="hidden absolute z-10 right-0 mt-2 w-64 bg-white rounded-md shadow-lg border border-green-200 p-3">
                                        <div className="flex justify-between items-start">
                                            <h4 className="font-medium text-green-700 text-xs">Net Investment Amount</h4>
                                                <button 
                                                    type="button" 
                                                    onClick={() => {
                                                        const infoPopup = document.getElementById('net-trading-info-popup');
                                                        if (infoPopup) {
                                                            infoPopup.classList.add('hidden');
                                                        }
                                                    }}
                                                    
                                                    className="text-gray-400 hover:text-gray-600"
                                                    aria-label="Close"
                                                >
                                                <svg className="h-3 w-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 18L18 6M6 6l12 12"></path>
                                                </svg>
                                            </button>
                                        </div>
                                        <p className="text-xs mt-1 text-gray-600">
                                            This is the actual amount available for purchasing assets after all fees have been deducted. Asset allocations and minimum order requirements are calculated based on this net amount.
                                        </p>
                                    </div>
                                </div>
                            </div>
                        </div>
                    </div>
                </>
            )}

            {/* Min notional data status indicator */}
            {assetsWithoutMinNotional > 0 && (
                <div className="bg-blue-50 border border-blue-200 text-blue-700 px-4 py-3 rounded mb-4">
                    <div className="flex items-start">
                        <svg className="h-5 w-5 text-blue-500 mt-0.5 mr-2" viewBox="0 0 20 20" fill="currentColor">
                            <path fillRule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7-4a1 1 0 11-2 0 1 1 0 012 0zM9 9a1 1 0 000 2v3a1 1 0 001 1h1a1 1 0 100-2v-3a1 1 0 00-1-1H9z" clipRule="evenodd" />
                        </svg>
                        <div>
                            <h4 className="font-medium">Minimum Order Requirements</h4>
                            <p className="text-sm mt-1">
                                {assetsWithMinNotional} of {availableAssets.length} assets have verified minimum order requirements.
                                {assetsWithoutMinNotional > 0 && (
                                    <span className="block mt-1">
                                        {assetsWithoutMinNotional} asset(s) could not be verified and may have order restrictions.
                                    </span>
                                )}
                            </p>
                        </div>
                    </div>
                </div>
            )}

            {/* Error message */}
            {localError && (
                <div className="bg-red-100 border border-red-400 text-red-700 px-4 py-3 rounded mb-4">
                    <p>{localError}</p>
                </div>
            )}

            {/* Validation errors for current allocations */}
            {hasValidationErrors && (
                <div className="bg-yellow-50 border border-yellow-400 text-yellow-700 px-4 py-3 rounded mb-4">
                    <div className="flex items-start">
                        <svg className="h-5 w-5 text-yellow-500 mt-0.5 mr-2" viewBox="0 0 20 20" fill="currentColor">
                            <path fillRule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clipRule="evenodd" />
                        </svg>
                        <div>
                            <h4 className="font-medium">Minimum Order Requirements Not Met (After Fees)</h4>
                            <div className="mt-1 space-y-1">
                                {allocationValidations.filter(av => !av.validation.isValid).map(av => (
                                    <p key={av.allocation.assetId} className="text-sm">
                                        • {av.validation.errorMessage}
                                    </p>
                                ))}
                            </div>
                        </div>
                    </div>
                </div>
            )}

            {/* Validation warnings for current allocations */}
            {!hasValidationErrors && hasValidationWarnings && (
                <div className="bg-amber-50 border border-amber-200 text-amber-700 px-4 py-3 rounded mb-4">
                    <div className="flex items-start">
                        <svg className="h-5 w-5 text-amber-500 mt-0.5 mr-2" viewBox="0 0 20 20" fill="currentColor">
                            <path fillRule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7-4a1 1 0 11-2 0 1 1 0 012 0zM9 9a1 1 0 000 2v3a1 1 0 001 1h1a1 1 0 100-2v-3a1 1 0 00-1-1H9z" clipRule="evenodd" />
                        </svg>
                        <div>
                            <h4 className="font-medium">Minimum Order Requirements Not Verified</h4>
                            <div className="mt-1 space-y-1">
                                {allocationValidations.filter(av => av.validation.warningMessage).map(av => (
                                    <p key={av.allocation.assetId} className="text-sm">
                                        • {av.validation.warningMessage}
                                    </p>
                                ))}
                            </div>
                        </div>
                    </div>
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
                        {allocationValidations.map(({ allocation, validation }) => (
                            <div key={allocation.assetId} className={`flex items-center justify-between p-3 rounded-lg border ${
                                !validation.isValid 
                                    ? 'bg-red-50 border-red-200' 
                                    : validation.warningMessage 
                                        ? 'bg-amber-50 border-amber-200' 
                                        : 'bg-gray-50 border-gray-200'
                                }`}>
                                <div className="flex items-center flex-1">
                                    <div
                                        className="w-4 h-4 rounded-full mr-2"
                                        style={{ backgroundColor: getAssetColor(allocation.assetTicker) }}
                                    />
                                    <div className="flex-1">
                                        <span className="font-medium">{allocation.assetName} ({allocation.assetTicker})</span>
                                        <div className="text-sm text-gray-600">
                                            <div className={validation.isValid ? 'text-green-600' : 'text-red-600'}>Net: ${validation.currentNetDollarAmount.toFixed(2)}</div>
                                            {validation.minDollarAmount > 0 ? (
                                                <span className={validation.isValid ? 'text-green-600' : 'text-red-600'}>
                                                    Min invenstment: ${validation.minDollarAmount.toFixed(2)}
                                                </span>
                                            ) : validation.warningMessage ? (
                                                <span className="text-amber-600">Min requirements unknown</span>
                                            ) : null}
                                        </div>
                                    </div>
                                </div>

                                <div className="flex items-center space-x-4">
                                    <div className="flex items-center">
                                        <input
                                            type="number"
                                            min={validation.minPercentageNeeded || 1}
                                            max="100"
                                            step="1"
                                            value={allocation.percentAmount}
                                            onChange={(e) => handleUpdateAllocation(allocation.assetId, parseFloat(e.target.value))}
                                            className={`w-20 p-1 border rounded text-center ${
                                                !validation.isValid 
                                                    ? 'border-red-300' 
                                                    : validation.warningMessage 
                                                        ? 'border-amber-300' 
                                                        : 'border-gray-300'
                                                }`}
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
                                    {asset.minNotional !== undefined && ` - Min: $${asset.minNotional.toFixed(2)}`}
                                    {asset.minNotional === undefined && ' - Min requirements unknown'}
                                </option>
                            ))}
                        </select>

                        {/* Minimum percentage hint */}
                        {selectedAssetId && selectedAssetMinPercentage > 0 && (
                            <p className="mt-1 text-sm text-blue-600">
                                Minimum allocation: {selectedAssetMinPercentage.toFixed(2)}% 
                                (${calculateNetDollarAmount(selectedAssetMinPercentage).toFixed(2)} net investment)
                            </p>
                        )}
                    </div>

                    {/* Percentage input */}
                    <div className="flex">
                        <input
                            type="number"
                            min={selectedAssetMinPercentage || 1}
                            max={remainingPercentage}
                            step="1"
                            value={percentAmount}
                            onChange={(e) => setPercentAmount(parseFloat(e.target.value))}
                            className="w-full p-3 border border-gray-300 rounded-l-lg focus:ring-2 focus:ring-blue-500 focus:outline-none"
                            disabled={remainingPercentage <= 0 || !selectedAssetId}
                            placeholder={selectedAssetMinPercentage > 0 ? `Min: ${selectedAssetMinPercentage.toFixed(2)}` : ''}
                        />
                        <div className="bg-gray-100 px-3 flex items-center border-y border-r border-gray-300 rounded-r-lg">
                            %
                        </div>
                    </div>
                </div>

                {/* Dollar amount preview */}
                {selectedAssetId && percentAmount > 0 && (
                    <div className="mt-2 text-sm text-gray-600">
                        <div>Net investment amount: ${calculateNetDollarAmount(percentAmount).toFixed(2)}</div>
                        {feeBreakdown && (
                            <div className="text-xs text-gray-500">
                                Gross amount: ${calculateGrossDollarAmount(percentAmount).toFixed(2)}
                            </div>
                        )}
                    </div>
                )}

                <button
                    type="button"
                    onClick={handleAddAllocation}
                    disabled={remainingPercentage <= 0 || !selectedAssetId || (selectedAssetMinPercentage > 0 && percentAmount < selectedAssetMinPercentage)}
                    className={`mt-3 ${remainingPercentage <= 0 || !selectedAssetId || (selectedAssetMinPercentage > 0 && percentAmount < selectedAssetMinPercentage)
                        ? 'bg-gray-300 cursor-not-allowed'
                        : 'bg-blue-600 hover:bg-blue-700'
                        } text-white py-2 px-4 rounded-lg transition-colors`}
                >
                    Add Asset
                </button>
            </div>

            {/* No assets warning */}
            {availableAssets.length === 0 && (
                <div className="bg-yellow-100 border border-yellow-400 text-yellow-700 px-4 py-3 rounded">
                    <p>No assets are currently available for allocation. Please try refreshing the page or contact support.</p>
                </div>
            )}

            {/* Enhanced tips for allocation */}
            <div className="bg-blue-50 p-4 rounded-lg">
                <h4 className="font-medium text-blue-700 mb-2">Tips for Asset Allocation</h4>
                <ul className="list-disc list-inside text-sm text-blue-700 space-y-1">
                    <li>Consider diversifying across different types of cryptocurrencies</li>
                    <li>Each asset has minimum order requirements that must be met after fees</li>
                    <li>Your total allocation must equal 100%</li>
                    <li>Minimum order amounts are validated against your net investment (after fees)</li>
                    <li>Larger investments may be needed to meet minimum requirements after fee deductions</li>
                </ul>
            </div>
        </div>
    );
};

export default AssetAllocationForm;