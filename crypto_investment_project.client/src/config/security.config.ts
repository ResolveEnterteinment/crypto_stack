// Enhanced Security Configuration for Production-Ready Application
// /src/config/security.config.ts

export const SECURITY_CONFIG = {
    // Rate Limiting Configuration
    RATE_LIMIT: {
        WINDOW_MS: 15 * 60 * 1000, // 15 minutes
        MAX_REQUESTS: 100, // Limit each IP to 100 requests per windowMs
        SKIP_SUCCESSFUL_REQUESTS: false,
        RETRY_AFTER_MS: 60000, // 1 minute retry after rate limit
        EXPONENTIAL_BACKOFF: {
            BASE_DELAY: 1000,
            MAX_DELAY: 120000,
            MULTIPLIER: 2,
            JITTER: true
        }
    },

    // CORS Configuration
    CORS: {
        ALLOWED_ORIGINS: [
            import.meta.env.VITE_CLIENT_URL || 'http://localhost:5173',
            'https://localhost:5173',
            'https://localhost:7144'
        ],
        ALLOWED_METHODS: ['GET', 'POST', 'PUT', 'DELETE', 'PATCH', 'OPTIONS'],
        ALLOWED_HEADERS: [
            'Content-Type',
            'Authorization',
            'X-CSRF-TOKEN',
            'X-Request-ID',
            'X-KYC-Session',
            'X-Idempotency-Key'
        ],
        EXPOSED_HEADERS: ['X-Total-Count', 'X-RateLimit-Remaining'],
        CREDENTIALS: true,
        MAX_AGE: 86400 // 24 hours
    },

    // Token Security
    TOKEN: {
        ACCESS_EXPIRY: 15 * 60 * 1000, // 15 minutes
        REFRESH_EXPIRY: 7 * 24 * 60 * 60 * 1000, // 7 days
        REFRESH_THRESHOLD: 5 * 60 * 1000, // Refresh 5 minutes before expiry
        SECURE_STORAGE: true,
        HTTP_ONLY: true,
        SAME_SITE: 'strict' as const,
        ROTATION_ENABLED: true
    },

    // CSRF Protection
    CSRF: {
        ENABLED: true,
        TOKEN_LENGTH: 32,
        COOKIE_NAME: '__Host-csrf',
        HEADER_NAME: 'X-CSRF-TOKEN',
        DOUBLE_SUBMIT: true,
        ROTATION_INTERVAL: 30 * 60 * 1000 // 30 minutes
    },

    // Content Security Policy
    CSP: {
        DEFAULT_SRC: ["'self'"],
        SCRIPT_SRC: ["'self'", "'unsafe-inline'", 'https://cdnjs.cloudflare.com'],
        STYLE_SRC: ["'self'", "'unsafe-inline'", 'https://fonts.googleapis.com'],
        IMG_SRC: ["'self'", 'data:', 'https:', 'blob:'],
        FONT_SRC: ["'self'", 'https://fonts.gstatic.com'],
        CONNECT_SRC: ["'self'", 'wss://localhost:7144', 'https://localhost:7144'],
        FRAME_ANCESTORS: ["'none'"],
        BASE_URI: ["'self'"],
        FORM_ACTION: ["'self'"]
    },

    // Security Headers
    HEADERS: {
        'Strict-Transport-Security': 'max-age=31536000; includeSubDomains',
        'X-Content-Type-Options': 'nosniff',
        'X-Frame-Options': 'DENY',
        'X-XSS-Protection': '1; mode=block',
        'Referrer-Policy': 'strict-origin-when-cross-origin',
        'Permissions-Policy': 'geolocation=(), microphone=(), camera=()'
    },

    // API Security
    API: {
        REQUEST_SIZE_LIMIT: '10mb',
        PARAMETER_POLLUTION_PROTECTION: true,
        SQL_INJECTION_PROTECTION: true,
        XSS_PROTECTION: true,
        TIMEOUT: 30000,
        MAX_REDIRECTS: 5
    },

    // Session Security
    SESSION: {
        SECRET: import.meta.env.SESSION_SECRET || 'CHANGE_THIS_IN_PRODUCTION',
        NAME: '__Host-session',
        RESAVE: false,
        SAVE_UNINITIALIZED: false,
        COOKIE: {
            secure: true,
            httpOnly: true,
            sameSite: 'strict' as const,
            maxAge: 24 * 60 * 60 * 1000 // 24 hours
        }
    },

    // Password Policy
    PASSWORD: {
        MIN_LENGTH: 12,
        REQUIRE_UPPERCASE: true,
        REQUIRE_LOWERCASE: true,
        REQUIRE_NUMBER: true,
        REQUIRE_SPECIAL: true,
        SPECIAL_CHARS: '!@#$%^&*()_+-=[]{}|;:,.<>?',
        BCRYPT_ROUNDS: 12,
        HISTORY_COUNT: 5,
        MAX_AGE_DAYS: 90,
        LOCKOUT_ATTEMPTS: 5,
        LOCKOUT_DURATION_MINUTES: 30
    },

    // Encryption
    ENCRYPTION: {
        ALGORITHM: 'aes-256-gcm',
        KEY_LENGTH: 32,
        IV_LENGTH: 16,
        TAG_LENGTH: 16,
        SALT_ROUNDS: 10
    },

    // Audit & Monitoring
    AUDIT: {
        ENABLED: true,
        LOG_LEVEL: 'info',
        SENSITIVE_DATA_MASK: true,
        RETENTION_DAYS: 90,
        ALERT_ON_SUSPICIOUS: true
    }
};

