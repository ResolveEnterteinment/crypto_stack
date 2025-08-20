// src/services/api/errorHandler.ts
import { AxiosError } from 'axios';
import { ApiError } from './types';
import { API_CONFIG } from './config';

export class ApiErrorHandler {
    static extractError(error: unknown): ApiError {
        if (error instanceof AxiosError) {
            const response = error.response;
            const isNetworkError = !response && error.code === 'ERR_NETWORK';
            const statusCode = response?.status;

            return {
                message: this.extractMessage(error),
                statusCode,
                errorCode: response?.data?.code || response?.data?.errorCode,
                validationErrors: response?.data?.validationErrors || response?.data?.errors,
                isNetworkError,
                isAuthError: statusCode === 401 || statusCode === 403,
                isServerError: statusCode ? statusCode >= 500 : false
            };
        }

        return {
            message: error instanceof Error ? error.message : 'An unexpected error occurred',
            isNetworkError: false,
            isAuthError: false,
            isServerError: false
        };
    }

    private static extractMessage(error: AxiosError): string {
        const response = error.response;

        if (!response) {
            if (error.code === 'ERR_NETWORK') {
                return 'Network error. Please check your connection.';
            }
            if (error.code === 'ECONNABORTED') {
                return 'Request timeout. Please try again.';
            }
            return error.message || 'Connection failed';
        }

        // Extract message from various response formats
        const data = response.data as any;
        return data?.message ||
            data?.error?.message ||
            data?.detail ||
            response.statusText ||
            'Request failed';
    }

    static formatUserMessage(error: ApiError): string {
        if (error.validationErrors) {
            const messages = Object.entries(error.validationErrors)
                .map(([field, errors]) => `${field}: ${errors.join(', ')}`)
                .join('; ');
            return `Validation failed: ${messages}`;
        }

        if (error.isNetworkError) {
            return 'Connection error. Please check your internet and try again.';
        }

        if (error.isAuthError) {
            return error.statusCode === 403
                ? 'You don\'t have permission to perform this action.'
                : 'Please log in to continue.';
        }

        if (error.isServerError) {
            return 'Server error. Please try again later.';
        }

        return error.message;
    }

    static shouldRetry(error: ApiError): boolean {
        if (error.isNetworkError) return true;
        if (error.statusCode && API_CONFIG.RETRY.STATUS_CODES.includes(error.statusCode as any)) {
            return true;
        }
        return false;
    }
}