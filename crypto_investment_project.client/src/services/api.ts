// src/services/api.ts
import axios, { AxiosInstance, AxiosResponse, AxiosError, InternalAxiosRequestConfig } from "axios";
import ITraceLogNode from "../interfaces/TraceLog/ITraceLogNode";

// API configuration
const API_CONFIG = {
    BASE_URL: import.meta.env.VITE_API_BASE_URL || "https://localhost:7144/api",
    TIMEOUT: 30000,
    RETRY_DELAY: 1000,
    MAX_RETRIES: 3,
    RETRY_STATUS_CODES: [408, 429, 500, 502, 503, 504],
    AUTH_HEADER: "Authorization",
    AUTH_SCHEME: "Bearer",
    CSRF_REFRESH_URL: "/v1/Csrf/refresh",
    CSRF_HEADER: "X-CSRF-TOKEN",
    CSRF_META_NAME: "csrf-token",
    CSRF_STORAGE_KEY: "csrf-token",
    CSRF_REFRESH_INTERVAL: 30 * 60 * 1000, // 30 minutes
    ENCRYPTION: {
        ENABLED: false,
        KEY_EXCHANGE_URL: '/v1/keyexchange/initialize',
        EXCLUDED_PATHS: [
            '/v1/auth/login',
            '/v1/auth/register',
            '/v1/auth/refresh-token',
            '/v1/keyexchange',
            '/health',
            '/swagger'
        ]
    }
};

// Create Axios instance
const apiClient: AxiosInstance = axios.create({
    baseURL: API_CONFIG.BASE_URL,
    timeout: API_CONFIG.TIMEOUT,
    headers: {
        "Content-Type": "application/json",
        "Accept": "application/json",
    },
    withCredentials: true,
});

// CSRF Token Management
const csrfTokenManager = {
    getToken: (): string | null => {
        const metaToken = document.querySelector(`meta[name="${API_CONFIG.CSRF_META_NAME}"]`)?.getAttribute('content');
        if (metaToken) return metaToken;
        return sessionStorage.getItem(API_CONFIG.CSRF_STORAGE_KEY);
    },

    storeToken: (token: string): void => {
        let metaTag = document.querySelector(`meta[name="${API_CONFIG.CSRF_META_NAME}"]`) as HTMLMetaElement;
        if (metaTag) {
            metaTag.setAttribute('content', token);
        } else {
            metaTag = document.createElement('meta');
            metaTag.setAttribute('name', API_CONFIG.CSRF_META_NAME);
            metaTag.setAttribute('content', token);
            document.head.appendChild(metaTag);
        }
        sessionStorage.setItem(API_CONFIG.CSRF_STORAGE_KEY, token);
        console.debug('✅ CSRF token stored successfully');
    },

    refreshToken: async (): Promise<string | null> => {
        try {
            console.debug('🔄 Refreshing CSRF token...');
            const response = await axios.get(`${API_CONFIG.BASE_URL}${API_CONFIG.CSRF_REFRESH_URL}`, {
                withCredentials: true,
                headers: { 'X-Skip-Csrf-Check': 'true' }
            });

            const token = response.data?.token;
            if (token) {
                csrfTokenManager.storeToken(token);
                console.debug('✅ CSRF token refreshed successfully');
                return token;
            }

            console.warn('⚠️ CSRF token refresh response missing token data');
            return null;
        } catch (error) {
            console.error("❌ Failed to refresh CSRF token:", error);
            return null;
        }
    },

    initialize: async (): Promise<void> => {
        if (!csrfTokenManager.getToken()) {
            await csrfTokenManager.refreshToken();
        }

        setInterval(async () => {
            try {
                await csrfTokenManager.refreshToken();
            } catch (error) {
                console.warn('⚠️ Scheduled CSRF token refresh failed:', error);
            }
        }, API_CONFIG.CSRF_REFRESH_INTERVAL);
    }
};

// Initialize CSRF token management
csrfTokenManager.initialize().catch(error => {
    console.warn('⚠️ Failed to initialize CSRF protection:', error);
});

// Request interceptor
apiClient.interceptors.request.use(
    async (config: InternalAxiosRequestConfig) => {
        // Add request ID for tracing
        const requestId = `req_${Date.now()}_${Math.random().toString(36).substring(2, 10)}`;
        config.headers = config.headers || {};
        config.headers["X-Request-ID"] = requestId;

        // Add auth token
        const token = localStorage.getItem("access_token");
        if (token) {
            config.headers[API_CONFIG.AUTH_HEADER] = `${API_CONFIG.AUTH_SCHEME} ${token}`;
        }

        // Add CSRF token for non-GET requests
        if (config.method?.toLowerCase() !== 'get' && !config.headers['X-Skip-Csrf-Check']) {
            const csrfToken = csrfTokenManager.getToken();
            if (csrfToken) {
                config.headers[API_CONFIG.CSRF_HEADER] = csrfToken;
            } else {
                console.warn(`⚠️ No CSRF token available for ${config.method?.toUpperCase()} request`);
            }
        }

        // Add timing for performance tracking
        (config as any)._requestStartTime = Date.now();

        return config;
    },
    (error: AxiosError) => {
        console.error("❌ Request interceptor error:", error);
        return Promise.reject(error);
    }
);

