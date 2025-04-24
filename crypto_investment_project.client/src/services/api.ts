// src/services/api.ts
import axios, { AxiosInstance, AxiosResponse, AxiosError, InternalAxiosRequestConfig } from "axios";

// API configuration
const API_CONFIG = {
    BASE_URL: import.meta.env.VITE_API_BASE_URL || "https://localhost:7144/api",
    TIMEOUT: 30000, // 30 seconds
    RETRY_DELAY: 1000,
    MAX_RETRIES: 3,
    RETRY_STATUS_CODES: [408, 429, 500, 502, 503, 504], // Status codes to retry
    AUTH_HEADER: "Authorization",
    AUTH_SCHEME: "Bearer",
    CSRF_REFRESH_URL: "/v1/Csrf/refresh", // Endpoint to refresh CSRF token
    CSRF_HEADER: "X-CSRF-TOKEN", // Header name for CSRF token
    CSRF_META_NAME: "csrf-token", // Meta tag name for storing CSRF token
    CSRF_STORAGE_KEY: "csrf-token", // Storage key for CSRF token
    CSRF_REFRESH_INTERVAL: 30 * 60 * 1000, // 30 minutes
};

// Create Axios instance
const apiClient: AxiosInstance = axios.create({
    baseURL: API_CONFIG.BASE_URL,
    timeout: API_CONFIG.TIMEOUT,
    headers: {
        "Content-Type": "application/json",
        "Accept": "application/json",
    },
    withCredentials: true, // Important for CSRF tokens and cookies
});

// CSRF Token Management
const csrfTokenManager = {
    // Get CSRF token from storage or meta tag
    getToken: (): string | null => {
        // First try meta tag
        const metaToken = document.querySelector(`meta[name="${API_CONFIG.CSRF_META_NAME}"]`)?.getAttribute('content');
        if (metaToken) {
            return metaToken;
        }

        // Fallback to sessionStorage
        return sessionStorage.getItem(API_CONFIG.CSRF_STORAGE_KEY);
    },

    // Store CSRF token in both meta tag and sessionStorage
    storeToken: (token: string): void => {
        // Update or create meta tag
        let metaTag = document.querySelector(`meta[name="${API_CONFIG.CSRF_META_NAME}"]`);
        if (metaTag) {
            metaTag.setAttribute('content', token);
        } else {
            metaTag = document.createElement('meta');
            metaTag.setAttribute('name', API_CONFIG.CSRF_META_NAME);
            metaTag.setAttribute('content', token);
            document.head.appendChild(metaTag);
        }

        // Also store in sessionStorage as backup
        sessionStorage.setItem(API_CONFIG.CSRF_STORAGE_KEY, token);

        console.debug('CSRF token stored successfully');
    },

    // Refresh CSRF token from server
    refreshToken: async (): Promise<string | null> => {
        try {
            console.debug('Refreshing CSRF token...');

            // Use axios directly to avoid circular dependencies with interceptors
            const response = await axios.get(`${API_CONFIG.BASE_URL}${API_CONFIG.CSRF_REFRESH_URL}`, {
                withCredentials: true,
                headers: {
                    'X-Skip-Csrf-Check': 'true' // Skip CSRF check to avoid infinite loop
                }
            });

            const token = response.data?.token;
            if (token) {
                csrfTokenManager.storeToken(token);
                console.debug('CSRF token refreshed successfully');
                return token;
            }

            console.warn('CSRF token refresh response missing token data');
            return null;
        } catch (error) {
            console.error("Failed to refresh CSRF token:", error);
            return null;
        }
    },

    // Initialize CSRF token
    initialize: async (): Promise<void> => {
        // Only refresh if we don't have a token already
        if (!csrfTokenManager.getToken()) {
            await csrfTokenManager.refreshToken();
        }

        // Set up automatic refresh interval
        setInterval(async () => {
            try {
                await csrfTokenManager.refreshToken();
            } catch (error) {
                console.warn('Scheduled CSRF token refresh failed:', error);
            }
        }, API_CONFIG.CSRF_REFRESH_INTERVAL);
    }
};

// Initialize CSRF token management on page load
csrfTokenManager.initialize().catch(error => {
    console.warn('Failed to initialize CSRF protection:', error);
});

// Request interceptor for adding tokens, request IDs, etc.
apiClient.interceptors.request.use(
    (config: InternalAxiosRequestConfig) => {
        // Add request ID for tracing
        const requestId = `req_${Date.now()}_${Math.random().toString(36).substring(2, 10)}`;
        config.headers = config.headers || {};
        config.headers["X-Request-ID"] = requestId;

        // Get auth token from storage
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
                console.warn(`No CSRF token available for ${config.method?.toUpperCase()} request to ${config.url}`);
            }
        }

        // Add timestamp for performance tracking
        (config as any)._requestStartTime = Date.now();

        return config;
    },
    (error: AxiosError) => {
        console.error("Request error interceptor:", error);
        return Promise.reject(error);
    }
);

