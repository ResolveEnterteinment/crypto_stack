import { KycVerificationResult } from '../components/KYC';
import { CreateSessionRequest, KycStatus, LiveDocumentCaptureRequest, LiveCaptureResponse, LiveSelfieCaptureRequest, VerificationSubmission, DocumentUploadResponse } from '../types/kyc';
import api from './api';

// API endpoints
const ENDPOINTS = {
    GET_STATUS: () => `/v1/kyc/status`,
    GET_SESSION: () => `/v1/kyc/session`,
    CREATE_SESSION: () => `/v1/kyc/session`,
    LIVE_DOCUMENT_CAPTURE: () => `/v1/kyc/document/live-capture`,
    LIVE_SELFIE_CAPTURE: () => `/v1/kyc/selfie/live-capture`,
    SESSION_VALIDATE: () => `/v1/kyc/session/validate`,
    SESSION_INVALIDATE: () => `/v1/kyc/session`,
    VERIFY: () => `/v1/kyc/verify`,
    UPLOAD_DOCUMENT: () => `/v1/kyc/document/upload`,
    GET_BY_USER_AND_ASSET: (userId: string, ticker: string) => `/kyc/admin/get/user/${userId}/asset/${ticker}`,
    HEALTH_CHECK: '/v1/kyc/health'
} as const;

class KycService {
    private currentSessionId: string | null = null;
       

    /**
     * Get current KYC status for the authenticated user
     */
    async getStatus(): Promise<KycStatus> {
        const response = await api.get<KycStatus>(ENDPOINTS.GET_STATUS());

        if (response == null || response.data == null || !response.success) {
            throw new Error(response.message || 'Failed to get KYC status');
        }

        return response.data;
    }

    /**
     * Create a new KYC verification session
     */
    async createSession(request: CreateSessionRequest): Promise<string> {
        const response = await api.post<string>(ENDPOINTS.CREATE_SESSION(), request);

        if (response == null || response.data == null || !response.success) {
            throw new Error(response.message || 'Failed to create KYC session');
        }

        var sessionId = response.data;

        // Store session ID for subsequent requests
        this.setSession(sessionId);

        return sessionId;
    }

    /**
     * Submit live capture data for document verification
     */
    async submitLiveDocumentCapture(request: LiveDocumentCaptureRequest): Promise<LiveCaptureResponse> {
        if (!this.currentSessionId) {
            throw new Error('No active KYC session. Please restart the verification process.');
        }

        // Ensure request includes current session
        request.sessionId = this.currentSessionId;

        try {
            const response = await api.post<LiveCaptureResponse>(ENDPOINTS.LIVE_DOCUMENT_CAPTURE(), request);

            if (response == null || response.data == null || !response.success)
            {
                throw new Error(response.message || 'Live capture submission failed');
            }

            return response.data;
        } catch (error) {
            console.error('Live capture submission error:', error);
            throw error;
        }
    }

    /**
     * Submit live capture data for selfie verification
     */
    async submitLiveSelfieCapture(request: LiveSelfieCaptureRequest): Promise<LiveCaptureResponse> {
        if (!this.currentSessionId) {
            throw new Error('No active KYC session. Please restart the verification process.');
        }

        // Ensure request includes current session
        request.sessionId = this.currentSessionId;

        try {
            console.log("kycService::submitLiveCapture => request: ", request);

            const response = await api.post<LiveCaptureResponse>(ENDPOINTS.LIVE_SELFIE_CAPTURE(), request);

            if (response == null || response.data == null || !response.success) {
                throw new Error(response.message || 'Live selfie capture submission failed');
            }

            return response.data;
        } catch (error) {
            console.error('Live selfie capture submission error:', error);
            throw error;
        }
    }

    /**
     * Set the current session and configure headers
     */
    private setSession(sessionId: string): void {
        this.currentSessionId = sessionId;
        localStorage.setItem('kyc_session_id', sessionId);

        // Configure API service to include session header
        api.setKycSession(sessionId);
    }

    /**
     * Clear the current session
     */
    private clearSession(): void {
        this.currentSessionId = null;
        localStorage.removeItem('kyc_session_id');
        api.clearKycSession();
    }

    /**
     * Restore session from localStorage
     */
    restoreSession(): string | null {
        const sessionId = localStorage.getItem('kyc_session_id');
        if (sessionId) {
            this.currentSessionId = sessionId;
            api.setKycSession(sessionId);
        }
        return sessionId;
    }

    /**
     * Validate current session
     */
    async validateSession(): Promise<boolean> {
        if (!this.currentSessionId) {
            return false;
        }

        try {
            const response = await api.get<boolean>(ENDPOINTS.SESSION_VALIDATE());

            if (response == null || response.data == null || !response.success) {
                throw new Error(response.message || 'Failed to validate KYC session');
            }

            return response.data;
        } catch (error) {
            this.clearSession();
            return false;
        }
    }

    /**
     * Submit verification with proper session handling
     */
    async submitVerification(submission: VerificationSubmission): Promise<KycVerificationResult> {
        if (!this.currentSessionId) {
            throw new Error('No active KYC session. Please restart the verification process.');
        }

        // Ensure submission includes current session
        submission.sessionId = this.currentSessionId;

        try {
            const response = await api.post<KycVerificationResult>(ENDPOINTS.VERIFY(), submission);

            if (response == null || response.data == null || !response.success) {
                throw new Error(response.message || 'Failed to verify KYC submisson');
            }

            // Clear session after successful verification
            this.clearSession();

            return response.data;
        } catch (error) {
            // Don't clear session on verification failure - user can retry
            throw error;
        }
    }

    /**
     * Upload document for KYC verification
     */
    async uploadDocument(sessionId: string, file: File, documentType: string): Promise<DocumentUploadResponse> {
        const formData = new FormData();
        formData.append('file', file);
        formData.append('sessionId', sessionId);
        formData.append('documentType', documentType);

        try {
            const response = await api.post<DocumentUploadResponse>(ENDPOINTS.UPLOAD_DOCUMENT(), formData, {
                headers: {
                    'Content-Type': 'multipart/form-data'
                }
            });

            if (response == null || response.data == null || !response.success) {
                throw new Error(response.message || 'Failed to upload document');
            }

            return response.data;
        } catch (error) {
            console.error('Document upload error:', error);
            throw error;
        }
    }

    /**
     * Invalidate current session
     */
    async invalidateSession(reason: string = 'User requested'): Promise<boolean | undefined > {
        if (!this.currentSessionId) {
            return;
        }

        try {
            var response = await api.delete<{ invalidated: boolean }>(ENDPOINTS.SESSION_INVALIDATE(), reason);

            if (response == null || response.data == null || !response.success) {
                throw new Error(response.message || 'Failed to invalidate session');
            }

            if (!response.success) {
                return false;
            }

            return response.data.invalidated;
        } catch (error) {
            console.error('Session invalidation error:', error);
            throw error;
        }
        finally {
            this.clearSession();
        }
    }
}

// Export singleton instance
const kycService = new KycService();
export default kycService;