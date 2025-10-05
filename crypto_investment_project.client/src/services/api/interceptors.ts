// Fixed interceptors.ts with proper TypeScript type handling
// src/services/api/interceptors.ts

import { AxiosError, AxiosInstance, InternalAxiosRequestConfig } from 'axios';
import { API_CONFIG } from './config';
import { metricsTracker } from './metricsTracker';
import { tokenManager } from './tokenManager';

// Define extended request config interface
interface ExtendedRequestConfig extends InternalAxiosRequestConfig {
    _retry?: boolean;
    _retryCount?: number;
    _startTime?: number;
    _requestId?: string;
}

export class InterceptorManager {
    private static requestIdCounter = 0;
    private static isRefreshing = false;
    private static refreshSubscribers: Array<(token: string) => void> = [];

    static setupInterceptors(axiosInstance: AxiosInstance): void {
        this.setupRequestInterceptor(axiosInstance);
        this.setupResponseInterceptor(axiosInstance);
    }

    // ✅ FIXED: Request interceptor with proper typing
    private static setupRequestInterceptor(axiosInstance: AxiosInstance): void {
        axiosInstance.interceptors.request.use(
            async (config: InternalAxiosRequestConfig) => {
                metricsTracker.recordRequest();

                // Generate request ID
                const requestId = this.generateRequestId();
                config.headers.set(API_CONFIG.HEADERS.REQUEST_ID, requestId);

                // ✅ Use type assertion for custom properties
                const extendedConfig = config as ExtendedRequestConfig;
                extendedConfig._startTime = Date.now();
                extendedConfig._requestId = requestId;

                // ✅ ENHANCED: Proactive token management
                if (!config.headers.get('X-Skip-Auth')) {
                    const token = await this.getValidToken(axiosInstance);
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
                    const autoKey = this.generateIdempotencyKey(config);
                    config.headers.set(API_CONFIG.HEADERS.IDEMPOTENCY_KEY, autoKey);
                }

                return config;
            },
            (error: AxiosError) => Promise.reject(error)
        );
    }

