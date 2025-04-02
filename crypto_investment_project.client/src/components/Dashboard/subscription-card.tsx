import React, { useState } from 'react';
import IAllocation from '../../interfaces/IAllocation';
import SubscriptionCardProps from '../../interfaces/SubscriptionCardProps';


const SubscriptionCard: React.FC<SubscriptionCardProps> = ({
    subscription,
    onEdit,
    onCancel,
    onViewHistory
}) => {
    const [isExpanded, setIsExpanded] = useState(false);

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

    // Format date
    const formatDate = (dateString: string | Date): string => {
        return new Date(dateString).toLocaleDateString('en-US', {
            year: 'numeric',
            month: 'short',
            day: 'numeric'
        });
    };

    // Calculate progress to next payment
    const calculateProgress = (): number => {
        const now = new Date();
        // We create this variable but use it in the calculation below
        const createdDate = new Date(subscription.createdAt);

        // Get interval in days
        const intervalMap: Record<string, number> = {
            DAILY: 1,
            WEEKLY: 7,
            MONTHLY: 30
        };

        const intervalKey = subscription.interval.toUpperCase();
        const intervalDays = intervalMap[intervalKey] || 30;
        const totalDuration = intervalDays * 24 * 60 * 60 * 1000; // in ms
        const elapsed = now.getTime() - createdDate.getTime();
        const progress = (elapsed % totalDuration) / totalDuration * 100;

        return Math.min(Math.max(progress, 0), 100);
    };

    // Handle view history click with proper ID validation
    const handleViewHistoryClick = () => {
        if (subscription && subscription.id) {
            onViewHistory(subscription.id);
        } else {
            console.error("Cannot view history: Missing subscription ID");
        }
    };

    return (
        <div className={`bg-white shadow-lg rounded-xl p-6 relative transition-all duration-200 ${subscription.isCancelled ? 'opacity-75' : ''}`}>
            {/* Status badge */}
            <div className={`absolute top-4 right-4 px-2 py-1 text-xs font-semibold rounded-full ${subscription.isCancelled ? 'bg-red-100 text-red-800' : 'bg-green-100 text-green-800'
                }`}>
                {subscription.isCancelled ? 'Cancelled' : 'Active'}
            </div>

            {/* Subscription header */}
            <div className="mb-4">
                <h3 className="text-xl font-semibold">
                    {subscription.amount} {subscription.currency} {subscription.interval.toLowerCase()}
                </h3>
                <p className="text-gray-500 text-sm">Created: {formatDate(subscription.createdAt)}</p>
            </div>

            {/* Progress to next payment */}
            <div className="mb-6">
                <div className="flex justify-between text-xs text-gray-500 mb-1">
                    <span>Last payment</span>
                    <span>Next payment: {formatDate(subscription.nextDueDate)}</span>
                </div>
                <div className="w-full bg-gray-200 rounded-full h-2.5">
                    <div
                        className="bg-blue-600 h-2.5 rounded-full"
                        style={{ width: `${calculateProgress()}%` }}
                    />
                </div>
            </div>

            {/* Toggle details button */}
            <button
                className="text-blue-600 text-sm font-medium mb-4 flex items-center"
                onClick={() => setIsExpanded(!isExpanded)}
                type="button"
                aria-expanded={isExpanded}
            >
                {isExpanded ? 'Hide details' : 'Show details'}
                <svg
                    className={`ml-1 w-4 h-4 transition-transform ${isExpanded ? 'rotate-180' : ''}`}
                    fill="none"
                    stroke="currentColor"
                    viewBox="0 0 24 24"
                    xmlns="http://www.w3.org/2000/svg"
                >
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M19 9l-7 7-7-7" />
                </svg>
            </button>

            {/* Expanded details */}
            {isExpanded && (
                <div className="mb-6 space-y-2 text-sm">
                    <p className="text-gray-700">
                        <span className="font-semibold">ID:</span> {subscription.id}
                    </p>
                    <p className="text-gray-700">
                        <span className="font-semibold">Total invested:</span> {subscription.totalInvestments} {subscription.currency}
                    </p>
                    <div>
                        <p className="font-semibold mb-1">Allocations:</p>
                        <div className="mb-4 h-4 bg-gray-200 rounded-full overflow-hidden flex">
                            {subscription.allocations?.map((alloc: IAllocation) => {
                                // Calculate percentage width for the bar
                                const percentage = alloc.percentAmount;
                                return (
                                    <div
                                        key={alloc.assetId}
                                        style={{
                                            width: `${percentage}%`,
                                            backgroundColor: assetColors[alloc.assetTicker] || '#6B7280'
                                        }}
                                        className="h-full"
                                        title={`${alloc.assetTicker}: ${percentage.toFixed(1)}%`}
                                    />
                                );
                            })}
                        </div>
                        {/* List of balances */}
                        <div className="space-y-3">
                            {subscription.allocations.map((alloc: IAllocation) => (
                                <div key={alloc.assetId} className="flex justify-between items-center">
                                    <div className="flex items-center">
                                        <div
                                            className="w-3 h-3 rounded-full mr-2"
                                            style={{ backgroundColor: assetColors[alloc.assetTicker] || '#6B7280' }}
                                        />
                                        <span className="font-medium">
                                            {(alloc.assetName || alloc.assetTicker) && <span className="text-gray-500 ml-1">{alloc.assetName} ({alloc.assetTicker})</span>}
                                        </span>
                                    </div>
                                    <div className="font-bold">{alloc.percentAmount}%</div>
                                </div>
                            ))}
                        </div>

                    </div>
                </div>
            )}

            {/* Action buttons */}
            <div className="flex space-x-2">
                <button
                    onClick={() => onEdit(subscription.id)}
                    className="flex-1 bg-blue-100 hover:bg-blue-200 text-blue-700 py-2 px-4 rounded-md text-sm font-medium transition-colors"
                    disabled={subscription.isCancelled}
                >
                    Edit
                </button>
                {!subscription.isCancelled && (
                    <button
                        onClick={() => onCancel(subscription.id)}
                        className="flex-1 bg-red-100 hover:bg-red-200 text-red-700 py-2 px-4 rounded-md text-sm font-medium transition-colors"
                    >
                        Cancel
                    </button>
                )}
                <button
                    onClick={handleViewHistoryClick}
                    className="flex-1 bg-gray-100 hover:bg-gray-200 text-gray-700 py-2 px-4 rounded-md text-sm font-medium transition-colors"
                >
                    History
                </button>
            </div>
        </div>
    );
};

export default SubscriptionCard;