// src/components/Subscription/AssetAllocationStep.tsx
import React from 'react';
import IAsset from '../../interfaces/IAsset';
import IAllocation from '../../interfaces/IAllocation';
import AssetAllocationForm from './AssetAllocationForm';

interface AssetAllocationStepProps {
    formData: {
        allocations: Omit<IAllocation, 'id'>[];
        [key: string]: any;
    };
    updateFormData: (field: string, value: any) => void;
    availableAssets: IAsset[];
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
    // Handler for when allocations change
    const handleAllocationChange = (allocations: Omit<IAllocation, 'id'>[]) => {
        updateFormData('allocations', allocations);
    };

    return (
        <div>
            <h2 className="text-2xl font-semibold text-gray-800 mb-4">Choose Your Asset Allocation</h2>
            <p className="text-gray-600 mb-6">
                Specify which cryptocurrencies to include in your portfolio and their allocation percentages.
            </p>

            <AssetAllocationForm
                availableAssets={availableAssets}
                allocations={formData.allocations}
                onChange={handleAllocationChange}
                isLoading={isLoading}
                error={error}
            />
        </div>
    );
};

export default AssetAllocationStep;