// Response interceptor
apiClient.interceptors.response.use(
    async (response: AxiosResponse) => {
        // Log response time
        const requestTime = Date.now() - ((response.config as any)._requestStartTime || 0);
        if (requestTime > 1000) {
            console.warn(`⚠️ Slow API request to ${response.config.url}: ${requestTime}ms`);
        }

        return response;
    },
    async (error: AxiosError) => {
        const originalRequest = error.config as InternalAxiosRequestConfig & {
            _retry?: boolean;
            _csrfRetry?: boolean;
        };

        // Handle CSRF token errors
        if (error.response?.status === 403 && !originalRequest._csrfRetry) {
            const errorData = error.response?.data as any;
            if (errorData?.code === "INVALID_ANTIFORGERY_TOKEN" ||
                errorData?.message?.includes("antiforgery")) {

                console.warn("🔄 CSRF token validation failed, refreshing...");
                originalRequest._csrfRetry = true;

                try {
                    const newToken = await csrfTokenManager.refreshToken();
                    if (newToken && originalRequest.headers) {
                        originalRequest.headers[API_CONFIG.CSRF_HEADER] = newToken;
                        return apiClient(originalRequest);
                    }
                } catch (refreshError) {
                    console.error("❌ Failed to refresh CSRF token:", refreshError);
                }
            }
        }

        // Retry for server errors
        if (originalRequest && !originalRequest._retry && error.response &&
            API_CONFIG.RETRY_STATUS_CODES.includes(error.response.status)) {

            originalRequest._retry = true;
            const delay = API_CONFIG.RETRY_DELAY * Math.pow(2, 0) * (0.8 + Math.random() * 0.4);

            console.warn(`🔄 Retrying failed request to ${originalRequest.url} after ${Math.round(delay)}ms`);
            await new Promise(resolve => setTimeout(resolve, delay));
            return apiClient(originalRequest);
        }

        // Handle token expiration
        if (error.response?.status === 401) {
            console.warn("🔑 Token expired, dispatching refresh event");
            window.dispatchEvent(new CustomEvent('auth:tokenExpired'));
        }

        // Enhanced error logging
        const errorDetails = {
            status: error.response?.status,
            statusText: error.response?.statusText,
            url: originalRequest?.url,
            method: originalRequest?.method,
            message: error.message,
        };
        console.error("❌ API Error:", errorDetails);

        return Promise.reject(error);
    }
);

// API wrapper interface
export interface ApiResponse<T> {
    success: boolean;
    data: T;
    message?: string;
    timestamp: Date;
}

export interface ClientResponse<T> {
    success: boolean;
    data: T;
    message?: string;
    statusCode: number;
    errors?: Record<string, string[]>;
    totalCount?: number;
}