    // ✅ FIXED: Response interceptor with proper typing
    private static setupResponseInterceptor(axiosInstance: AxiosInstance): void {
        axiosInstance.interceptors.response.use(
            (response) => {
                // Log slow requests in development
                if (import.meta.env.DEV) {
                    const extendedConfig = response.config as ExtendedRequestConfig;
                    const duration = Date.now() - (extendedConfig._startTime || 0);
                    if (duration > API_CONFIG.PERFORMANCE.SLOW_REQUEST_THRESHOLD) {
                        console.warn(`⚠️ Slow request: ${response.config.url} took ${duration}ms`);
                    }
                }

                return response;
            },
            async (error: AxiosError) => {
                // ✅ FIXED: Cast to extended config type
                const originalRequest = error.config as ExtendedRequestConfig;
                
                if (!originalRequest) {
                    return Promise.reject(error);
                }

                // Handle cancellation
                if (error.code === 'ERR_CANCELED' || error.message === 'canceled') {
                    metricsTracker.recordCancellation();
                    return Promise.reject(error);
                }

                // Exclude auth endpoints from token refresh
                const isRefreshTokenRequest = originalRequest.url?.includes('/auth/refresh-token');
                const isAuthRequest = originalRequest.url?.includes('/auth/login') ||
                    originalRequest.url?.includes('/auth/register') ||
                    originalRequest.url?.includes('/auth/logout');

                // ✅ FIXED: Handle 401 errors with proper typing
                if (error.response?.status === 401 &&
                    !originalRequest._retry &&
                    !isRefreshTokenRequest &&
                    !isAuthRequest &&
                    originalRequest.url !== API_CONFIG.CSRF.REFRESH_URL) {

                    originalRequest._retry = true;

                    try {
                        // Use queue-based refresh to handle concurrent requests
                        const newToken = await this.refreshTokenWithQueue(axiosInstance);

                        if (newToken) {
                            // Update original request with new token
                            originalRequest.headers.set(API_CONFIG.AUTH.HEADER, `${API_CONFIG.AUTH.SCHEME} ${newToken}`);
                            return axiosInstance(originalRequest);
                        }
                    } catch (refreshError) {
                        // Refresh failed, clear tokens and redirect
                        console.error('Token refresh failed:', refreshError);
                        this.handleAuthFailure();
                        return Promise.reject(refreshError);
                    }

                    // If we get here, refresh failed
                    this.handleAuthFailure();
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

    // ✅ NEW: Get valid token with proactive refresh
    private static async getValidToken(axiosInstance: AxiosInstance): Promise<string | null> {
        const tokenInfo = tokenManager.getTokenInfo();
        if (!tokenInfo) return null;

        // If token is expired, try to refresh
        if (tokenManager.isTokenExpired(tokenInfo)) {
            console.log('🔄 Token expired, attempting refresh before request');
            const refreshed = await this.performTokenRefresh(axiosInstance);
            return refreshed ? tokenManager.getAccessToken() : null;
        }

        // If token needs refresh soon, refresh proactively
        if (tokenManager.shouldRefreshToken(tokenInfo)) {
            console.log('Token expiring soon, refreshing proactively');
            try {
                await this.performTokenRefresh(axiosInstance);  // ✅ Wait for completion
            } catch (err) {
                console.warn('Proactive refresh failed, will retry on 401:', err);
                // Continue with current token
            }
        }

        return tokenInfo.token;
    }

    // ✅ NEW: Queue-based token refresh to handle concurrent requests
    private static async refreshTokenWithQueue(axiosInstance: AxiosInstance): Promise<string | null> {
        if (this.isRefreshing) {
            // If refresh is in progress, wait for it
            return new Promise((resolve) => {
                this.refreshSubscribers.push((token: string) => {
                    resolve(token || null);
                });
            });
        }

        const success = await this.performTokenRefresh(axiosInstance);
        return success ? tokenManager.getAccessToken() : null;
    }

    // ✅ ENHANCED: Improved token refresh with subscriber notification
    static async performTokenRefresh(axiosInstance: AxiosInstance): Promise<boolean> {
        if (this.isRefreshing) return false;

        this.isRefreshing = true;

        try {
            const currentToken = tokenManager.getAccessToken();
            if (!currentToken) {
                this.isRefreshing = false;
                return false;
            }

            const response = await axiosInstance.post('/v1/auth/refresh-token', null, {
                headers: {
                    'Authorization': `Bearer ${currentToken}`,
                    'X-Skip-Auth': 'true'
                },
                withCredentials: true
            });

            if (response.data.success && response.data.data?.accessToken) {
                const newToken = response.data.data.accessToken;
                tokenManager.setAccessToken(newToken);

                // Notify all waiting subscribers
                this.refreshSubscribers.forEach(callback => callback(newToken));
                this.refreshSubscribers = [];

                console.log('✅ Token refreshed successfully');
                return true;
            }

            return false;
        } catch (error) {
            console.error('❌ Token refresh failed:', error);

            // Notify subscribers of failure
            this.refreshSubscribers.forEach(callback => callback(''));
            this.refreshSubscribers = [];

            return false;
        } finally {
            this.isRefreshing = false;
        }
    }

    // ✅ NEW: Public method for proactive refresh (fixes the missing method error)
    static async performProactiveRefresh(axiosInstance: AxiosInstance): Promise<boolean> {
        console.log('🔄 Performing proactive token refresh');
        return this.performTokenRefresh(axiosInstance);
    }

    // ✅ NEW: Centralized auth failure handling
    private static handleAuthFailure(): void {
        tokenManager.clearAll();

        // Prevent redirect loops
        if (!window.location.pathname.includes('/login')) {
            // Dispatch event for components to handle
            window.dispatchEvent(new CustomEvent('auth-failure'));

            // Fallback redirect
            setTimeout(() => {
                if (!window.location.pathname.includes('/login')) {
                    window.location.href = '/login';
                }
            }, 100);
        }
    }

    // Unchanged helper methods...
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