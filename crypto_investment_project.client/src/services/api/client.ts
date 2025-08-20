// src/services/api/client.ts
import axios, { AxiosInstance, AxiosRequestConfig } from 'axios';
import { API_CONFIG } from './config';
import { ApiResponse, PaginatedResult, RequestConfig } from './types';
import { InterceptorManager } from './interceptors';
import { ApiErrorHandler } from './errorHandler';
import { tokenManager } from './tokenManager';
import { requestDeduplicator } from './requestDeduplicator';
import { requestQueue } from './requestQueue';
import { metricsTracker } from './metricsTracker';
import { v4 as uuidv4 } from 'uuid';

class ApiClient {
    private axiosInstance: AxiosInstance;
    private debounceTimers: Map<string, NodeJS.Timeout> = new Map();
    private throttleTimestamps: Map<string, number> = new Map();

    constructor() {
        this.axiosInstance = axios.create({
            baseURL: API_CONFIG.BASE_URL,
            timeout: API_CONFIG.TIMEOUT,
            headers: {
                'Content-Type': 'application/json',
                'Accept': 'application/json',
            },
            withCredentials: true
        });

        InterceptorManager.setupInterceptors(this.axiosInstance);

        // Start metrics logging in development
        if (API_CONFIG.SIDE_EFFECTS.ENABLE_METRICS) {
            metricsTracker.startLogging();
        }
    }

    // Generic request method with side effect prevention
    private async request<T>(
        method: 'get' | 'post' | 'put' | 'patch' | 'delete',
        url: string,
        data?: unknown,
        config?: RequestConfig
    ): Promise<ApiResponse<T>> {
        // Handle debounce
        if (config?.debounceMs) {
            return this.debounceRequest(method, url, data, config);
        }

        // Handle throttle
        if (config?.throttleMs) {
            const throttled = this.throttleRequest(method, url, config.throttleMs);
            if (!throttled) {
                metricsTracker.recordThrottled();
                throw new Error('Request throttled');
            }
        }

        // Handle deduplication
        if (config?.dedupe !== false && API_CONFIG.SIDE_EFFECTS.DEDUP_ENABLED) {
            const dedupeKey = config?.dedupeKey || requestDeduplicator.generateKey(method, url, data);

            return requestDeduplicator.dedupe(
                dedupeKey,
                () => this.executeRequest<T>(method, url, data, config),
                API_CONFIG.SIDE_EFFECTS.DEDUP_TTL
            );
        }

        // Handle request queue
        if (!config?.skipQueue && this.shouldQueue(config)) {
            return requestQueue.enqueue(
                () => this.executeRequest<T>(method, url, data, config),
                config?.priority || 'normal'
            );
        }

        return this.executeRequest<T>(method, url, data, config);
    }

    private async executeRequest<T>(
        method: string,
        url: string,
        data?: unknown,
        config?: RequestConfig
    ): Promise<ApiResponse<T>> {
        try {
            // Create abort controller
            const abortController = new AbortController();

            // Generate idempotency key if not provided
            let idempotencyKey = config?.idempotencyKey;
            if (!idempotencyKey && ['post', 'put', 'patch'].includes(method) && data) {
                idempotencyKey = uuidv4();
            }

            const axiosConfig: AxiosRequestConfig = {
                ...config,
                headers: {
                    ...config?.headers,
                    ...(config?.skipAuth && { 'X-Skip-Auth': 'true' }),
                    ...(config?.skipCsrf && { 'X-Skip-Csrf': 'true' }),
                    ...(idempotencyKey && { [API_CONFIG.HEADERS.IDEMPOTENCY_KEY]: idempotencyKey })
                },
                params: config?.params,
                signal: config?.signal || abortController.signal,
            };

            const response = await this.axiosInstance.request<ApiResponse<T>>({
                method,
                url,
                data,
                ...axiosConfig
            });

            // Extract total count from headers if available
            const totalCount = response.headers[API_CONFIG.HEADERS.TOTAL_COUNT];

            return {
                ...response.data,
                success: true,
                totalCount: totalCount ? parseInt(totalCount, 10) : undefined
            };
        } catch (error) {
            const apiError = ApiErrorHandler.extractError(error);

            // Don't retry if explicitly disabled
            if (config?.retryCount === 0) {
                throw apiError;
            }

            // Check if we should retry (handled by interceptor)
            throw apiError;
        }
    }

    private async debounceRequest<T>(
        method: string,
        url: string,
        data?: unknown,
        config?: RequestConfig
    ): Promise<ApiResponse<T>> {
        const debounceKey = `${method}-${url}`;

        // Clear existing timer
        const existingTimer = this.debounceTimers.get(debounceKey);
        if (existingTimer) {
            clearTimeout(existingTimer);
            metricsTracker.recordDebounced();
        }

        return new Promise((resolve, reject) => {
            const timer = setTimeout(async () => {
                this.debounceTimers.delete(debounceKey);
                try {
                    const result = await this.request<T>(
                        method as any,
                        url,
                        data,
                        { ...config, debounceMs: undefined }
                    );
                    resolve(result);
                } catch (error) {
                    reject(error);
                }
            }, config!.debounceMs);

            this.debounceTimers.set(debounceKey, timer);
        });
    }

