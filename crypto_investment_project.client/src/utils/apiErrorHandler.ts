/**
 * Format error details into a user-friendly message
 */
export function formatApiError(error: any): string {
    const errorDetails = extractApiErrorDetails(error);

    // If there are validation errors, format them nicely
    if (errorDetails.validationErrors && Object.keys(errorDetails.validationErrors).length > 0) {
        const validationMessages = Object.entries(errorDetails.validationErrors)
            .map(([field, errors]) => {
                const errorList = Array.isArray(errors) ? errors.join(', ') : errors;
                return `${field}: ${errorList}`;
            })
            .join('; ');

        return `Validation failed: ${validationMessages}`;
    }

    // Special handling for specific error types
    if (errorDetails.isNetworkError) {
        return 'Unable to connect to the server. Please check your internet connection and try again.';
    }

    if (errorDetails.isAuthError) {
        return 'Authentication error. Please log in again.';
    }

    if (errorDetails.isServerError) {
        return `Server error: ${errorDetails.message}. Please try again later.`;
    }

    // Return the error message
    return errorDetails.message;
}

/**
 * Log detailed error information to console
 */
export function logApiError(error: any, context: string = 'API Error'): void {
    const errorDetails = extractApiErrorDetails(error);

    console.group(`🔴 ${context}`);

    console.error('Error Details:', {
        message: errorDetails.message,
        statusCode: errorDetails.statusCode,
        errorCode: errorDetails.errorCode,
        isNetworkError: errorDetails.isNetworkError,
        isServerError: errorDetails.isServerError,
        isClientError: errorDetails.isClientError,
        isAuthError: errorDetails.isAuthError,
        validationErrors: errorDetails.validationErrors
    });

    // Log the original error
    console.error('Original Error:', errorDetails.originalError);

    // If it's an Axios error, log the request and response
    if (axios.isAxiosError(error)) {
        if (error.config) {
            console.log('Request:', {
                url: error.config.url,
                method: error.config.method?.toUpperCase(),
                headers: error.config.headers,
                data: error.config.data
            });
        }

        if (error.response) {
            console.log('Response:', {
                status: error.response.status,
                statusText: error.response.statusText,
                headers: error.response.headers,
                data: error.response.data
            });
        }
    }

    console.groupEnd();
}

/**
 * Custom hook for handling API errors in React components
 */
export function handleApiError(error: any, setErrorFn: (message: string) => void, context: string = 'API Error'): void {
    // Log the error for debugging
    logApiError(error, context);

    // Format user-friendly message
    const errorMessage = formatApiError(error);

    // Set the error message in the component state
    setErrorFn(errorMessage);
}// src/utils/apiErrorHandler.ts
import axios, { AxiosError } from 'axios';

/**
 * Enhanced error handler for API requests
 * Provides detailed error information and formatting
 */
export interface ApiErrorDetails {
    message: string;
    statusCode?: number;
    errorCode?: string;
    validationErrors?: Record<string, string[]>;
    originalError: any;
    isNetworkError: boolean;
    isServerError: boolean;
    isClientError: boolean;
    isAuthError: boolean;
}

/**
 * Process API errors to extract useful information
 */
export function extractApiErrorDetails(error: any): ApiErrorDetails {
    // Default error structure
    const errorDetails: ApiErrorDetails = {
        message: 'An unknown error occurred',
        originalError: error,
        isNetworkError: false,
        isServerError: false,
        isClientError: false,
        isAuthError: false
    };

    // Handle Axios errors
    if (axios.isAxiosError(error)) {
        const axiosError = error as AxiosError;

        // Handle network errors (no response)
        if (axiosError.code === 'ECONNABORTED' || axiosError.code === 'ERR_NETWORK') {
            errorDetails.message = 'Network error: Unable to connect to the server';
            errorDetails.isNetworkError = true;
            return errorDetails;
        }

        // Get response data
        if (axiosError.response) {
            const { status, data } = axiosError.response;
            errorDetails.statusCode = status;

            // Classify error type
            errorDetails.isServerError = status >= 500;
            errorDetails.isClientError = status >= 400 && status < 500;
            errorDetails.isAuthError = status === 401 || status === 403;

            // Extract error information from response data
            if (data) {
                // Handle structured API errors
                if (typeof data === 'object') {
                    // Try different error message fields
                    if (data.message) {
                        errorDetails.message = data.message;
                    } else if (data.error) {
                        errorDetails.message = typeof data.error === 'string' ? data.error : 'An error occurred';
                    } else if (data.errorMessage) {
                        errorDetails.message = data.errorMessage;
                    }

                    // Extract error code
                    if (data.code) {
                        errorDetails.errorCode = data.code;
                    } else if (data.errorCode) {
                        errorDetails.errorCode = data.errorCode;
                    }

                    // Extract validation errors
                    if (data.validationErrors) {
                        errorDetails.validationErrors = data.validationErrors;
                    } else if (data.errors) {
                        errorDetails.validationErrors = data.errors;
                    }
                } else if (typeof data === 'string') {
                    // Plain string error message
                    errorDetails.message = data;
                }
            }

            // Default error messages based on status code if no specific message
            if (errorDetails.message === 'An unknown error occurred') {
                switch (status) {
                    case 400:
                        errorDetails.message = 'Invalid request data';
                        break;
                    case 401:
                        errorDetails.message = 'Authentication required';
                        break;
                    case 403:
                        errorDetails.message = 'You don\'t have permission to perform this action';
                        break;
                    case 404:
                        errorDetails.message = 'Resource not found';
                        break;
                    case 409:
                        errorDetails.message = 'Request conflicts with the current state of the resource';
                        break;
                    case 422:
                        errorDetails.message = 'Validation failed';
                        break;
                    case 429:
                        errorDetails.message = 'Too many requests, please try again later';
                        break;
                    case 500:
                        errorDetails.message = 'Internal server error';
                        break;
                    case 502:
                        errorDetails.message = 'Bad gateway';
                        break;
                    case 503:
                        errorDetails.message = 'Service temporarily unavailable';
                        break;
                    default:
                        errorDetails.message = `Request failed with status ${status}`;
                }
            }
        }
    } else if (error instanceof Error) {
        // Handle regular JavaScript errors
        errorDetails.message = error.message;
    }

    return errorDetails;
}