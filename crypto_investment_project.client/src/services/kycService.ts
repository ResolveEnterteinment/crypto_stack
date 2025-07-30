import { KycVerificationResult } from '../components/KYC';
import { CreateSessionRequest, KycStatus, LiveDocumentCaptureRequest, LiveCaptureResponse, LiveSelfieCaptureRequest, VerificationSubmission } from '../types/kyc';
import api, { ClientResponse } from './api';

class KycService {
    private apiUrl = '/v1/kyc';
    private currentSessionId: string | null = null;

    private handleResponse<T>(response: ClientResponse<T>): T {
        if (!response.success) {
            // Handle session expiry specifically
            if (response.statusCode === 403 && response.message?.includes('ACTIVE_SESSION_REQUIRED')) {
                this.clearSession();
                throw new Error('KYC session expired. Please restart the verification process.');
            }

            throw new Error(response.message || `HTTP error! status: ${response.statusCode}`);
        }

        const data: T = response.data;
        if (!data) {
            throw new Error(response.message || 'Request failed');
        }

        return data;
    }

    /**
     * Get current KYC status for the authenticated user
     */
    async getStatus(): Promise<KycStatus> {
        const response: ClientResponse<KycStatus> = await api.safeRequest('get', `${this.apiUrl}/status`);
        return this.handleResponse<KycStatus>(response);
    }

    /**
     * Create a new KYC verification session
     */
    async createSession(request: CreateSessionRequest): Promise<string> {
        const response = await api.safeRequest<string>('post', `${this.apiUrl}/session`, request);
        const sessionId = await this.handleResponse(response);

        // Store session ID for subsequent requests
        this.setSession(sessionId);

        return sessionId;
    }

    /**
     * Submit live capture data for document verification
     */
    async submitLiveDocumentCapture(request: LiveDocumentCaptureRequest): Promise<ClientResponse<LiveCaptureResponse>> {
        if (!this.currentSessionId) {
            throw new Error('No active KYC session. Please restart the verification process.');
        }

        // Ensure request includes current session
        request.sessionId = this.currentSessionId;

        try {
            console.log("kycService::submitLiveCapture => request: ", request);

            const response = await api.safeRequest<LiveCaptureResponse>('post', `${this.apiUrl}/document/live-capture`, request);

            if (!response.success) {
                throw new Error(response.message || 'Live capture submission failed');
            }

            return response;
        } catch (error) {
            console.error('Live capture submission error:', error);
            throw error;
        }
    }

    /**
     * Submit live capture data for selfie verification
     */
    async submitLiveSelfieCapture(request: LiveSelfieCaptureRequest): Promise<ClientResponse<LiveCaptureResponse>> {
        if (!this.currentSessionId) {
            throw new Error('No active KYC session. Please restart the verification process.');
        }

        // Ensure request includes current session
        request.sessionId = this.currentSessionId;

        try {
            console.log("kycService::submitLiveCapture => request: ", request);

            const response = await api.safeRequest<LiveCaptureResponse>('post', `${this.apiUrl}/selfie/live-capture`, request);

            if (!response.success) {
                throw new Error(response.message || 'Live selfie capture submission failed');
            }

            return response;
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
            const response = await api.safeRequest('get', `${this.apiUrl}/session/validate`);
            return response.success;
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
            const response = await api.safeRequest<KycVerificationResult>('post', `${this.apiUrl}/verify`, submission);
            const result = this.handleResponse<KycVerificationResult>(response);

            // Clear session after successful verification
            this.clearSession();

            return result;
        } catch (error) {
            // Don't clear session on verification failure - user can retry
            throw error;
        }
    }

    /**
     * Upload document for KYC verification
     */
    async uploadDocument(sessionId: string, file: File, documentType: string): Promise<any> {
        const formData = new FormData();
        formData.append('file', file);
        formData.append('sessionId', sessionId);
        formData.append('documentType', documentType);

        try {
            const response = await api.safeRequest('post', `${this.apiUrl}/document/upload`, formData, {
                headers: {
                    'Content-Type': 'multipart/form-data'
                }
            });

            return this.handleResponse<any>(response);
        } catch (error) {
            console.error('Document upload error:', error);
            throw error;
        }
    }

    /**
     * Invalidate current session
     */
    async invalidateSession(reason: string = 'User requested'): Promise<void> {
        if (!this.currentSessionId) {
            return;
        }

        try {
            await api.safeRequest('delete', `${this.apiUrl}/session`, { reason });
        } finally {
            this.clearSession();
        }
    }
}

// Export singleton instance
const kycService = new KycService();
export default kycService;