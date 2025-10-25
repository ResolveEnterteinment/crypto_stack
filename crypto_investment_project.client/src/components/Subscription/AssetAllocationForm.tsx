// src/components/Subscription/AssetAllocationForm.tsx - Refactored with Global Styling & Ant Design
import React, { useEffect, useMemo, useState } from 'react';
import {
    Button,
    Select,
    InputNumber,
    Card,
    Alert,
    Typography,
    Space,
    Divider,
    Progress,
    Tag,
    Tooltip
} from 'antd';
import {
    PlusOutlined,
    DeleteOutlined,
    InfoCircleOutlined,
    CheckCircleOutlined,
    WarningOutlined,
    CloseCircleOutlined
} from '@ant-design/icons';
import { getAssetColor as getColor } from '../../types/assetTypes';
import { Allocation } from '../../types/subscription';
import { FeeBreakdown } from '../../utils/feeCalculation';
import { EnhancedAsset } from './AssetAllocationStep';

const { Title, Text, Paragraph } = Typography;
const { Option } = Select;

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
    totalInvestmentAmount,
    grossInvestmentAmount,
    feeBreakdown,
    isLoading = false,
    error = null
}) => {
    const [selectedAssetId, setSelectedAssetId] = useState<string>('');
    const [percentAmount, setPercentAmount] = useState<number>(100);
    const [localError, setLocalError] = useState<string | null>(null);

    // Use NET amount for all calculations
    const netInvestmentAmount = totalInvestmentAmount;
    const displayGrossAmount = grossInvestmentAmount || totalInvestmentAmount;

    /**
     * Reset percent amount when allocations change
     */
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

    /**
     * Get color for asset based on ticker
     */
    const getAssetColor = (ticker: string): string => {
        return getColor(ticker);
    };

    /**
     * Get an asset by ID
     */
    const getAssetById = (id: string): EnhancedAsset | undefined => {
        return availableAssets.find(asset => asset.id === id);
    };

    /**
     * Calculate NET dollar amount from percentage
     */
    const calculateNetDollarAmount = (percentage: number): number => {
        return (percentage / 100) * netInvestmentAmount;
    };

    /**
     * Calculate GROSS dollar amount from percentage
     */
    const calculateGrossDollarAmount = (percentage: number): number => {
        return (percentage / 100) * displayGrossAmount;
    };

    /**
     * Calculate minimum percentage needed for an asset based on NET amount
     */
    const calculateMinimumPercentage = (asset: EnhancedAsset): number => {
        if (!asset.minNotional || netInvestmentAmount <= 0) return 0;
        return Math.ceil((asset.minNotional / netInvestmentAmount) * 100);
    };

    /**
     * Validate allocation against minimum notional
     */
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
                isValid: true,
                minPercentageNeeded: 0,
                minDollarAmount: 0,
                currentNetDollarAmount,
                currentGrossDollarAmount,
                warningMessage: `Minimum order requirements for ${asset.ticker} could not be verified.`
            };
        }

        const isValid = minNotional === 0 || currentNetDollarAmount >= minNotional;

        return {
            isValid,
            minPercentageNeeded,
            minDollarAmount: minNotional,
            currentNetDollarAmount,
            currentGrossDollarAmount,
            errorMessage: !isValid ?
                `Minimum order value for ${asset.ticker} is $${minNotional.toFixed(2)} but your net allocation is only $${currentNetDollarAmount.toFixed(2)}`
                : undefined
        };
    };

    /**
     * Get validation for all current allocations
     */
    const allocationValidations = useMemo(() => {
        return allocations.map(allocation => ({
            allocation,
            validation: validateAllocation(allocation.assetId, allocation.percentAmount)
        }));
    }, [allocations, netInvestmentAmount, availableAssets]);

    // Check if there are any validation errors or warnings
    const hasValidationErrors = allocationValidations.some(av => !av.validation.isValid);
    const hasValidationWarnings = allocationValidations.some(av => av.validation.warningMessage);

    /**
     * Handle adding a new asset allocation
     */
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

        // Validate minimum notional
        const validation = validateAllocation(selectedAssetId, percentAmount);

        // Allow adding even with warnings, but block if there's a hard error
        if (!validation.isValid && validation.errorMessage) {
            setLocalError(validation.errorMessage);
            return;
        }

        const asset = getAssetById(selectedAssetId);
        if (!asset) {
            setLocalError('Selected asset not found');
            return;
        }

        const newAllocation: Allocation = {
            assetId: asset.id,
            assetName: asset.name,
            assetTicker: asset.ticker,
            percentAmount: percentAmount
        };

        onChange([...allocations, newAllocation]);
        setSelectedAssetId('');
        setPercentAmount(Math.min(remainingPercentage - percentAmount, 100));
        setLocalError(null);
    };

    /**
     * Handle updating an existing allocation
     */
    const handleUpdateAllocation = (assetId: string, newPercentage: number) => {
        if (newPercentage <= 0 || newPercentage > 100) {
            return;
        }

        const updatedAllocations = allocations.map(allocation =>
            allocation.assetId === assetId
                ? { ...allocation, percentAmount: newPercentage }
                : allocation
        );

        onChange(updatedAllocations);
    };

    /**
     * Handle removing an allocation
     */
    const handleRemoveAllocation = (assetId: string) => {
        const updatedAllocations = allocations.filter(allocation => allocation.assetId !== assetId);
        onChange(updatedAllocations);
        setLocalError(null);
    };

    /**
     * Distribute remaining percentage evenly
     */
    const handleDistributeEvenly = () => {
        if (allocations.length === 0) return;

        const evenPercentage = Math.floor(100 / allocations.length);
        let remaining = 100 - (evenPercentage * allocations.length);

        const updatedAllocations = allocations.map((allocation, index) => ({
            ...allocation,
            percentAmount: evenPercentage + (index < remaining ? 1 : 0)
        }));

        onChange(updatedAllocations);
    };

    // Filter available assets (exclude already allocated)
    const filteredAssets = availableAssets.filter(
        asset => !allocations.some(allocation => allocation.assetId === asset.id)
    );

    // Get minimum percentage for selected asset
    const selectedAsset = selectedAssetId ? getAssetById(selectedAssetId) : null;
    const selectedAssetMinPercentage = selectedAsset ? calculateMinimumPercentage(selectedAsset) : 0;

    return (
        <div className="stack-lg">
            {/* Local Error Display */}
            {localError && (
                <Alert
                    message={localError}
                    type="error"
                    closable
                    onClose={() => setLocalError(null)}
                    showIcon
                    className="animate-fade-in"
                />
            )}

            {/* Validation Summary */}
            {(hasValidationErrors || hasValidationWarnings) && allocations.length > 0 && (
                <Alert
                    message={hasValidationErrors ? "Allocation Errors Detected" : "Allocation Warnings"}
                    description={
                        hasValidationErrors
                            ? "Some allocations don't meet minimum requirements. Please adjust them before proceeding."
                            : "Some minimum requirements couldn't be verified. Your orders may be rejected if below exchange minimums."
                    }
                    type={hasValidationErrors ? "error" : "warning"}
                    showIcon
                    icon={hasValidationErrors ? <CloseCircleOutlined /> : <WarningOutlined />}
                    className="animate-fade-in"
                />
            )}

            {/* Fee Breakdown Info */}
            {feeBreakdown && (
                <Card className="card-minimal" style={{ backgroundColor: 'var(--color-bg-container)' }}>
                    <Space direction="vertical" size="small" style={{ width: '100%' }}>
                        <div className="flex-between">
                            <Text type="secondary">Gross Investment Amount:</Text>
                            <Text strong>${feeBreakdown.grossAmount.toFixed(2)}</Text>
                        </div>
                        <div className="flex-between">
                            <Text type="secondary">Platform Fee (1%):</Text>
                            <Text>-${feeBreakdown.platformFee.toFixed(2)}</Text>
                        </div>
                        <div className="flex-between">
                            <Text type="secondary">Payment Processing Fee:</Text>
                            <Text>-${feeBreakdown.stripeFee.toFixed(2)}</Text>
                        </div>
                        <Divider style={{ margin: '8px 0' }} />
                        <div className="flex-between">
                            <Text strong>Net Investment Amount:</Text>
                            <Text strong className="text-primary">${feeBreakdown.netInvestmentAmount.toFixed(2)}</Text>
                        </div>
                    </Space>
                </Card>
            )}

            {/* Current Allocations */}
            {allocations.length > 0 && (
                <Card
                    title={
                        <div className="flex-between">
                            <Text strong>Current Allocation</Text>
                            <Button
                                type="link"
                                size="small"
                                onClick={handleDistributeEvenly}
                            >
                                Distribute Evenly
                            </Button>
                        </div>
                    }
                    className="elevation-2"
                >
                    {/* Progress Bar Visualization */}
                    <div className="mb-md">
                        <div style={{
                            height: '24px',
                            borderRadius: 'var(--radius-full)',
                            overflow: 'hidden',
                            display: 'flex',
                            backgroundColor: 'var(--color-bg-layout)'
                        }}>
                            {allocations.map(allocation => (
                                <Tooltip
                                    key={allocation.assetId}
                                    title={`${allocation.assetTicker}: ${allocation.percentAmount.toFixed(1)}%`}
                                >
                                    <div
                                        style={{
                                            width: `${allocation.percentAmount}%`,
                                            backgroundColor: getAssetColor(allocation.assetTicker),
                                            transition: 'all 0.3s ease'
                                        }}
                                    />
                                </Tooltip>
                            ))}
                        </div>
                    </div>

                    {/* Allocation List */}
                    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
                        {allocationValidations.map(({ allocation, validation }) => (
                            <Card
                                key={allocation.assetId}
                                size="small"
                                className={
                                    !validation.isValid
                                        ? 'border-error'
                                        : validation.warningMessage
                                            ? 'border-warning'
                                            : ''
                                }
                                style={{
                                    backgroundColor: !validation.isValid
                                        ? 'rgba(255, 59, 48, 0.05)'
                                        : validation.warningMessage
                                            ? 'rgba(255, 149, 0, 0.05)'
                                            : 'transparent'
                                }}
                            >
                                <div className="flex-between">
                                    <Space>
                                        <div
                                            style={{
                                                width: '16px',
                                                height: '16px',
                                                borderRadius: '50%',
                                                backgroundColor: getAssetColor(allocation.assetTicker)
                                            }}
                                        />
                                        <div>
                                            <Text strong>
                                                {allocation.assetName} ({allocation.assetTicker})
                                            </Text>
                                            <div>
                                                <Text
                                                    type={validation.isValid ? 'success' : 'danger'}
                                                    className="text-body-sm"
                                                >
                                                    Net: ${validation.currentNetDollarAmount.toFixed(2)}
                                                </Text>
                                                {validation.minDollarAmount > 0 && (
                                                    <Text type="secondary" className="text-body-sm">
                                                        {' '}(Min: ${validation.minDollarAmount.toFixed(2)})
                                                    </Text>
                                                )}
                                            </div>
                                            {validation.errorMessage && (
                                                <Text type="danger" className="text-body-sm">
                                                    <CloseCircleOutlined /> {validation.errorMessage}
                                                </Text>
                                            )}
                                            {validation.warningMessage && (
                                                <Text type="warning" className="text-body-sm">
                                                    <WarningOutlined /> {validation.warningMessage}
                                                </Text>
                                            )}
                                        </div>
                                    </Space>

                                    <Space>
                                        <InputNumber
                                            min={validation.minPercentageNeeded || 1}
                                            max={100}
                                            value={allocation.percentAmount}
                                            onChange={(value) => handleUpdateAllocation(allocation.assetId, value || 0)}
                                            formatter={value => `${value}%`}
                                            parser={value => Number(value!.replace('%', ''))}
                                            size="small"
                                            status={!validation.isValid ? 'error' : validation.warningMessage ? 'warning' : undefined}
                                            style={{ width: '100px' }}
                                        />
                                        <Button
                                            type="text"
                                            danger
                                            size="small"
                                            icon={<DeleteOutlined />}
                                            onClick={() => handleRemoveAllocation(allocation.assetId)}
                                        />
                                    </Space>
                                </div>
                            </Card>
                        ))}
                    </Space>

                    <Divider />

                    <div className="flex-between">
                        <Text strong>Remaining:</Text>
                        <Tag color={remainingPercentage === 0 ? 'success' : remainingPercentage > 0 ? 'processing' : 'error'}>
                            {remainingPercentage.toFixed(1)}%
                        </Tag>
                    </div>
                </Card>
            )}

            {/* Add New Allocation */}
            <Card
                title={<Text strong>Add Asset</Text>}
                className="elevation-2"
            >
                <Space direction="vertical" size="middle" style={{ width: '100%' }}>
                    <div className="grid-auto">
                        {/* Asset Selector */}
                        <Select
                            value={selectedAssetId}
                            onChange={setSelectedAssetId}
                            size="middle"
                            placeholder="Select an asset"
                            disabled={remainingPercentage <= 0 || filteredAssets.length === 0}
                            style={{ width: '100%' }}
                            showSearch
                            optionFilterProp="children"
                        >
                            {filteredAssets.map(asset => (
                                <Option key={asset.id} value={asset.id}>
                                    <Space>
                                        <div
                                            style={{
                                                width: '12px',
                                                height: '12px',
                                                borderRadius: '50%',
                                                backgroundColor: getAssetColor(asset.ticker)
                                            }}
                                        />
                                        <span>{asset.name} ({asset.ticker})</span>
                                        {asset.minNotional !== undefined && (
                                            <Text type="secondary" className="text-body-sm">
                                                Min: ${asset.minNotional.toFixed(2)}
                                            </Text>
                                        )}
                                    </Space>
                                </Option>
                            ))}
                        </Select>

                        {/* Percentage Input */}
                        <InputNumber
                            min={selectedAssetMinPercentage || 1}
                            max={remainingPercentage}
                            value={percentAmount}
                            onChange={(value) => setPercentAmount(value || 0)}
                            size="large"
                            formatter={value => `${value}%`}
                            parser={value => Number(value!.replace('%', ''))}
                            disabled={remainingPercentage <= 0 || !selectedAssetId}
                            placeholder={selectedAssetMinPercentage > 0 ? `Min: ${selectedAssetMinPercentage.toFixed(2)}%` : 'Percentage'}
                            style={{ width: '100%' }}
                        />
                    </div>

                    {/* Minimum Percentage Hint */}
                    {selectedAssetId && selectedAssetMinPercentage > 0 && (
                        <Alert
                            message={
                                <Text className="text-body-sm">
                                    Minimum allocation: {selectedAssetMinPercentage.toFixed(2)}%
                                    (${calculateNetDollarAmount(selectedAssetMinPercentage).toFixed(2)} net investment)
                                </Text>
                            }
                            type="info"
                            showIcon
                            icon={<InfoCircleOutlined />}
                        />
                    )}

                    {/* Dollar Amount Preview */}
                    {selectedAssetId && percentAmount > 0 && (
                        <Card size="small" style={{ backgroundColor: 'var(--color-bg-container)' }}>
                            <Space direction="vertical" size="small">
                                <div className="flex-between">
                                    <Text type="secondary">Net investment amount:</Text>
                                    <Text strong>${calculateNetDollarAmount(percentAmount).toFixed(2)}</Text>
                                </div>
                                {feeBreakdown && (
                                    <div className="flex-between">
                                        <Text type="secondary" className="text-body-sm">Gross amount:</Text>
                                        <Text className="text-body-sm">${calculateGrossDollarAmount(percentAmount).toFixed(2)}</Text>
                                    </div>
                                )}
                            </Space>
                        </Card>
                    )}

                    {/* Add Button */}
                    <Button
                        type="primary"
                        size="large"
                        icon={<PlusOutlined />}
                        onClick={handleAddAllocation}
                        disabled={
                            remainingPercentage <= 0 ||
                            !selectedAssetId ||
                            (selectedAssetMinPercentage > 0 && percentAmount < selectedAssetMinPercentage)
                        }
                        block
                    >
                        Add Asset
                    </Button>
                </Space>
            </Card>

            {/* No Assets Warning */}
            {availableAssets.length === 0 && (
                <Alert
                    message="No Assets Available"
                    description="No assets are currently available for allocation. Please try refreshing the page or contact support."
                    type="warning"
                    showIcon
                />
            )}
        </div>
    );
};

export default AssetAllocationForm;