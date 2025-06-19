// src/utils/feeCalculation.ts
// Fee calculation utility for investment amounts

export interface FeeBreakdown {
    grossAmount: number;
    platformFee: number;
    stripeFee: number;
    totalFees: number;
    netInvestmentAmount: number;
}

export interface FeeConfiguration {
    platformFeeRate: number; // e.g., 0.01 for 1%
    stripeFeeRate: number;   // e.g., 0.029 for 2.9%
    stripeFixedFee: number;  // e.g., 0.30 for $0.30
}

// Default fee configuration
const DEFAULT_FEE_CONFIG: FeeConfiguration = {
    platformFeeRate: 0.01,    // 1% platform fee
    stripeFeeRate: 0.029,     // 2.9% Stripe percentage fee
    stripeFixedFee: 0.30      // $0.30 Stripe fixed fee
};

/**
 * Calculate comprehensive fee breakdown for an investment amount
 */
export const calculateFeeBreakdown = (
    grossAmount: number, 
    feeConfig: FeeConfiguration = DEFAULT_FEE_CONFIG
): FeeBreakdown => {
    // Input validation
    if (grossAmount < 0) {
        throw new Error('Gross amount must be non-negative');
    }

    // Calculate individual fees
    const platformFee = grossAmount * feeConfig.platformFeeRate;
    const stripeFee = (grossAmount * feeConfig.stripeFeeRate) + feeConfig.stripeFixedFee;
    const totalFees = platformFee + stripeFee;
    const netInvestmentAmount = grossAmount - totalFees;

    // Validate that net amount is positive
    if (netInvestmentAmount <= 0) {
        throw new Error(`Investment amount too low. Minimum required: $${(totalFees + 0.01).toFixed(2)}`);
    }

    return {
        grossAmount,
        platformFee: Math.round(platformFee * 100) / 100, // Round to 2 decimals
        stripeFee: Math.round(stripeFee * 100) / 100,
        totalFees: Math.round(totalFees * 100) / 100,
        netInvestmentAmount: Math.round(netInvestmentAmount * 100) / 100
    };
};

/**
 * Calculate the net investment amount after all fees
 */
export const calculateNetInvestmentAmount = (
    grossAmount: number,
    feeConfig: FeeConfiguration = DEFAULT_FEE_CONFIG
): number => {
    const breakdown = calculateFeeBreakdown(grossAmount, feeConfig);
    return breakdown.netInvestmentAmount;
};

/**
 * Calculate the minimum gross amount needed to achieve a target net investment
 */
export const calculateGrossAmountForTargetNet = (
    targetNetAmount: number,
    feeConfig: FeeConfiguration = DEFAULT_FEE_CONFIG
): number => {
    if (targetNetAmount <= 0) {
        throw new Error('Target net amount must be positive');
    }

    // Formula: gross = (net + fixed_fee) / (1 - platform_rate - stripe_rate)
    const combinedPercentageRate = feeConfig.platformFeeRate + feeConfig.stripeFeeRate;
    const grossAmount = (targetNetAmount + feeConfig.stripeFixedFee) / (1 - combinedPercentageRate);
    
    return Math.ceil(grossAmount * 100) / 100; // Round up to ensure we meet target
};

/**
 * Validate that an investment amount meets minimum requirements
 */
export const validateInvestmentAmount = (
    grossAmount: number,
    minimumNetAmount: number = 10, // $10 minimum net investment
    feeConfig: FeeConfiguration = DEFAULT_FEE_CONFIG
): { isValid: boolean; errorMessage?: string; minimumGrossRequired?: number } => {
    try {
        const breakdown = calculateFeeBreakdown(grossAmount, feeConfig);
        
        if (breakdown.netInvestmentAmount < minimumNetAmount) {
            const minimumGrossRequired = calculateGrossAmountForTargetNet(minimumNetAmount, feeConfig);
            return {
                isValid: false,
                errorMessage: `Net investment amount ($${breakdown.netInvestmentAmount.toFixed(2)}) is below minimum ($${minimumNetAmount.toFixed(2)})`,
                minimumGrossRequired
            };
        }

        return { isValid: true };
    } catch (error) {
        return {
            isValid: false,
            errorMessage: error instanceof Error ? error.message : 'Invalid investment amount'
        };
    }
};

/**
 * Format fee breakdown for display
 */
export const formatFeeBreakdown = (breakdown: FeeBreakdown): string => {
    return `
Investment: $${breakdown.grossAmount.toFixed(2)}
Platform Fee: -$${breakdown.platformFee.toFixed(2)} (1%)
Payment Fee: -$${breakdown.stripeFee.toFixed(2)} (2.9% + $0.30)
Total Fees: -$${breakdown.totalFees.toFixed(2)}
Net Investment: $${breakdown.netInvestmentAmount.toFixed(2)}
    `.trim();
};

/**
 * Get fee configuration from environment or use defaults
 */
export const getFeeConfiguration = (): FeeConfiguration => {
    // In a real app, you might load this from environment variables or API
    return {
        platformFeeRate: parseFloat(process.env.REACT_APP_PLATFORM_FEE_RATE || '0.01'),
        stripeFeeRate: parseFloat(process.env.REACT_APP_STRIPE_FEE_RATE || '0.029'),
        stripeFixedFee: parseFloat(process.env.REACT_APP_STRIPE_FIXED_FEE || '0.30')
    };
};

// Example usage and testing
export const examples = {
    // Example 1: $100 investment
    example100: calculateFeeBreakdown(100),
    // Result: { grossAmount: 100, platformFee: 1, stripeFee: 3.20, totalFees: 4.20, netInvestmentAmount: 95.80 }

    // Example 2: What gross amount needed for $100 net?
    grossFor100Net: calculateGrossAmountForTargetNet(100),
    // Result: ~108.86 (so user needs to invest ~$109 to get $100 net)

    // Example 3: Validate $50 investment
    validate50: validateInvestmentAmount(50),
    // Result: { isValid: true } (since $47.85 net > $10 minimum)

    // Example 4: Validate $10 investment (probably too low)
    validate10: validateInvestmentAmount(10),
    // Result: { isValid: false, errorMessage: "...", minimumGrossRequired: ... }
};

export default {
    calculateFeeBreakdown,
    calculateNetInvestmentAmount,
    calculateGrossAmountForTargetNet,
    validateInvestmentAmount,
    formatFeeBreakdown,
    getFeeConfiguration,
    DEFAULT_FEE_CONFIG
};