// src/types/withdrawal.ts
export interface WithdrawalLimits {
    kycLevel: string;
    dailyLimit: number;
    monthlyLimit: number;
    dailyRemaining: number;
    monthlyRemaining: number;
    dailyUsed: number;
    monthlyUsed: number;
    periodResetDate: string;
}

export interface Withdrawal {
    id: string;
    userId: string;
    requestedBy: string;
    amount: number;
    currency: string;
    withdrawalMethod: string;
    withdrawalAddress: string;
    status: string;
    createdAt: string;
    processedAt?: string;
    transactionHash?: string;
    comments?: string;
    kycLevelAtTime?: string;
    additionalDetails?: {
        bankName?: string;
        accountHolder?: string;
        accountNumber?: string;
        routingNumber?: string;
        memo?: string;
        network?: string;
    };
}

export interface WithdrawalRequest {
    userId: string,
    amount: number,
    currency: string,
    withdrawalMethod: string,
    withdrawalAddress: string,
    additionalDetails?: {
        bankName?: string;
        accountHolder?: string;
        accountNumber?: string;
        routingNumber?: string;
        network?: string;
        memo?: string;
        [key: string]: any;
    };
}

export interface WithdrawalFormValues {
    amount: number;
    currency: string;
    withdrawalMethod: string;
    withdrawalAddress: string;
    bankName?: string;
    accountHolder?: string;
    accountNumber?: string;
    routingNumber?: string;
}

export interface NetworkDto {
    name: string;
    tokenStandard: string;
    requiresMemo: boolean;
}

export interface WithdrawalResponse {
    id: string;
    amount: number;
    currency: string;
    status: string;
    createdAt: string;
    withdrawalMethod: string;
    withdrawalAddress: string;
    transactionHash?: string;
}