// Interfaces for the component
export interface VerificationData {
    id: string;
    userId: string;
    email: string;
    provider: string;
    status: string;
    verificationLevel: string;
    isHighRisk: boolean;
    isPep: boolean;
    country: string;
    submittedAt: string;
    completedAt?: string;
    processingTime?: number | null;
}