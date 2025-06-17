// src/components/Subscription/SubscriptionSummary.tsx
import React from 'react';
import { Allocation } from '../../types/subscription';

interface SubscriptionSummaryProps {
    interval: string;
    amount: number;
    currency: string;
    endDate: Date | null;
    allocations: Omit<Allocation, 'id'>[];
}

const SubscriptionSummary: React.FC<SubscriptionSummaryProps> = ({
    interval,
    amount,
    currency,
    endDate,
    allocations
}) => {
    // Format the interval for display
    const formatInterval = (intervalCode: string): string => {
        switch (intervalCode) {
            case 'ONCE':
                return 'One-time payment';
            case 'DAILY':
                return 'Daily';
            case 'WEEKLY':
                return 'Weekly';
            case 'MONTHLY':
                return 'Monthly';
            default:
                return intervalCode;
        }
    };

    // Format date for display
    const formatDate = (date: Date | null): string => {
        if (!date) return 'Ongoing';
        return date.toLocaleDateString('en-US', {
            year: 'numeric',
            month: 'long',
            day: 'numeric'
        });
    };

    // Calculate estimated annual investment (excluding one-time)
    const calculateAnnualAmount = (): number => {
        if (interval === 'ONCE') return amount;

        const multipliers: Record<string, number> = {
            'DAILY': 365,
            'WEEKLY': 52,
            'MONTHLY': 12
        };

        return amount * (multipliers[interval] || 0);
    };

    // Get estimated annual investment
    const annualAmount = calculateAnnualAmount();

    return (
        <div className="space-y-4">
            {/* Basic plan details */}
            <div className="grid grid-cols-2 gap-4">
                <div>
                    <p className="text-sm text-gray-500">Plan Type</p>
                    <p className="font-medium">{formatInterval(interval)}</p>
                </div>
                <div>
                    <p className="text-sm text-gray-500">Investment Amount</p>
                    <p className="font-medium">${amount.toFixed(2)} {currency}</p>
                </div>
                {interval !== 'ONCE' && (
                    <>
                        <div>
                            <p className="text-sm text-gray-500">End Date</p>
                            <p className="font-medium">{formatDate(endDate)}</p>
                        </div>
                        <div>
                            <p className="text-sm text-gray-500">Est. Annual Investment</p>
                            <p className="font-medium">${annualAmount.toFixed(2)} {currency}</p>
                        </div>
                    </>
                )}
            </div>

            {/* Asset allocation breakdown */}
            <div>
                <p className="text-sm text-gray-500 mb-2">Asset Allocation</p>
                <div className="space-y-2">
                    {allocations.map(allocation => (
                        <div key={allocation.assetId} className="flex justify-between items-center">
                            <div className="flex items-center">
                                <span className="font-medium">{allocation.assetName} ({allocation.assetTicker})</span>
                            </div>
                            <div className="flex items-center">
                                <span className="font-medium">{allocation.percentAmount}%</span>
                                <span className="text-gray-500 ml-2">
                                    ${((amount * allocation.percentAmount) / 100).toFixed(2)}
                                </span>
                            </div>
                        </div>
                    ))}
                </div>
            </div>

            {/* Fee disclosure */}
            <div className="border-t pt-4 mt-4">
                <p className="text-sm text-gray-500 mb-1">Transaction Fees</p>
                <p className="text-sm">Payment processing fee: <span className="font-medium">2.9% + $0.30</span></p>
                <p className="text-sm">Platform fee: <span className="font-medium">1.0%</span></p>
            </div>
        </div>
    );
}

module.exports = { SubscriptionSummary };