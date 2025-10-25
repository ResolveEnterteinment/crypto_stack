// src/components/Subscription/AssetAllocationStep.tsx - Refactored with Global Styling & Ant Design
import React, { useState, useEffect, useMemo } from 'react';
import { Alert, Spin, Button as AntButton, Typography } from 'antd';
import { ReloadOutlined, WarningOutlined, CloseCircleOutlined } from '@ant-design/icons';
import AssetAllocationForm from './AssetAllocationForm';
import { Allocation } from '../../types/subscription';
import { Asset } from '../../types/assetTypes';
import exchangeService from '../../services/exchangeService';
import { calculateFeeBreakdown, FeeBreakdown } from '../../utils/feeCalculation';

const { Title, Paragraph, Text } = Typography;

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

    /**
     * Fetch minimum notionals when assets change
     */
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
                    minNotional: minNotionals[asset.ticker] || 10 // Default to $10 if not found
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

    /**
     * Handler for when allocations change
     */
    const handleAllocationChange = (allocations: Allocation[]) => {
        updateFormData('allocations', allocations);
    };

    /**
     * Handler for retrying min notional fetch
     */
    const handleRetryMinNotionals = () => {
        setRetryCount(prev => prev + 1);
    };

    // Determine overall loading state
    const overallLoading = isLoading || minNotionalsLoading;

    // Determine error state - prioritize asset loading errors over min notional errors
    const overallError = error || minNotionalsError;

    // Check if investment amount is too low after fees
    const isInvestmentAmountTooLow = feeBreakdown.netInvestmentAmount <= 0;

    /**
     * Enhanced error display component
     */
    const ErrorDisplay = () => {
        if (!overallError && !isInvestmentAmountTooLow) return null;

        const isMinNotionalError = !error && minNotionalsError;
        const isAmountError = isInvestmentAmountTooLow;

        if (isAmountError) {
            return (
                <Alert
                    message="Investment Amount Too Low"
                    description={
                        <>
                            <Text>
                                After fees (${feeBreakdown.totalFees.toFixed(2)}), your net investment would be ${feeBreakdown.netInvestmentAmount.toFixed(2)}.
                                Please increase your investment amount.
                            </Text>
                        </>
                    }
                    type="error"
                    showIcon
                    icon={<CloseCircleOutlined />}
                    className="mb-lg animate-fade-in"
                    style={{ marginBottom: 'var(--spacing-lg)' }}
                />
            );
        }

        if (isMinNotionalError) {
            return (
                <Alert
                    message="Unable to Load Minimum Order Requirements"
                    description={
                        <>
                            <Paragraph className="mb-sm">{overallError}</Paragraph>
                            <AntButton
                                type="primary"
                                icon={<ReloadOutlined />}
                                onClick={handleRetryMinNotionals}
                                size="small"
                            >
                                Retry Loading
                            </AntButton>
                            <Paragraph className="text-body-sm mt-sm">
                                You can still create allocations, but minimum order validations may not be accurate.
                            </Paragraph>
                        </>
                    }
                    type="warning"
                    showIcon
                    icon={<WarningOutlined />}
                    className="mb-lg animate-fade-in"
                    style={{ marginBottom: 'var(--spacing-lg)' }}
                />
            );
        }

        return (
            <Alert
                message="Asset Loading Error"
                description={overallError}
                type="error"
                showIcon
                icon={<CloseCircleOutlined />}
                className="mb-lg animate-fade-in"
                style={{ marginBottom: 'var(--spacing-lg)' }}
            />
        );
    };

    /**
     * Loading state component
     */
    const LoadingDisplay = () => {
        if (!overallLoading) return null;

        return (
            <div className="flex-center" style={{ padding: 'var(--spacing-2xl)' }}>
                <Spin
                    size="large"
                    tip={isLoading ? 'Loading available assets...' : 'Loading minimum order requirements...'}
                >
                    <div style={{ padding: 'var(--spacing-xl)' }} />
                </Spin>
            </div>
        );
    };

    return (
        <div className="stack-lg">
            {/* Header */}
            <div>
                <Title level={2} className="mb-sm">
                    Choose Your Asset Allocation
                </Title>
                <Paragraph className="text-body text-secondary">
                    Specify which cryptocurrencies to include in your portfolio and their allocation percentages.
                    Each asset has minimum investment requirements that must be met after fees are deducted.
                </Paragraph>
            </div>

            {/* Error Display */}
            <ErrorDisplay />

            {/* Content */}
            {overallLoading ? (
                <LoadingDisplay />
            ) : !isInvestmentAmountTooLow ? (
                <AssetAllocationForm
                    availableAssets={enhancedAssets}
                    allocations={formData.allocations}
                    onChange={handleAllocationChange}
                    totalInvestmentAmount={feeBreakdown.netInvestmentAmount}
                    grossInvestmentAmount={feeBreakdown.grossAmount}
                    feeBreakdown={feeBreakdown}
                    isLoading={false}
                    error={null}
                />
            ) : null}
        </div>
    );
};

export default AssetAllocationStep;