    private throttleRequest(method: string, url: string, throttleMs: number): boolean {
        const throttleKey = `${method}-${url}`;
        const now = Date.now();
        const lastCall = this.throttleTimestamps.get(throttleKey) || 0;

        if (now - lastCall < throttleMs) {
            return false;
        }

        this.throttleTimestamps.set(throttleKey, now);
        return true;
    }

    private shouldQueue(config?: RequestConfig): boolean {
        // Don't queue if explicitly skipped
        if (config?.skipQueue) return false;

        // Check if we're at max concurrent requests
        return requestQueue.active >= API_CONFIG.SIDE_EFFECTS.MAX_CONCURRENT_REQUESTS;
    }

    private delay(ms: number): Promise<void> {
        return new Promise(resolve => setTimeout(resolve, ms));
    }

    // Public API methods
    async get<T>(url: string, config?: RequestConfig): Promise<ApiResponse<T>> {
        return this.request<T>('get', url, undefined, config);
    }

    async post<T>(url: string, data?: unknown, config?: RequestConfig): Promise<ApiResponse<T>> {
        return this.request<T>('post', url, data, config);
    }

    async put<T>(url: string, data?: unknown, config?: RequestConfig): Promise<ApiResponse<T>> {
        return this.request<T>('put', url, data, config);
    }

    async patch<T>(url: string, data?: unknown, config?: RequestConfig): Promise<ApiResponse<T>> {
        return this.request<T>('patch', url, data, config);
    }

    async delete<T>(url: string, data?: unknown, config?: RequestConfig): Promise<ApiResponse<T>> {
        return this.request<T>('delete', url, data, config);
    }

    // File upload method with progress tracking
    async uploadFile<T>(
        url: string,
        file: File,
        additionalData?: Record<string, unknown>,
        onProgress?: (percentage: number) => void
    ): Promise<ApiResponse<T>> {
        const formData = new FormData();
        formData.append('file', file);

        if (additionalData) {
            Object.entries(additionalData).forEach(([key, value]) => {
                formData.append(key, String(value));
            });
        }

        // Generate unique upload ID for idempotency
        const uploadId = uuidv4();

        return this.request<T>('post', url, formData, {
            headers: {
                'Content-Type': 'multipart/form-data',
                [API_CONFIG.HEADERS.IDEMPOTENCY_KEY]: uploadId
            },
            skipQueue: true, // Don't queue file uploads
            onUploadProgress: onProgress ? (progressEvent: any) => {
                const percentCompleted = Math.round((progressEvent.loaded * 100) / progressEvent.total);
                onProgress(percentCompleted);
            } : undefined
        } as any);
    }

    // Batch request helper
    async batch<T>(requests: Array<{
        method: 'get' | 'post' | 'put' | 'delete';
        url: string;
        data?: unknown;
    }>): Promise<ApiResponse<T>[]> {
        const promises = requests.map(req =>
            this.request<T>(req.method, req.url, req.data, { skipQueue: false })
        );

        return Promise.all(promises);
    }

    // Paginated request helper
    async getPaginated<T>(
        url: string,
        page: number = 1,
        pageSize: number = 10,
        config?: RequestConfig
    ): Promise<ApiResponse<PaginatedResult<T>>> {
        const paginatedUrl = `${url}?page=${page}&pageSize=${pageSize}`;
        const response = await this.get<PaginatedResult<T>>(paginatedUrl, config);

        if (response == null || !response.success)
            throw new Error('Failed to fetch paginated data');

        return response;
    }

    // Cancel all pending requests
    cancelAll(): void {
        requestDeduplicator.cancelAll();
        requestQueue.clear();

        // Clear debounce timers
        this.debounceTimers.forEach(timer => clearTimeout(timer));
        this.debounceTimers.clear();
    }

    // Utility methods
    setAuthToken(token: string | null): void {
        tokenManager.setAccessToken(token);
    }

    setKycSession(sessionId: string | null): void {
        tokenManager.setKycSessionId(sessionId);
    }

    clearKycSession(): void {
        tokenManager.setKycSessionId(null);
    }

    clearTokens(): void {
        tokenManager.clearAll();
    }

    isAuthenticated(): boolean {
        return !!tokenManager.getAccessToken();
    }

    getMetrics() {
        return metricsTracker.getMetrics();
    }

    resetMetrics() {
        metricsTracker.reset();
    }
}

// Export singleton instance
export const apiClient = new ApiClient();

// Export convenience methods with side effect prevention built-in
export default {
    get: apiClient.get.bind(apiClient),
    post: apiClient.post.bind(apiClient),
    put: apiClient.put.bind(apiClient),
    patch: apiClient.patch.bind(apiClient),
    delete: apiClient.delete.bind(apiClient),
    uploadFile: apiClient.uploadFile.bind(apiClient),
    batch: apiClient.batch.bind(apiClient),
    getPaginated: apiClient.getPaginated.bind(apiClient),
    cancelAll: apiClient.cancelAll.bind(apiClient),
    setAuthToken: apiClient.setAuthToken.bind(apiClient),
    setKycSession: apiClient.setKycSession.bind(apiClient),
    clearKycSession: apiClient.clearKycSession.bind(apiClient),
    clearTokens: apiClient.clearTokens.bind(apiClient),
    isAuthenticated: apiClient.isAuthenticated.bind(apiClient),
    getMetrics: apiClient.getMetrics.bind(apiClient),
    resetMetrics: apiClient.resetMetrics.bind(apiClient)
};