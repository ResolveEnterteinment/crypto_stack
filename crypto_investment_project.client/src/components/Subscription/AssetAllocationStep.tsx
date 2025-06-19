// src/components/Subscription/AssetAllocationStep.tsx (updated with fee-aware validation)
import React, { useState, useEffect, useMemo } from 'react';
import AssetAllocationForm from './AssetAllocationForm';
import { Allocation } from '../../types/subscription';
import { Asset } from '../../types/assetTypes';
import exchangeService, { MinNotionalResponse } from '../../services/exchangeService';
import { calculateNetInvestmentAmount, calculateFeeBreakdown, FeeBreakdown } from '../../utils/feeCalculation';

// Enhanced Asset interface with minimum notional value
export interface EnhancedAsset extends Asset {
    minNotional?: number; // Minimum order value in USD
    currentPrice?: number; // Current price in USD for calculations
}

interface AssetAllocationStepProps {
    formData: {
        allocations: Allocation[];
        amount: number;
        [key: string]: any;
    };
    updateFormData: (field: string, value: any) => void;
    availableAssets: Asset[];
    isLoading?: boolean;
    error?: string | null;
}

const AssetAllocationStep: React.FC<AssetAllocationStepProps> = ({
    formData,
    updateFormData,
    availableAssets,
    isLoading = false,
    error = null
}) => {
    const [enhancedAssets, setEnhancedAssets] = useState<EnhancedAsset[]>([]);
    const [minNotionalsLoading, setMinNotionalsLoading] = useState(false);
    const [minNotionalsError, setMinNotionalsError] = useState<string | null>(null);
    const [retryCount, setRetryCount] = useState(0);

    // Calculate fee breakdown for the investment amount
    const feeBreakdown: FeeBreakdown = useMemo(() => {
        try {
            return calculateFeeBreakdown(formData.amount);
        } catch (error) {
            // If amount is too low or invalid, return a safe default
            return {
                grossAmount: formData.amount,
                platformFee: 0,
                stripeFee: 0,
                totalFees: 0,
                netInvestmentAmount: formData.amount
            };
        }
    }, [formData.amount]);

    // Extract tickers from available assets
    const assetTickers = useMemo(() => 
        availableAssets.map(asset => asset.ticker), 
        [availableAssets]
    );

    // Fetch minimum notionals when assets change
    useEffect(() => {
        const fetchMinNotionals = async () => {
            if (availableAssets.length === 0) {
                setEnhancedAssets([]);
                return;
            }

            setMinNotionalsLoading(true);
            setMinNotionalsError(null);

            try {
                // Fetch minimum notionals for all tickers
                const minNotionals = await exchangeService.getMinNotionals(assetTickers);
                
                // Enhance assets with minimum notional data
                const enhanced: EnhancedAsset[] = availableAssets.map(asset => ({
                    ...asset,
                    minNotional: minNotionals[asset.ticker] || undefined
                }));

                setEnhancedAssets(enhanced);
                
                // Log any missing min notionals for debugging
                const missingNotionals = enhanced
                    .filter(asset => asset.minNotional === undefined)
                    .map(asset => asset.ticker);
                
                if (missingNotionals.length > 0) {
                    console.warn('Missing minimum notionals for tickers:', missingNotionals);
                }

            } catch (err) {
                console.error('Failed to fetch minimum notionals:', err);
                setMinNotionalsError(
                    err instanceof Error ? err.message : 'Failed to fetch minimum order requirements'
                );
                
                // Fall back to assets without min notional data
                const fallbackAssets: EnhancedAsset[] = availableAssets.map(asset => ({
                    ...asset,
                    minNotional: undefined
                }));
                setEnhancedAssets(fallbackAssets);
            } finally {
                setMinNotionalsLoading(false);
            }
        };

        fetchMinNotionals();
    }, [availableAssets, assetTickers, retryCount]);

    // Handler for when allocations change
    const handleAllocationChange = (allocations: Allocation[]) => {
        updateFormData('allocations', allocations);
    };

    // Handler for retrying min notional fetch
    const handleRetryMinNotionals = () => {
        setRetryCount(prev => prev + 1);
    };

    // Determine overall loading state
    const overallLoading = isLoading || minNotionalsLoading;

    // Determine error state - prioritize asset loading errors over min notional errors
    const overallError = error || minNotionalsError;

    // Check if investment amount is too low after fees
    const isInvestmentAmountTooLow = feeBreakdown.netInvestmentAmount <= 0;

    // Enhanced error display component
    const ErrorDisplay = () => {
        if (!overallError && !isInvestmentAmountTooLow) return null;

        const isMinNotionalError = !error && minNotionalsError;
        const isAmountError = isInvestmentAmountTooLow;

        return (
            <div className={`border-l-4 p-4 mb-6 ${
                isAmountError
                    ? 'bg-red-50 border-red-500'
                    : isMinNotionalError 
                        ? 'bg-yellow-50 border-yellow-500' 
                        : 'bg-red-50 border-red-500'
            }`}>
                <div className="flex">
                    <div className="flex-shrink-0">
                        <svg 
                            className={`h-5 w-5 ${
                                isAmountError || !isMinNotionalError ? 'text-red-500' : 'text-yellow-500'
                            }`} 
                            viewBox="0 0 20 20" 
                            fill="currentColor"
                        >
                            {isMinNotionalError && !isAmountError ? (
                                <path fillRule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clipRule="evenodd" />
                            ) : (
                                <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clipRule="evenodd" />
                            )}
                        </svg>
                    </div>
                    <div className="ml-3 flex-1">
                        <h3 className={`text-sm font-medium ${
                            isAmountError || !isMinNotionalError ? 'text-red-700' : 'text-yellow-700'
                        }`}>
                            {isAmountError 
                                ? 'Investment Amount Too Low' 
                                : isMinNotionalError 
                                    ? 'Unable to Load Minimum Order Requirements' 
                                    : 'Asset Loading Error'
                            }
                        </h3>
                        <p className={`mt-1 text-sm ${
                            isAmountError || !isMinNotionalError ? 'text-red-600' : 'text-yellow-600'
                        }`}>
                            {isAmountError 
                                ? `After fees ($${feeBreakdown.totalFees.toFixed(2)}), your net investment would be $${feeBreakdown.netInvestmentAmount.toFixed(2)}. Please increase your investment amount.`
                                : overallError
                            }
                        </p>
                        {isMinNotionalError && !isAmountError && (
                            <div className="mt-3">
                                <button
                                    type="button"
                                    onClick={handleRetryMinNotionals}
                                    className="text-sm bg-yellow-100 hover:bg-yellow-200 text-yellow-800 px-3 py-1 rounded border border-yellow-300 transition-colors"
                                >
                                    Retry Loading
                                </button>
                                <p className="mt-2 text-sm text-yellow-600">
                                    You can still create allocations, but minimum order validations may not be accurate.
                                </p>
                            </div>
                        )}
                    </div>
                </div>
            </div>
        );
    };

    // Loading state component
    const LoadingDisplay = () => {
        if (!overallLoading) return null;

        return (
            <div className="flex justify-center items-center h-40">
                <div className="animate-spin w-12 h-12 border-4 border-blue-500 border-t-transparent rounded-full"></div>
                <div className="ml-4">
                    <p className="text-gray-600 font-medium">
                        {isLoading ? 'Loading available assets...' : 'Loading minimum order requirements...'}
                    </p>
                    <p className="text-sm text-gray-500 mt-1">
                        This may take a few moments
                    </p>
                </div>
            </div>
        );
    };

    return (
        <div>
            <h2 className="text-2xl font-semibold text-gray-800 mb-4">Choose Your Asset Allocation</h2>
            <p className="text-gray-600 mb-6">
                Specify which cryptocurrencies to include in your portfolio and their allocation percentages.
                Each asset has minimum order requirements that must be met after fees are deducted.
            </p>
            <ErrorDisplay />

            {overallLoading ? (
                <LoadingDisplay />
            ) : !isInvestmentAmountTooLow ? (
                <AssetAllocationForm
                    availableAssets={enhancedAssets}
                    allocations={formData.allocations}
                    onChange={handleAllocationChange}
                    totalInvestmentAmount={feeBreakdown.netInvestmentAmount} // 🔧 Use NET amount, not gross
                    grossInvestmentAmount={feeBreakdown.grossAmount} // Also pass gross for display
                    feeBreakdown={feeBreakdown} // Pass full fee breakdown for additional context
                    isLoading={false} // We handle loading state here
                    error={null} // We handle error state here
                />
            ) : null}

            {/* Debug information in development */}
            {process.env.NODE_ENV === 'development' && enhancedAssets.length > 0 && (
                <div className="mt-6 p-4 bg-gray-100 rounded-lg">
                    <h4 className="font-medium text-gray-700 mb-2">Debug Info</h4>
                    <div className="text-sm text-gray-600">
                        <p>Assets loaded: {enhancedAssets.length}</p>
                        <p>Assets with min notional: {enhancedAssets.filter(a => a.minNotional !== undefined).length}</p>
                        <p>Gross investment: ${feeBreakdown.grossAmount.toFixed(2)}</p>
                        <p>Net investment: ${feeBreakdown.netInvestmentAmount.toFixed(2)}</p>
                        <p>Total fees: ${feeBreakdown.totalFees.toFixed(2)}</p>
                        {enhancedAssets.length > 0 && (
                            <details className="mt-2">
                                <summary className="cursor-pointer">Asset Details</summary>
                                <pre className="mt-2 text-xs overflow-auto">
                                    {JSON.stringify(
                                        enhancedAssets.map(a => ({
                                            ticker: a.ticker,
                                            name: a.name,
                                            minNotional: a.minNotional
                                        })), 
                                        null, 
                                        2
                                    )}
                                </pre>
                            </details>
                        )}
                    </div>
                </div>
            )}
        </div>
    );
};

export default AssetAllocationStep;