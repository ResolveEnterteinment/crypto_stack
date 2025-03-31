// src/components/Dashboard/subscription-card.tsx
import React, { useState } from 'react';
import { ISubscription, IAllocation } from '../../services/subscription';

interface SubscriptionCardProps {
    subscription: ISubscription;
    onEdit: (id: string) => void;
    onCancel: (id: string) => void;
    onViewHistory: (id: string) => void;
    isLoading?: boolean;
}

const SubscriptionCard: React.FC<SubscriptionCardProps> = ({
    subscription,
    onEdit,
    onCancel,
    onViewHistory,
    isLoading = false
}) => {
    const [isExpanded, setIsExpanded] = useState(false);
    const [isConfirmingCancel, setIsConfirmingCancel] = useState(false);

    // Format date
    const formatDate = (dateString: string | Date): string => {
        if (!dateString) return 'N/A';

        try {
            const date = new Date(dateString);
            return date.toLocaleDateString('en-US', {
                year: 'numeric',
                month: 'short',
                day: 'numeric'
            });
        } catch (error) {
            console.error('Error formatting date:', error);
            return 'Invalid date';
        }
    };

    // Calculate progress to next payment
    const calculateProgress = (): number => {
        try {
            const now = new Date();
            const nextDue = new Date(subscription.nextDueDate);
            const createdDate = new Date(subscription.createdAt);

            // Get interval in days
            const intervalMap: Record<string, number> = {
                DAILY: 1,
                WEEKLY: 7,
                MONTHLY: 30
            };

            const intervalDays = intervalMap[subscription.interval.toUpperCase()] || 30;
            const totalDuration = intervalDays * 24 * 60 * 60 * 1000; // in ms
            const elapsed = now.getTime() - createdDate.getTime();
            const progress = (elapsed % totalDuration) / totalDuration * 100;

            return Math.min(Math.max(progress, 0), 100);
        } catch (error) {
            console.error('Error calculating progress:', error);
            return 0;
        }
    };

    // Safely format currency amounts
    const formatCurrency = (amount: number): string => {
        if (amount === undefined || amount === null) return '$0.00';

        try {
            return amount.toLocaleString('en-US', {
                minimumFractionDigits: 2,
                maximumFractionDigits: 2
            });
        } catch (error) {
            console.error('Error formatting currency:', error);
            return '$0.00';
        }
    };

    // Calculate days remaining until next payment
    const getDaysRemaining = (): number => {
        try {
            const now = new Date();
            const nextDue = new Date(subscription.nextDueDate);
            const diffTime = nextDue.getTime() - now.getTime();
            const diffDays = Math.ceil(diffTime / (1000 * 60 * 60 * 24));
            return Math.max(diffDays, 0);
        } catch (error) {
            console.error('Error calculating days remaining:', error);
            return 0;
        }
    };

    const handleCancelClick = () => {
        if (isConfirmingCancel) {
            onCancel(subscription.id);
            setIsConfirmingCancel(false);
        } else {
            setIsConfirmingCancel(true);
        }
    };

    // If loading, show skeleton
    if (isLoading) {
        return (
            <div className="bg-white shadow-lg rounded-xl p-6 animate-pulse">
                <div className="h-6 bg-gray-200 rounded w-3/4 mb-4"></div>
                <div className="h-4 bg-gray-200 rounded w-1/2 mb-6"></div>
                <div className="h-2 bg-gray-200 rounded mb-6"></div>
                <div className="h-4 bg-gray-200 rounded w-1/4 mb-6"></div>
                <div className="flex space-x-2">
                    <div className="h-10 bg-gray-200 rounded flex-1"></div>
                    <div className="h-10 bg-gray-200 rounded flex-1"></div>
                    <div className="h-10 bg-gray-200 rounded flex-1"></div>
                </div>
            </div>
        );
    }

    // Check for required data
    if (!subscription || !subscription.id) {
        return (
            <div className="bg-white shadow-lg rounded-xl p-6">
                <p className="text-red-500">Invalid subscription data</p>
            </div>
        );
    }

    return (
        <div
            className={`bg-white shadow-lg rounded-xl p-6 relative transition-all duration-200 ${subscription.isCancelled ? 'opacity-75' : ''
                }`}
            aria-label={`Subscription for ${subscription.amount} ${subscription.currency} ${subscription.interval.toLowerCase()}`}
        >
            {/* Status badge */}
            <div
                className={`absolute top-4 right-4 px-2 py-1 text-xs font-semibold rounded-full ${subscription.isCancelled ? 'bg-red-100 text-red-800' : 'bg-green-100 text-green-800'
                    }`}
                aria-label={`Status: ${subscription.isCancelled ? 'Cancelled' : 'Active'}`}
            >
                {subscription.isCancelled ? 'Cancelled' : 'Active'}
            </div>

            {/* Subscription header */}
            <div className="mb-4">
                <h3 className="text-xl font-semibold">
                    {formatCurrency(subscription.amount)} {subscription.currency} {subscription.interval.toLowerCase()}
                </h3>
                <p className="text-gray-500 text-sm">Created: {formatDate(subscription.createdAt)}</p>
            </div>

            {/* Progress to next payment */}
            <div className="mb-6">
                <div className="flex justify-between text-xs text-gray-500 mb-1">
                    <span>Progress to next payment</span>
                    <span>Next: {formatDate(subscription.nextDueDate)} ({getDaysRemaining()} days)</span>
                </div>
                <div
                    className="w-full bg-gray-200 rounded-full h-2.5"
                    role="progressbar"
                    aria-valuenow={calculateProgress()}
                    aria-valuemin={0}
                    aria-valuemax={100}
                >
                    <div
                        className="bg-blue-600 h-2.5 rounded-full transition-all duration-300"
                        style={{ width: `${calculateProgress()}%` }}
                    />
                </div>
            </div>

            {/* Toggle details button */}
            <button
                className="text-blue-600 text-sm font-medium mb-4 flex items-center focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2 rounded px-2 py-1 -ml-2 transition-colors duration-200 hover:bg-blue-50"
                onClick={() => setIsExpanded(!isExpanded)}
                aria-expanded={isExpanded}
                aria-controls={`details-${subscription.id}`}
            >
                {isExpanded ? 'Hide details' : 'Show details'}
                <svg
                    className={`ml-1 w-4 h-4 transition-transform duration-200 ${isExpanded ? 'rotate-180' : ''}`}
                    fill="none"
                    stroke="currentColor"
                    viewBox="0 0 24 24"
                    xmlns="http://www.w3.org/2000/svg"
                    aria-hidden="true"
                >
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M19 9l-7 7-7-7" />
                </svg>
            </button>

            {/* Expanded details */}
            <div
                id={`details-${subscription.id}`}
                className={`mb-6 space-y-2 text-sm overflow-hidden transition-all duration-300 ${isExpanded ? 'max-h-96 opacity-100' : 'max-h-0 opacity-0'
                    }`}
                aria-hidden={!isExpanded}
            >
                <p className="text-gray-700">
                    <span className="font-semibold">ID:</span> {subscription.id}
                </p>
                <p className="text-gray-700">
                    <span className="font-semibold">Total invested:</span> {formatCurrency(subscription.totalInvestments)} {subscription.currency}
                </p>
                <div>
                    <p className="font-semibold mb-1">Allocations:</p>
                    {subscription.allocations?.length > 0 ? (
                        <div className="bg-gray-50 p-3 rounded-md">
                            {subscription.allocations.map((alloc: IAllocation) => (
                                <div key={alloc.id} className="flex justify-between py-1 border-b border-gray-100 last:border-0">
                                    <span>{alloc.ticker}</span>
                                    <span className="font-medium">{alloc.percentAmount}%</span>
                                </div>
                            ))}
                        </div>
                    ) : (
                        <p className="text-gray-500 italic">No allocations found</p>
                    )}
                </div>
                <p className="text-gray-700">
                    <span className="font-semibold">End date:</span> {formatDate(subscription.endDate)}
                </p>
            </div>

            {/* Action buttons */}
            <div className="flex space-x-2">
                <button
                    onClick={() => onEdit(subscription.id)}
                    className="flex-1 bg-blue-100 hover:bg-blue-200 text-blue-700 py-2 px-4 rounded-md text-sm font-medium transition-colors duration-200 disabled:opacity-50 disabled:cursor-not-allowed focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2"
                    disabled={subscription.isCancelled}
                    aria-label={`Edit subscription ${subscription.id}`}
                >
                    Edit
                </button>

                {!subscription.isCancelled ? (
                    <button
                        onClick={handleCancelClick}
                        className={`flex-1 ${isConfirmingCancel
                                ? 'bg-red-500 hover:bg-red-600 text-white'
                                : 'bg-red-100 hover:bg-red-200 text-red-700'
                            } py-2 px-4 rounded-md text-sm font-medium transition-colors duration-200 focus:outline-none focus:ring-2 focus:ring-red-500 focus:ring-offset-2`}
                        aria-label={isConfirmingCancel ? 'Confirm cancellation' : 'Cancel subscription'}
                    >
                        {isConfirmingCancel ? 'Confirm' : 'Cancel'}
                    </button>
                ) : null}

                <button
                    onClick={() => onViewHistory(subscription.id)}
                    className="flex-1 bg-gray-100 hover:bg-gray-200 text-gray-700 py-2 px-4 rounded-md text-sm font-medium transition-colors duration-200 focus:outline-none focus:ring-2 focus:ring-gray-500 focus:ring-offset-2"
                    aria-label={`View history for subscription ${subscription.id}`}
                >
                    History
                </button>
            </div>

            {/* Cancel confirmation notice */}
            {isConfirmingCancel && (
                <div className="mt-2 text-xs text-red-600" role="alert">
                    <p>Are you sure? This action cannot be undone.</p>
                    <button
                        onClick={() => setIsConfirmingCancel(false)}
                        className="text-gray-600 underline mt-1 focus:outline-none focus:ring-2 focus:ring-gray-500 rounded"
                        aria-label="Cancel the cancellation process"
                    >
                        Cancel
                    </button>
                </div>
            )}
        </div>
    );
};

export default SubscriptionCard;