// Response interceptor for error handling, retries, etc.
apiClient.interceptors.response.use(
    (response: AxiosResponse) => {
        // Log API response time for performance monitoring
        const requestTime = Date.now() - ((response.config as any)._requestStartTime || 0);
        console.debug(`API request to ${response.config.url} completed in ${requestTime}ms`);

        return response;
    },
    async (error: AxiosError) => {
        const originalRequest = error.config as InternalAxiosRequestConfig & {
            _retry?: boolean;
            _csrfRetry?: boolean;
        };

        // Handle CSRF token errors
        if (error.response?.status === 403 &&
            (error.response?.data?.code === "INVALID_ANTIFORGERY_TOKEN" ||
                error.response?.data?.message?.includes("antiforgery")) &&
            !originalRequest._csrfRetry) {

            console.log("CSRF token validation failed, attempting to refresh token...");
            originalRequest._csrfRetry = true;

            try {
                // Refresh the CSRF token
                const newToken = await csrfTokenManager.refreshToken();

                if (newToken) {
                    // Update the failed request with new token
                    originalRequest.headers[API_CONFIG.CSRF_HEADER] = newToken;
                    // Retry the original request
                    return apiClient(originalRequest);
                }
            } catch (refreshError) {
                console.error("Failed to refresh CSRF token after 403 error:", refreshError);
                // Dispatch an event for app-wide handling if needed
                window.dispatchEvent(new CustomEvent('auth:csrfInvalid'));
            }
        }

        // Retry for specific server errors (with exponential backoff)
        if (originalRequest &&
            !originalRequest._retry &&
            error.response &&
            API_CONFIG.RETRY_STATUS_CODES.includes(error.response.status)) {

            originalRequest._retry = true;

            // Implement exponential backoff with a small random delay
            const retryCount = originalRequest._retry ? 1 : 0;
            const delay = Math.min(
                API_CONFIG.RETRY_DELAY * Math.pow(2, retryCount) * (0.8 + Math.random() * 0.4),
                10000 // Maximum 10 seconds
            );

            console.warn(`Retrying failed request to ${originalRequest.url} after ${Math.round(delay)}ms`);

            // Wait before retrying
            await new Promise(resolve => setTimeout(resolve, delay));

            return apiClient(originalRequest);
        }

        // Handle token expiration
        if (error.response?.status === 401) {
            // Dispatch an event that Auth context can listen for
            window.dispatchEvent(new CustomEvent('auth:tokenExpired'));
        }

        // Enhanced error logging
        const errorDetails = {
            status: error.response?.status,
            statusText: error.response?.statusText,
            url: originalRequest?.url,
            method: originalRequest?.method,
            message: error.message,
            data: error.response?.data,
        };

        console.error("API Error:", errorDetails);

        return Promise.reject(error);
    }
);

// Type for the enhanced API response
interface ApiResponse<T> {
    data: T;
    success: boolean;
    statusCode: number;
    message?: string;
    errors?: Record<string, string[]>;
    totalCount?: number;
}

// Enhanced API wrapper with improved error handling and typing
const api = {
    // Direct access to the axios instance
    instance: apiClient,

    // Set default headers
    setHeader: (name: string, value: string): void => {
        apiClient.defaults.headers.common[name] = value;
    },

    // Remove a header
    removeHeader: (name: string): void => {
        delete apiClient.defaults.headers.common[name];
    },

    // Set auth token
    setAuthToken: (token: string | null): void => {
        if (token) {
            apiClient.defaults.headers.common[API_CONFIG.AUTH_HEADER] = `${API_CONFIG.AUTH_SCHEME} ${token}`;
        } else {
            delete apiClient.defaults.headers.common[API_CONFIG.AUTH_HEADER];
        }
    },

    // HTTP method implementations
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

    async head<T = any>(url: string, config?: any): Promise<AxiosResponse<T>> {
        return apiClient.head<T>(url, config);
    },

    async patch<T = any>(url: string, data?: any, config?: any): Promise<AxiosResponse<T>> {
        return apiClient.patch<T>(url, data, config);
    },

    // Safely handle API errors with typed returns
    async safeRequest<T = any>(
        method: 'get' | 'post' | 'put' | 'delete' | 'patch',
        url: string,
        data?: any,
        config?: any
    ): Promise<ApiResponse<T>> {
        try {
            let response;

            switch (method) {
                case 'get':
                    response = await apiClient.get<T>(url, config);
                    break;
                case 'post':
                    response = await apiClient.post<T>(url, data, config);
                    break;
                case 'put':
                    response = await apiClient.put<T>(url, data, config);
                    break;
                case 'delete':
                    response = await apiClient.delete<T>(url, config);
                    break;
                case 'patch':
                    response = await apiClient.patch<T>(url, data, config);
                    break;
                default:
                    throw new Error(`Unsupported method: ${method}`);
            }

            return {
                data: response.data,
                success: true,
                statusCode: response.status,
                message: response.statusText,
                totalCount: parseInt(response.headers['x-total-count'] || '0')
            };
        } catch (error: any) {
            // Format error response consistently
            const apiResponse: ApiResponse<T> = {
                data: {} as T,
                success: false,
                statusCode: error.response?.status || 500,
                message: error.response?.data?.message || error.message || 'An error occurred',
                errors: error.response?.data?.validationErrors || error.response?.data?.errors
            };

            return apiResponse;
        }
    },

    // Helper to check if user is authenticated
    isAuthenticated: (): boolean => {
        const token = localStorage.getItem("access_token");
        return !!token;
    },

    // CSRF token helper methods
    getCsrfToken: (): string | null => {
        return csrfTokenManager.getToken();
    },

    // Refresh CSRF token
    refreshCsrfToken: async (): Promise<string | null> => {
        return csrfTokenManager.refreshToken();
    },

    // Check if CSRF protection is active
    isCsrfProtectionActive: (): boolean => {
        return !!csrfTokenManager.getToken();
    },

    // Initialize API services including CSRF
    initialize: async (): Promise<void> => {
        await csrfTokenManager.initialize();
    }
};

// Add event listener for auth events
window.addEventListener('auth:tokenExpired', async () => {
    // Dispatch event to inform app about expired token
    window.dispatchEvent(new CustomEvent('auth:refreshNeeded'));
});

export default api;