// Enhanced API wrapper
const api = {
    instance: apiClient,
    kycSessionId: null as string | null, // Add the missing property

    setKycSession(sessionId: string): void {
        this.kycSessionId = sessionId;
    },

    clearKycSession(): void {
        this.kycSessionId = null;
    },

    getHeaders(): Record<string, string> {
        const headers: Record<string, string> = {
            'Content-Type': 'application/json',
        };

        const token = localStorage.getItem('authToken');
        if (token) {
            headers['Authorization'] = `Bearer ${token}`;
        }

        // Add KYC session header if available
        if (this.kycSessionId) {
            headers['X-KYC-Session'] = this.kycSessionId;
        }

        return headers;
    },

    setHeader: (name: string, value: string): void => {
        apiClient.defaults.headers.common[name] = value;
    },

    removeHeader: (name: string): void => {
        delete apiClient.defaults.headers.common[name];
    },

    setAuthToken: (token: string | null): void => {
        if (token) {
            apiClient.defaults.headers.common[API_CONFIG.AUTH_HEADER] = `${API_CONFIG.AUTH_SCHEME} ${token}`;
        } else {
            delete apiClient.defaults.headers.common[API_CONFIG.AUTH_HEADER];
        }
    },

    // HTTP methods
    async get<T = any>(url: string, config?: any): Promise<AxiosResponse<T>> {
        // Add KYC session header if available and not already in config
        const finalConfig = { ...config };
        if (this.kycSessionId && (!finalConfig.headers || !finalConfig.headers['X-KYC-Session'])) {
            finalConfig.headers = { ...finalConfig.headers, 'X-KYC-Session': this.kycSessionId };
        }
        return apiClient.get<T>(url, finalConfig);
    },

    async post<T = any>(url: string, data?: any, config?: any): Promise<AxiosResponse<T>> {
        // Add KYC session header if available and not already in config
        const finalConfig = { ...config };
        if (this.kycSessionId && (!finalConfig.headers || !finalConfig.headers['X-KYC-Session'])) {
            finalConfig.headers = { ...finalConfig.headers, 'X-KYC-Session': this.kycSessionId };
        }
        return apiClient.post<T>(url, data, finalConfig);
    },

    async put<T = any>(url: string, data?: any, config?: any): Promise<AxiosResponse<T>> {
        // Add KYC session header if available and not already in config
        const finalConfig = { ...config };
        if (this.kycSessionId && (!finalConfig.headers || !finalConfig.headers['X-KYC-Session'])) {
            finalConfig.headers = { ...finalConfig.headers, 'X-KYC-Session': this.kycSessionId };
        }
        return apiClient.put<T>(url, data, finalConfig);
    },

    async delete<T = any>(url: string, config?: any): Promise<AxiosResponse<T>> {
        // Add KYC session header if available and not already in config
        const finalConfig = { ...config };
        if (this.kycSessionId && (!finalConfig.headers || !finalConfig.headers['X-KYC-Session'])) {
            finalConfig.headers = { ...finalConfig.headers, 'X-KYC-Session': this.kycSessionId };
        }
        return apiClient.delete<T>(url, finalConfig);
    },

    async patch<T = any>(url: string, data?: any, config?: any): Promise<AxiosResponse<T>> {
        // Add KYC session header if available and not already in config
        const finalConfig = { ...config };
        if (this.kycSessionId && (!finalConfig.headers || !finalConfig.headers['X-KYC-Session'])) {
            finalConfig.headers = { ...finalConfig.headers, 'X-KYC-Session': this.kycSessionId };
        }
        return apiClient.patch<T>(url, data, finalConfig);
    },

    // Safe request wrapper
    async safeRequest<T = any>(
        method: 'get' | 'post' | 'put' | 'delete' | 'patch',
        url: string,
        data?: any,
        config?: any
    ): Promise<ClientResponse<T>> {
        try {
            let response: AxiosResponse<any>;

            // Add KYC session header if available and not already in config
            const finalConfig = { ...config };
            if (this.kycSessionId && (!finalConfig.headers || !finalConfig.headers['X-KYC-Session'])) {
                finalConfig.headers = { ...finalConfig.headers, 'X-KYC-Session': this.kycSessionId };
            }

            switch (method) {
                case 'get': response = await this.get<ApiResponse<T>>(url, finalConfig); break;
                case 'post': response = await this.post<ApiResponse<T>>(url, data, finalConfig); break;
                case 'put': response = await this.put<ApiResponse<T>>(url, data, finalConfig); break;
                case 'delete': response = await this.delete<ApiResponse<T>>(url, finalConfig); break;
                case 'patch': response = await this.patch<ApiResponse<T>>(url, data, finalConfig); break;
                default: throw new Error(`Unsupported method: ${method}`);
            }


            console.log("api::safeRequest => response: ", response);
            return {
                data: response.data &&
                    typeof response.data === 'object' &&
                    response.data.hasOwnProperty('data') ? response.data.data : response.data,
                success: true,
                statusCode: response.status,
                message: response.statusText || response.data?.message || 'Request completed',
                totalCount: parseInt(response.headers['x-total-count'] || '0')
            };
        } catch (error: any) {
            return {
                data: {} as T,
                success: false,
                statusCode: error.response?.status || 500,
                message: error.response?.data?.message || error.message || 'An error occurred',
                errors: error.response?.data?.validationErrors || error.response?.data?.errors
            };
        }
    },

    // Utility methods
    isAuthenticated: (): boolean => !!localStorage.getItem("access_token"),
    getCsrfToken: (): string | null => csrfTokenManager.getToken(),
    refreshCsrfToken: async (): Promise<string | null> => csrfTokenManager.refreshToken(),
    isCsrfProtectionActive: (): boolean => !!csrfTokenManager.getToken(),

    // Initialize API services
    initialize: async (): Promise<void> => {
        console.info("🚀 Initializing API services...");

        try {
            await csrfTokenManager.initialize();
            console.debug("✅ CSRF protection initialized");

        } catch (error) {
            console.error("❌ Failed to initialize API services:", error);
            throw error;
        }
    }
};

// Export trace functions
export const getTraceTree = () => api.get<ITraceLogNode[]>('/Trace/tree');
export const resolveTraceLog = (id: string, comment: string) => api.post(`/Trace/resolve/${id}`, comment);

// Event handlers
window.addEventListener('auth:tokenExpired', () => {
    window.dispatchEvent(new CustomEvent('auth:refreshNeeded'));
});

export default api;