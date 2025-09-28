// src/services/api/types.ts
export interface ApiResponse<T = unknown> {
    data: T;
    success: boolean;
    message?: string;
    statusCode?: number;
    errors?: Record<string, string[]>;
    totalCount?: number;
    headers?: Record<string, string>; // ✅ Add headers property
}

export interface ApiError {
    message: string;
    statusCode?: number;
    errorCode?: string;
    validationErrors?: Record<string, string[]>;
    isNetworkError: boolean;
    isAuthError: boolean;
    isServerError: boolean;
}

export interface RequestConfig {
    headers?: Record<string, string>;
    params?: Record<string, any>;
    skipAuth?: boolean;
    skipCsrf?: boolean;
    retryCount?: number;
    signal?: AbortSignal;
    idempotencyKey?: string;
    dedupe?: boolean;
    dedupeKey?: string;
    debounceMs?: number;
    throttleMs?: number;
    priority?: 'low' | 'normal' | 'high';
    skipQueue?: boolean;
    responseType?: 'json' | 'blob' | 'text' | 'arraybuffer' | 'document' | 'stream';
    includeHeaders?: boolean; // ✅ Add option to include headers
}

export interface PendingRequest {
    promise: Promise<any>;
    timestamp: number;
    abortController?: AbortController;
}

export interface RequestMetrics {
    duplicatesPrevented: number;
    requestsCancelled: number;
    requestsQueued: number;
    requestsThrottled: number;
    requestsDebounced: number;
    requestsRetried: number;
    totalRequests: number;
}

export interface PaginatedResult<T> {
    items: T[];
    page: number;
    pageSize: number;
    totalCount: number;
    totalPages: number;
    hasPreviousPage: boolean;
    hasNextPage: boolean;
}