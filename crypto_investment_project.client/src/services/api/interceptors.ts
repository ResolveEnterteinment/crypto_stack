// src/services/api/interceptors.ts
import { AxiosError, AxiosInstance, InternalAxiosRequestConfig } from 'axios';
import { API_CONFIG } from './config';
import { metricsTracker } from './metricsTracker';
import { tokenManager } from './tokenManager';

export class InterceptorManager {
    private static requestIdCounter = 0;

    static setupInterceptors(axiosInstance: AxiosInstance): void {
        this.setupRequestInterceptor(axiosInstance);
        this.setupResponseInterceptor(axiosInstance);
    }

    private static setupRequestInterceptor(axiosInstance: AxiosInstance): void {
        axiosInstance.interceptors.request.use(
            (config: InternalAxiosRequestConfig) => {
                // Track request
                metricsTracker.recordRequest();

                // Generate request ID
                const requestId = this.generateRequestId();
                config.headers.set(API_CONFIG.HEADERS.REQUEST_ID, requestId);

                // Add auth token if available and not skipped
                if (!config.headers.get('X-Skip-Auth')) {
                    const token = tokenManager.getAccessToken();
                    if (token) {
                        config.headers.set(API_CONFIG.AUTH.HEADER, `${API_CONFIG.AUTH.SCHEME} ${token}`);
                    }
                }

                // Add CSRF token for mutation requests
                const isMutation = ['post', 'put', 'patch', 'delete'].includes(config.method?.toLowerCase() || '');
                if (isMutation && !config.headers.get('X-Skip-Csrf')) {
                    const csrfToken = tokenManager.getCsrfToken();
                    if (csrfToken) {
                        config.headers.set(API_CONFIG.CSRF.HEADER, csrfToken);
                    }
                }

                // Add KYC session if available
                const kycSessionId = tokenManager.getKycSessionId();
                if (kycSessionId && !config.headers.get(API_CONFIG.HEADERS.KYC_SESSION)) {
                    config.headers.set(API_CONFIG.HEADERS.KYC_SESSION, kycSessionId);
                }

                // Add idempotency key if provided or generate for mutations
                const idempotencyKey = config.headers.get(API_CONFIG.HEADERS.IDEMPOTENCY_KEY);
                if (!idempotencyKey && isMutation && config.data) {
                    // Auto-generate idempotency key for mutations with data
                    const autoKey = this.generateIdempotencyKey(config);
                    config.headers.set(API_CONFIG.HEADERS.IDEMPOTENCY_KEY, autoKey);
                }

                // Add performance tracking
                (config as any)._startTime = Date.now();
                (config as any)._requestId = requestId;

                return config;
            },
            (error: AxiosError) => Promise.reject(error)
        );
    }

