import api from './api';

export interface KycVerificationResult {
    success: boolean;
    message: string;
}

export interface PersonalInfo {
    firstName: string;
    lastName: string;
    dateOfBirth: string;
    documentNumber: string;
    nationality: string;
}

export interface KycSessionRequest {
    userId: string;
    verificationLevel: string;
}

export interface KycVerificationRequest {
    userId: string;
    sessionId: string;
    verificationLevel: string;
    data: Record<string, any>;
}

const kycService = {
    // Create a new KYC session
    createSession: async (request: KycSessionRequest): Promise<string> => {
        const response = await api.safeRequest('post', '/kyc/session', request);
        if (response.data.success != true)
            throw "Failed to create KYC session";
        return response.data.data;
    },

    // Submit verification data
    submitVerification: async (request: KycVerificationRequest): Promise<KycVerificationResult> => {
        const response = await api.safeRequest<KycVerificationResult>('post', '/kyc/verify', request);
        return response.data;
    }
};

export default kycService;