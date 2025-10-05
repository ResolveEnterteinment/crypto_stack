// src/components/KYC/index.ts - KYC Components Export Index
export { default as KycVerificationReports } from './KycVerificationReports';
export { default as KycAdminPanel } from './KycAdminPanel';

// Constants
export const KYC_LEVELS = {
    NONE: 'NONE',
    BASIC: 'BASIC',
    STANDARD: 'STANDARD',
    ADVANCED: 'ADVANCED',
    ENHANCED: 'ENHANCED',
} as const;

export const KYC_STATUS = {
    NOT_STARTED: 'NOT_STARTED',
    PENDING: 'PENDING',
    APPROVED: 'APPROVED',
    REJECTED: 'REJECTED',
    NEEDS_REVIEW: 'NEEDS_REVIEW',
    BLOCKED: 'BLOCKED'
} as const;

export const DOCUMENT_TYPES = {
    PASSPORT: 'passport',
    DRIVERS_LICENSE: 'drivers_license',
    NATIONAL_ID: 'national_id',
    UTILITY_BILL: 'utility_bill',
    BANK_STATEMENT: 'bank_statement'
} as const;

// KYC Status Types
export interface KycStatusData {
    status: 'NOT_STARTED' | 'PENDING' | 'APPROVED' | 'REJECTED' | 'NEEDS_REVIEW' | 'BLOCKED';
    verificationLevel: 'NONE' | 'BASIC' | 'STANDARD' | 'ADVANCED' | 'ENHANCED';
    submittedAt?: string;
    updatedAt?: string;
    verifiedAt?: string;
    expiresAt?: string;
    nextSteps: string[];
}

// KYC Document Types
export interface KycDocument {
    id: string;
    type: 'passport' | 'drivers_license' | 'national_id' | 'utility_bill' | 'bank_statement';
    status: 'UPLOADED' | 'PROCESSING' | 'VERIFIED' | 'REJECTED';
    fileName: string;
    uploadedAt: string;
    verificationScore?: number;
    issues?: string[];
}

// KYC Session Types
export interface KycSession {
    id: string;
    userId: string;
    status: 'ACTIVE' | 'EXPIRED' | 'COMPLETED';
    verificationLevel: string;
    createdAt: string;
    expiresAt: string;
}

// KYC Verification Result
export interface KycVerificationResult {
    success: boolean;
    level: string;
    status: string;
    submittedAt: string;
    nextSteps: string[];
    message?: string;
}

// Utility functions
export const getKycLevelColor = (level: string) => {
    switch (level) {
        case KYC_LEVELS.BASIC:
            return 'green';
        case KYC_LEVELS.STANDARD:
            return 'blue';
        case KYC_LEVELS.ADVANCED:
            return 'purple';
        case KYC_LEVELS.ENHANCED:
            return 'yellow';
        default:
            return 'default';
    }
};

export const getKycStatusColor = (status: string) => {
    switch (status) {
        case KYC_STATUS.APPROVED:
            return 'success';
        case KYC_STATUS.PENDING:
            return 'processing';
        case KYC_STATUS.NEEDS_REVIEW:
            return 'warning';
        case KYC_STATUS.REJECTED:
        case KYC_STATUS.BLOCKED:
            return 'error';
        default:
            return 'default';
    }
};

export const getKycLevelValue = (level: string): number => {
    switch (level) {
        case KYC_LEVELS.BASIC:
            return 1;
        case KYC_LEVELS.STANDARD:
            return 2;
        case KYC_LEVELS.ADVANCED:
            return 3;
        case KYC_LEVELS.ENHANCED:
            return 4;
        default:
            return 0;
    }
};

export const getKycLevelName = (level: number): string => {
    switch (level) {
        case 1:
            return KYC_LEVELS.BASIC;
        case 2:
            return KYC_LEVELS.STANDARD;
        case 3:
            return KYC_LEVELS.ADVANCED;
        case 4:
            return KYC_LEVELS.ENHANCED;
        default:
            return KYC_LEVELS.NONE;
    }
};

export const canUpgradeKycLevel = (currentLevel: string, targetLevel: string): boolean => {
    return getKycLevelValue(targetLevel) > getKycLevelValue(currentLevel);
};