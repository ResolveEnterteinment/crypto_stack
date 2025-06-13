// src/services/api.ts
import axios, { AxiosInstance, AxiosResponse, AxiosError, InternalAxiosRequestConfig } from "axios";
import ITraceLogNode from "../interfaces/TraceLog/ITraceLogNode";
import encryptionService from './encryptionService';

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

// Encryption management with improved error handling
const encryptionManager = {
    enabled: false,

    initialize: async (encryptionKey?: string): Promise<boolean> => {
        try {
            if (encryptionManager.enabled) {
                console.debug("✅ Encryption already initialized");
                return true;
            }

            // Get encryption key from server if not provided
            if (!encryptionKey) {
                encryptionKey = await encryptionManager._fetchEncryptionKey();
                if (!encryptionKey) {
                    console.error("❌ Failed to obtain encryption key");
                    return false;
                }
            }

            // Initialize the encryption service
            await encryptionService.initialize(encryptionKey);
            encryptionManager.enabled = true;
            console.info("🔐 Encryption initialized successfully");
            return true;

        } catch (error) {
            console.error("❌ Failed to initialize encryption:", error);
            encryptionManager.enabled = false;
            return false;
        }
    },

    _fetchEncryptionKey: async (): Promise<string | null> => {
        const maxAttempts = 3;

        for (let attempt = 1; attempt <= maxAttempts; attempt++) {
            try {
                console.debug(`🔑 Requesting encryption key (attempt ${attempt}/${maxAttempts})...`);

                const response = await axios.post(
                    `${API_CONFIG.BASE_URL}${API_CONFIG.ENCRYPTION.KEY_EXCHANGE_URL}`,
                    {},
                    {
                        withCredentials: true,
                        headers: { 'X-Skip-Csrf-Check': 'true' }
                    }
                );

                const key = response.data?.key;
                if (key) {
                    console.debug("✅ Encryption key received successfully");
                    return key;
                }

                console.warn(`⚠️ No encryption key in response (attempt ${attempt})`);
            } catch (error) {
                console.error(`❌ Key exchange attempt ${attempt} failed:`, error);

                if (attempt < maxAttempts) {
                    await new Promise(resolve => setTimeout(resolve, 1000 * attempt)); // Exponential backoff
                }
            }
        }

        return null;
    },

    shouldEncrypt: (url?: string): boolean => {
        if (!encryptionManager.enabled || !url) return false;
        return !API_CONFIG.ENCRYPTION.EXCLUDED_PATHS.some(
            path => url.toLowerCase().includes(path.toLowerCase())
        );
    },

    disable: (): void => {
        encryptionManager.enabled = false;
        encryptionService.reset();
        console.info("🔓 Encryption disabled");
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

        // Add encryption for request data
        if (encryptionManager.shouldEncrypt(config.url) && config.data) {
            try {
                const dataToEncrypt = typeof config.data === 'string' ? config.data : JSON.stringify(config.data);
                const encryptedPayload = await encryptionService.encrypt(dataToEncrypt);
                config.data = { payload: encryptedPayload };
                console.debug(`🔐 Request encrypted for ${config.url}`);
            } catch (error) {
                console.error("❌ Failed to encrypt request data:", error);
                // Continue with unencrypted data rather than failing
            }
        }

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

        // Handle response decryption
        if (encryptionManager.shouldEncrypt(response.config.url) && response.data?.payload) {
            try {
                console.debug(`🔓 Decrypting response from ${response.config.url}`);
                const decryptedData = await encryptionService.decrypt(response.data.payload);
                response.data = typeof decryptedData === 'string' && decryptedData.startsWith('{')
                    ? JSON.parse(decryptedData)
                    : decryptedData;
                console.debug(`✅ Response decrypted successfully`);
            } catch (error) {
                console.error("❌ Failed to decrypt response:", error);
                // Keep original response as fallback
            }
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
interface ApiResponse<T> {
    data: T;
    success: boolean;
    statusCode: number;
    message?: string;
    errors?: Record<string, string[]>;
    totalCount?: number;
}

// Enhanced API wrapper
const api = {
    instance: apiClient,

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
        return apiClient.get<T>(url, config);
    },

    async post<T = any>(url: string, data?: any, config?: any): Promise<AxiosResponse<T>> {
        return apiClient.post<T>(url, data, config);
    },

    async put<T = any>(url: string, data?: any, config?: any): Promise<AxiosResponse<T>> {
        return apiClient.put<T>(url, data, config);
    },

    async delete<T = any>(url: string, config?: any): Promise<AxiosResponse<T>> {
        return apiClient.delete<T>(url, config);
    },

    async patch<T = any>(url: string, data?: any, config?: any): Promise<AxiosResponse<T>> {
        return apiClient.patch<T>(url, data, config);
    },

    // Safe request wrapper
    async safeRequest<T = any>(
        method: 'get' | 'post' | 'put' | 'delete' | 'patch',
        url: string,
        data?: any,
        config?: any
    ): Promise<ApiResponse<T>> {
        try {
            let response: AxiosResponse<T>;

            switch (method) {
                case 'get': response = await this.get<T>(url, config); break;
                case 'post': response = await this.post<T>(url, data, config); break;
                case 'put': response = await this.put<T>(url, data, config); break;
                case 'delete': response = await this.delete<T>(url, config); break;
                case 'patch': response = await this.patch<T>(url, data, config); break;
                default: throw new Error(`Unsupported method: ${method}`);
            }

            return {
                data: response.data,
                success: true,
                statusCode: response.status,
                message: response.statusText,
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

    // Encryption management
    enableEncryption: async (key?: string): Promise<boolean> => {
        return encryptionManager.initialize(key);
    },

    disableEncryption: (): void => {
        encryptionManager.disable();
    },

    isEncryptionEnabled: (): boolean => {
        return encryptionManager.enabled;
    },

    // Initialize API services
    initialize: async (encryptionKey?: string): Promise<void> => {
        console.info("🚀 Initializing API services...");

        try {
            await csrfTokenManager.initialize();
            console.debug("✅ CSRF protection initialized");

            if (encryptionKey || API_CONFIG.ENCRYPTION.ENABLED) {
                const success = await encryptionManager.initialize(encryptionKey);
                if (success) {
                    console.info("✅ API services initialized with encryption");
                } else {
                    console.warn("⚠️ API services initialized without encryption");
                }
            }
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