    private static setupResponseInterceptor(axiosInstance: AxiosInstance): void {
        axiosInstance.interceptors.response.use(
            (response) => {
                // Log slow requests in development
                if (import.meta.env.DEV) {
                    const duration = Date.now() - ((response.config as any)._startTime || 0);
                    if (duration > API_CONFIG.PERFORMANCE.SLOW_REQUEST_THRESHOLD) {
                        console.warn(`⚠️ Slow request: ${response.config.url} took ${duration}ms`);
                    }
                }

                return response;
            },
            async (error: AxiosError) => {
                const originalRequest = error.config as InternalAxiosRequestConfig & {
                    _retry?: boolean;
                    _retryCount?: number;
                };

                // Handle cancellation
                if (error.code === 'ERR_CANCELED' || error.message === 'canceled') {
                    metricsTracker.recordCancellation();
                    return Promise.reject(error);
                }

                // Handle 401 errors with token refresh
                if (error.response?.status === 401 && !originalRequest._retry && originalRequest.url !== API_CONFIG.CSRF.REFRESH_URL) {
                    originalRequest._retry = true;

                    try {
                        const refreshed = await this.refreshAuthToken(axiosInstance);
                        if (refreshed) {
                            // Retry original request with new token
                            const newToken = tokenManager.getAccessToken();
                            if (newToken) {
                                originalRequest.headers.set(API_CONFIG.AUTH.HEADER, `${API_CONFIG.AUTH.SCHEME} ${newToken}`);
                            }
                            return axiosInstance(originalRequest);
                        }
                    } catch (refreshError) {
                        // Refresh failed, redirect to login
                        tokenManager.clearAll();
                        window.location.href = '/login';
                        return Promise.reject(refreshError);
                    }
                }

                // Handle CSRF token errors
                if (error.response?.status === 403 && (error.response?.data as any)?.message?.includes('CSRF')) {
                    if (!originalRequest._retry) {
                        originalRequest._retry = true;

                        try {
                            await this.refreshCsrfToken(axiosInstance);
                            const newCsrfToken = tokenManager.getCsrfToken();
                            if (newCsrfToken) {
                                originalRequest.headers.set(API_CONFIG.CSRF.HEADER, newCsrfToken);
                            }
                            return axiosInstance(originalRequest);
                        } catch (csrfError) {
                            return Promise.reject(csrfError);
                        }
                    }
                }

                // Handle retry for specific status codes
                const shouldRetry = error.response && API_CONFIG.RETRY.STATUS_CODES.includes(error.response.status);
                const retryCount = originalRequest._retryCount || 0;

                if (shouldRetry && retryCount < API_CONFIG.RETRY.ATTEMPTS) {
                    originalRequest._retryCount = retryCount + 1;
                    metricsTracker.recordRetry();

                    const delay = Math.min(
                        API_CONFIG.RETRY.DELAY * Math.pow(API_CONFIG.RETRY.BACKOFF_MULTIPLIER, retryCount),
                        API_CONFIG.RETRY.MAX_DELAY
                    );

                    console.log(`[Retry] Attempt ${retryCount + 1}/${API_CONFIG.RETRY.ATTEMPTS} after ${delay}ms`);

                    await new Promise(resolve => setTimeout(resolve, delay));
                    return axiosInstance(originalRequest);
                }

                return Promise.reject(error);
            }
        );
    }

    private static async refreshAuthToken(axiosInstance: AxiosInstance): Promise<boolean> {
        // Check if refresh is already in progress
        let refreshPromise = tokenManager.getRefreshPromise();

        if (refreshPromise) {
            return refreshPromise;
        }

        // Create new refresh promise
        refreshPromise = this.performTokenRefresh(axiosInstance);
        tokenManager.setRefreshPromise(refreshPromise);

        try {
            const result = await refreshPromise;
            return result;
        } finally {
            tokenManager.setRefreshPromise(null);
        }
    }

    private static async performTokenRefresh(axiosInstance: AxiosInstance): Promise<boolean> {
        try {
            const currentToken = tokenManager.getAccessToken();
            if (!currentToken) return false;

            const response = await axiosInstance.post('/v1/auth/refresh-token', null, {
                headers: {
                    'Authorization': `Bearer ${currentToken}`,
                    'X-Skip-Auth': 'true' // Skip auth interceptor for this request
                }
            });

            if (response.data.success && response.data.accessToken) {
                tokenManager.setAccessToken(response.data.accessToken);
                return true;
            }

            return false;
        } catch (error) {
            console.error('Token refresh failed:', error);
            return false;
        }
    }

    private static async refreshCsrfToken(axiosInstance: AxiosInstance): Promise<void> {
        try {
            const response = await axiosInstance.get(API_CONFIG.CSRF.REFRESH_URL, {
                headers: { 'X-Skip-Csrf': 'true' }
            });

            if (response.data.token) {
                tokenManager.setCsrfToken(response.data.token);
            }
        } catch (error) {
            console.error('CSRF token refresh failed:', error);
            throw error;
        }
    }

    private static generateRequestId(): string {
        const timestamp = Date.now();
        const counter = ++this.requestIdCounter;
        const random = Math.random().toString(36).substring(2, 8);
        return `req_${timestamp}_${counter}_${random}`;
    }

    private static generateIdempotencyKey(config: InternalAxiosRequestConfig): string {
        const method = config.method || 'unknown';
        const url = config.url || '';
        const data = config.data ? JSON.stringify(config.data) : '';
        const timestamp = Date.now();

        // Create a deterministic key based on request content
        const baseKey = `${method}-${url}-${data}`;
        const hash = this.simpleHash(baseKey);

        return `auto_${timestamp}_${hash}`;
    }

    private static simpleHash(str: string): string {
        let hash = 0;
        for (let i = 0; i < str.length; i++) {
            const char = str.charCodeAt(i);
            hash = ((hash << 5) - hash) + char;
            hash = hash & hash;
        }
        return Math.abs(hash).toString(36);
    }
}