// src/services/api/index.ts
/**
 * API Service with Built-in Side Effect Prevention
 * 
 * Features:
 * - Automatic request deduplication
 * - Request queue management
 * - Debouncing and throttling
 * - Automatic retry with exponential backoff
 * - Idempotency key generation
 * - Comprehensive metrics tracking
 * - Token management and refresh
 */

import { API_CONFIG } from './config';
import { metricsTracker } from './metricsTracker';

// Main client export (default)
export { default } from './client';
export { apiClient } from './client';

// Type exports
export type {
    ApiResponse,
    ApiError,
    PaginatedResult,
    RequestConfig,
    PendingRequest,
    RequestMetrics
} from './types';

// Configuration export
export { API_CONFIG } from './config';

// Utility exports for advanced usage
export { tokenManager } from './tokenManager';
export { requestDeduplicator } from './requestDeduplicator';
export { requestQueue } from './requestQueue';
export { metricsTracker } from './metricsTracker';
export { ApiErrorHandler } from './errorHandler';

// Helper function exports
export const configureApi = (overrides: Partial<typeof API_CONFIG>) => {
    Object.assign(API_CONFIG, overrides);
};

export const getApiMetrics = () => metricsTracker.getMetrics();
export const resetApiMetrics = () => metricsTracker.reset();