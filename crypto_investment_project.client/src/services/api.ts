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
    AUTH_SCHEME: "Bearer"
};

// Create Axios instance
const apiClient: AxiosInstance = axios.create({
    baseURL: API_CONFIG.BASE_URL,
    timeout: API_CONFIG.TIMEOUT,
    headers: {
        "Content-Type": "application/json",
        "Accept": "application/json",
    },
});

// Request interceptor
apiClient.interceptors.request.use(
    (config: InternalAxiosRequestConfig) => {
        // Add request ID for tracing
        const requestId = `req_${Date.now()}_${Math.random().toString(36).substring(2, 10)}`;
        config.headers = config.headers || {};
        config.headers["X-Request-ID"] = requestId;

        // Get token from storage
        const token = localStorage.getItem("access_token");
        if (token) {
            config.headers[API_CONFIG.AUTH_HEADER] = `${API_CONFIG.AUTH_SCHEME} ${token}`;
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

// Response interceptor
apiClient.interceptors.response.use(
    (response: AxiosResponse) => {
        // Log API response time for performance monitoring
        const requestTime = Date.now() - ((response.config as any)._requestStartTime || 0);
        console.debug(`API request to ${response.config.url} completed in ${requestTime}ms`);

        return response;
    },
    async (error: AxiosError) => {
        const originalRequest = error.config as InternalAxiosRequestConfig & { _retry?: boolean };

        // Only retry if we haven't already and status is in our retry list
        if (
            originalRequest &&
            !originalRequest._retry &&
            error.response &&
            API_CONFIG.RETRY_STATUS_CODES.includes(error.response.status)
        ) {
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
            const event = new CustomEvent('auth:tokenExpired');
            window.dispatchEvent(event);
        }

        // Handle CSRF token issues
        if (error.response?.status === 403 &&
            error.response?.data &&
            typeof error.response.data === 'object' &&
            'code' in error.response.data &&
            error.response.data.code === "INVALID_ANTIFORGERY_TOKEN") {
            // Dispatch an event that Auth context can listen for
            const event = new CustomEvent('auth:csrfInvalid');
            window.dispatchEvent(event);
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
};

export default api;