// Security Utilities
export class SecurityUtils {
    /**
     * Generate secure random token
     */
    static generateSecureToken(length: number = 32): string {
        const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
        const array = new Uint8Array(length);
        crypto.getRandomValues(array);
        return Array.from(array, byte => chars[byte % chars.length]).join('');
    }

    /**
     * Validate password against policy
     */
    static validatePassword(password: string): { valid: boolean; errors: string[] } {
        const errors: string[] = [];
        const policy = SECURITY_CONFIG.PASSWORD;

        if (password.length < policy.MIN_LENGTH) {
            errors.push(`Password must be at least ${policy.MIN_LENGTH} characters`);
        }
        if (policy.REQUIRE_UPPERCASE && !/[A-Z]/.test(password)) {
            errors.push('Password must contain at least one uppercase letter');
        }
        if (policy.REQUIRE_LOWERCASE && !/[a-z]/.test(password)) {
            errors.push('Password must contain at least one lowercase letter');
        }
        if (policy.REQUIRE_NUMBER && !/\d/.test(password)) {
            errors.push('Password must contain at least one number');
        }
        if (policy.REQUIRE_SPECIAL) {
            const specialRegex = new RegExp(`[${policy.SPECIAL_CHARS.replace(/[-[\]{}()*+?.,\\^$|#\s]/g, '\\$&')}]`);
            if (!specialRegex.test(password)) {
                errors.push('Password must contain at least one special character');
            }
        }

        return { valid: errors.length === 0, errors };
    }

    /**
     * Sanitize user input to prevent XSS
     */
    static sanitizeInput(input: string): string {
        const map: { [key: string]: string } = {
            '&': '&amp;',
            '<': '&lt;',
            '>': '&gt;',
            '"': '&quot;',
            "'": '&#x27;',
            '/': '&#x2F;'
        };
        return input.replace(/[&<>"'/]/g, (char) => map[char]);
    }

    /**
     * Generate CSP header string
     */
    static generateCSPHeader(): string {
        const csp = SECURITY_CONFIG.CSP;
        const directives = Object.entries(csp)
            .map(([key, values]) => {
                const directive = key.toLowerCase().replace(/_/g, '-');
                return `${directive} ${values.join(' ')}`;
            })
            .join('; ');
        return directives;
    }

    /**
     * Check if origin is allowed for CORS
     */
    static isOriginAllowed(origin: string): boolean {
        const allowedOrigins = SECURITY_CONFIG.CORS.ALLOWED_ORIGINS;
        return allowedOrigins.includes(origin) ||
            (import.meta.env.NODE_ENV === 'development' && origin.includes('localhost'));
    }
}

export default SECURITY_CONFIG;