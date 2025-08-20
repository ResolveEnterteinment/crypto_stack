// src/services/api/config.ts
export const API_CONFIG = {
    BASE_URL: import.meta.env.VITE_API_BASE_URL || "https://localhost:7144/api",
    TIMEOUT: 30000,
    RETRY: {
        ATTEMPTS: 3,
        DELAY: 1000,
        STATUS_CODES: [408, 429, 500, 502, 503, 504] as number[],
        BACKOFF_MULTIPLIER: 2,
        MAX_DELAY: 120000
    },
    AUTH: {
        HEADER: "Authorization",
        SCHEME: "Bearer",
        TOKEN_KEY: "access_token",
        REFRESH_TOKEN_KEY: "refresh_token"
    },
    CSRF: {
        HEADER: "X-CSRF-TOKEN",
        REFRESH_URL: "/v1/csrf/refresh",
        STORAGE_KEY: "csrf-token",
        REFRESH_INTERVAL: 30 * 60 * 1000
    },
    HEADERS: {
        REQUEST_ID: "X-Request-ID",
        KYC_SESSION: "X-KYC-Session",
        TOTAL_COUNT: "x-total-count",
        IDEMPOTENCY_KEY: "X-Idempotency-Key"
    },
    PERFORMANCE: {
        SLOW_REQUEST_THRESHOLD: 1000
    },
    SIDE_EFFECTS: {
        DEDUP_TTL: 5000,
        DEDUP_ENABLED: true,
        MAX_CONCURRENT_REQUESTS: 5,
        REQUEST_QUEUE_TIMEOUT: 60000,
        DEBOUNCE_DEFAULT: 300,
        THROTTLE_DEFAULT: 1000,
        ENABLE_METRICS: true
    }
} as const;