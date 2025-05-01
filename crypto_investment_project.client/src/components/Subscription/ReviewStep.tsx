import IAllocation from '../../interfaces/IAllocation';

// Define interval display mapping
const INTERVAL_DISPLAY = {
    ONCE: 'One-time payment',
    DAILY: 'Daily',
    WEEKLY: 'Weekly',
    MONTHLY: 'Monthly',
    YEARLY: 'Yearly'
};

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
    DEFAULT: '#6B7280' // Gray
};

const ReviewStep = ({ formData }) => {
    const { interval, amount, currency, endDate, allocations } = formData;

    // Format date for display
    const formatDate = (date: Date) => {
        if (!date) return 'Ongoing (until cancelled)';
        return new Date(date).toLocaleDateString('en-US', {
            year: 'numeric',
            month: 'long',
            day: 'numeric'
        });
    };

    // Calculate estimated annual investment (excluding one-time)
    const calculateAnnualAmount = () => {
        if (interval === 'ONCE') return amount;

        const multipliers = {
            'DAILY': 365,
            'WEEKLY': 52,
            'MONTHLY': 12,
            'YEARLY': 1
        };

        return amount * (multipliers[interval] || 0);
    };

    // Get estimated annual investment
    const annualAmount = calculateAnnualAmount();

    // Get color for asset based on ticker
    const getAssetColor = (ticker) => {
        return ASSET_COLORS[ticker] || ASSET_COLORS.DEFAULT;
    };

    // Calculate fees
    const platformFee = amount * 0.01; // 1% platform fee
    const stripeFee = (amount * 0.029) + 0.30; // 2.9% + $0.30 Stripe fee
    const totalFees = platformFee + stripeFee;
    const netInvestment = amount - totalFees;

    return (
        <div className="space-y-8">
            <div>
                <h2 className="text-2xl font-semibold text-gray-800 mb-4">Review Your Investment Plan</h2>
                <p className="text-gray-600 mb-6">
                    Please review your investment plan details before confirming.
                </p>
            </div>

            {/* Plan Details Summary */}
            <div className="bg-gray-50 p-6 rounded-lg">
                <h3 className="text-lg font-medium text-gray-800 mb-4">Plan Details</h3>
                <div className="grid grid-cols-2 gap-x-8 gap-y-4">
                    <div>
                        <p className="text-sm text-gray-500">Plan Type</p>
                        <p className="font-medium">{INTERVAL_DISPLAY[interval]}</p>
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

                    <div>
                        <p className="text-sm text-gray-500">Payment Method</p>
                        <p className="font-medium">Credit/Debit Card</p>
                    </div>
                    <div>
                        <p className="text-sm text-gray-500">Start Date</p>
                        <p className="font-medium">Immediately after payment</p>
                    </div>
                </div>
            </div>

            {/* Asset Allocation Summary */}
            <div className="bg-gray-50 p-6 rounded-lg">
                <h3 className="text-lg font-medium text-gray-800 mb-4">Asset Allocation</h3>

                {/* Visual representation of allocation */}
                <div className="mb-4 h-8 bg-gray-200 rounded-full overflow-hidden flex">
                    {allocations.map((allocation: IAllocation) => (
                        <div
                            key={allocation.assetId}
                            style={{
                                width: `${allocation.percentAmount}%`,
                                backgroundColor: getAssetColor(allocation.ticker)
                            }}
                            className="h-full"
                        />
                    ))}
                </div>

                {/* Allocation details */}
                <div className="space-y-3 mt-4">
                    {allocations.map((allocation: IAllocation) => (
                        <div key={allocation.assetId} className="flex justify-between items-center border-b border-gray-200 pb-2">
                            <div className="flex items-center">
                                <div
                                    className="w-4 h-4 rounded-full mr-2"
                                    style={{ backgroundColor: getAssetColor(allocation.ticker) }}
                                />
                                <span className="font-medium">{allocation.assetName} ({allocation.ticker})</span>
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

            {/* Payment & Fee Summary */}
            <div className="bg-gray-50 p-6 rounded-lg">
                <h3 className="text-lg font-medium text-gray-800 mb-4">Payment Summary</h3>

                <div className="space-y-2">
                    <div className="flex justify-between">
                        <span>Invested Amount</span>
                        <span className="font-medium">${amount.toFixed(2)}</span>
                    </div>
                    <div className="flex justify-between text-gray-500">
                        <span>Platform Fee (1%)</span>
                        <span>-${platformFee.toFixed(2)}</span>
                    </div>
                    <div className="flex justify-between text-gray-500">
                        <span>Payment Processing Fee (2.9% + $0.30)</span>
                        <span>-${stripeFee.toFixed(2)}</span>
                    </div>
                    <div className="flex justify-between font-medium pt-2 border-t border-gray-200">
                        <span>Net Investment Amount</span>
                        <span>${netInvestment.toFixed(2)}</span>
                    </div>
                </div>

                <p className="mt-4 text-sm text-gray-500">
                    By proceeding, you agree to our Terms of Service and Privacy Policy. You authorize regular charges to your payment method
                    {interval !== 'ONCE' ? ' according to your selected plan' : ''}. You can cancel anytime.
                </p>
            </div>
        </div>
    );
};

export default ReviewStep;