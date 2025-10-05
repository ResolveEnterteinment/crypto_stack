// Secure Token Manager - HTTP-Only Cookie Architecture
// src/services/api/tokenManager.ts

import { API_CONFIG } from "./config";

interface TokenInfo {
    token: string;
    expiresAt: number; // Unix timestamp in milliseconds
    issuedAt: number;
}

/**
 * Token Manager for Access Tokens Only
 * 
 * SECURITY ARCHITECTURE:
 * - Access tokens: Stored in localStorage (short-lived, 15 min)
 * - Refresh tokens: HTTP-only cookies ONLY (never accessible to JavaScript)
 * - CSRF tokens: Session storage (for mutation requests)
 * 
 * CRITICAL: This class never touches refresh tokens - they're managed
 * entirely by the browser's cookie storage and sent automatically.
 */
class TokenManager {
    private static instance: TokenManager;
    private csrfToken: string | null = null;
    private kycSessionId: string | null = null;
    private tokenExpiryCheckInterval: number | null = null;
    private scheduledRefreshTimeout: number | null = null;
    private isRefreshing: boolean = false;

    // Proactive refresh threshold: refresh when less than 5 minutes remaining
    private readonly TOKEN_REFRESH_THRESHOLD = 5 * 60 * 1000; // 5 minutes

    // Check token expiry every 30 seconds
    private readonly TOKEN_EXPIRY_CHECK_INTERVAL = 30 * 1000;

    static getInstance(): TokenManager {
        if (!TokenManager.instance) {
            TokenManager.instance = new TokenManager();
        }
        return TokenManager.instance;
    }

    constructor() {
        // Initialize monitoring if we have a valid token
        this.initializeTokenMonitoring();

        // Listen for storage events from other tabs
        window.addEventListener('storage', this.handleStorageChange.bind(this));
    }

    // ==================== ACCESS TOKEN MANAGEMENT ====================

    /**
     * Get current access token if valid
     * Returns null if token is expired or missing
     */
    getAccessToken(): string | null {
        const tokenInfo = this.getTokenInfo();
        if (!tokenInfo) return null;

        if (this.isTokenExpired(tokenInfo)) {
            console.warn('Access token expired, clearing from storage');
            this.clearAccessToken();
            return null;
        }

        return tokenInfo.token;
    }

    /**
     * Get detailed token information with expiry
     */
    getTokenInfo(): TokenInfo | null {
        const token = localStorage.getItem(API_CONFIG.AUTH.TOKEN_KEY);
        if (!token) return null;

        try {
            const payload = this.parseJwtPayload(token);

            // Validate payload has required fields
            if (!payload.exp || !payload.iat) {
                console.warn('Invalid JWT payload: missing exp or iat');
                this.clearAccessToken();
                return null;
            }

            const expiresAt = payload.exp * 1000; // Convert to milliseconds
            const issuedAt = payload.iat * 1000;

            // Log expiry for debugging (only in dev)
            if (import.meta.env.DEV) {
                const timeRemaining = expiresAt - Date.now();
                const minutesRemaining = Math.floor(timeRemaining / 60000);
                console.log(`Token expires in ${minutesRemaining} minutes (${new Date(expiresAt).toLocaleString()})`);
            }

            return { token, expiresAt, issuedAt };
        } catch (error) {
            console.warn('Failed to parse token payload:', error);
            this.clearAccessToken();
            return null;
        }
    }

    /**
     * Set access token and start lifecycle management
     */
    setAccessToken(token: string | null): void {
        if (token) {
            localStorage.setItem(API_CONFIG.AUTH.TOKEN_KEY, token);

            // Start monitoring and schedule proactive refresh
            this.startTokenExpiryMonitoring();
            this.scheduleTokenRefresh();

            if (import.meta.env.DEV) {
                const tokenInfo = this.getTokenInfo();
                if (tokenInfo) {
                    console.log('Access token set, expires:', new Date(tokenInfo.expiresAt).toLocaleString());
                }
            }
        } else {
            this.clearAccessToken();
        }
    }

    /**
     * Clear access token and stop monitoring
     */
    private clearAccessToken(): void {
        localStorage.removeItem(API_CONFIG.AUTH.TOKEN_KEY);
        this.cancelScheduledRefresh();
    }

    /**
     * Check if token is expired (with buffer for network latency)
     */
    isTokenExpired(tokenInfo?: TokenInfo | null): boolean {
        const info = tokenInfo ?? this.getTokenInfo();
        if (!info || !info.expiresAt) return true;

        // Add 30 second buffer for network latency
        const bufferTime = 30 * 1000;
        return Date.now() >= (info.expiresAt - bufferTime);
    }

    /**
     * Check if token should be refreshed proactively
     */
    shouldRefreshToken(tokenInfo?: TokenInfo | null): boolean {
        const info = tokenInfo ?? this.getTokenInfo();
        if (!info || !info.expiresAt) return false;

        const timeUntilExpiry = info.expiresAt - Date.now();
        return timeUntilExpiry <= this.TOKEN_REFRESH_THRESHOLD && timeUntilExpiry > 0;
    }

    /**
     * Get milliseconds until token expires
     */
    getTimeUntilExpiry(tokenInfo?: TokenInfo | null): number {
        const info = tokenInfo ?? this.getTokenInfo();
        if (!info || !info.expiresAt) return 0;
        return Math.max(0, info.expiresAt - Date.now());
    }

    // ==================== TOKEN LIFECYCLE MANAGEMENT ====================

    /**
     * Initialize token monitoring if we have a valid token
     */
    private initializeTokenMonitoring(): void {
        const tokenInfo = this.getTokenInfo();
        if (tokenInfo && !this.isTokenExpired(tokenInfo)) {
            this.startTokenExpiryMonitoring();
            this.scheduleTokenRefresh();
        }
    }

    /**
     * Start periodic checks for token expiry
     */
    private startTokenExpiryMonitoring(): void {
        if (this.tokenExpiryCheckInterval) return; // Already monitoring

        this.tokenExpiryCheckInterval = window.setInterval(() => {
            const tokenInfo = this.getTokenInfo();

            if (!tokenInfo) {
                this.cancelScheduledRefresh();
                return;
            }

            if (this.isTokenExpired(tokenInfo)) {
                console.warn('Token expired during monitoring');
                this.handleTokenExpired();
            } else if (this.shouldRefreshToken(tokenInfo) && !this.isRefreshing) {
                console.log('Token needs refresh during periodic check');
                this.triggerProactiveRefresh();
            }
        }, this.TOKEN_EXPIRY_CHECK_INTERVAL);
    }

    /**
     * Schedule proactive token refresh before expiry
     */
    private scheduleTokenRefresh(): void {
        // Clear any existing scheduled refresh
        if (this.scheduledRefreshTimeout) {
            clearTimeout(this.scheduledRefreshTimeout);
            this.scheduledRefreshTimeout = null;
        }

        const tokenInfo = this.getTokenInfo();
        if (!tokenInfo || !tokenInfo.expiresAt) return;

        const timeUntilRefresh = Math.max(0,
            tokenInfo.expiresAt - Date.now() - this.TOKEN_REFRESH_THRESHOLD
        );

        // Only schedule if reasonable time frame (< 24 hours)
        if (timeUntilRefresh > 0 && timeUntilRefresh < 24 * 60 * 60 * 1000) {
            if (import.meta.env.DEV) {
                console.log(`Scheduling token refresh in ${Math.floor(timeUntilRefresh / 60000)} minutes`);
            }

            this.scheduledRefreshTimeout = window.setTimeout(() => {
                console.log('Executing scheduled token refresh');
                this.triggerProactiveRefresh();
            }, timeUntilRefresh);
        }
    }

    /**
     * Cancel scheduled refresh and monitoring
     */
    private cancelScheduledRefresh(): void {
        if (this.tokenExpiryCheckInterval) {
            clearInterval(this.tokenExpiryCheckInterval);
            this.tokenExpiryCheckInterval = null;
        }

        if (this.scheduledRefreshTimeout) {
            clearTimeout(this.scheduledRefreshTimeout);
            this.scheduledRefreshTimeout = null;
        }
    }

    /**
     * Trigger proactive refresh (called by monitoring/scheduled refresh)
     */
    private async triggerProactiveRefresh(): Promise<void> {
        if (this.isRefreshing) {
            console.log('Refresh already in progress, skipping');
            return;
        }

        this.isRefreshing = true;

        try {
            // Dynamically import to avoid circular dependency
            const { InterceptorManager } = await import('./interceptors');
            const { default: axios } = await import('axios');

            const success = await InterceptorManager.performProactiveRefresh(axios);

            if (success) {
                console.log('Proactive token refresh successful');
                // Token updated, reschedule next refresh
                this.scheduleTokenRefresh();
            } else {
                console.warn('Proactive token refresh failed');
            }
        } catch (error) {
            console.error('Error during proactive token refresh:', error);
        } finally {
            this.isRefreshing = false;
        }
    }

    /**
     * Handle token expiration
     */
    private handleTokenExpired(): void {
        console.warn('Access token expired');
        this.clearAll();
        window.dispatchEvent(new CustomEvent('token-expired'));

        // Prevent redirect loops
        if (!window.location.pathname.includes('/login')) {
            window.location.href = '/login';
        }
    }

    /**
     * Handle storage changes from other tabs
     */
    private handleStorageChange(event: StorageEvent): void {
        if (event.key === API_CONFIG.AUTH.TOKEN_KEY) {
            if (!event.newValue) {
                // Token removed in another tab
                console.log('Token removed in another tab');
                this.cancelScheduledRefresh();
                window.dispatchEvent(new CustomEvent('token-expired'));
            } else {
                // Token updated in another tab
                console.log('Token updated in another tab');
                this.scheduleTokenRefresh();
            }
        }
    }

    // ==================== CSRF TOKEN MANAGEMENT ====================

    getCsrfToken(): string | null {
        // Return cached token if available
        if (this.csrfToken) return this.csrfToken;

        // Try meta tag first (server-rendered)
        const metaToken = document.querySelector('meta[name="csrf-token"]')?.getAttribute('content');
        if (metaToken) {
            this.csrfToken = metaToken;
            return metaToken;
        }

        // Try session storage
        const storedToken = sessionStorage.getItem(API_CONFIG.CSRF.STORAGE_KEY);
        if (storedToken) {
            this.csrfToken = storedToken;
            return storedToken;
        }

        return null;
    }

    setCsrfToken(token: string | null): void {
        this.csrfToken = token;

        if (token) {
            // Store in session storage
            sessionStorage.setItem(API_CONFIG.CSRF.STORAGE_KEY, token);

            // Update meta tag
            let metaTag = document.querySelector('meta[name="csrf-token"]') as HTMLMetaElement;
            if (!metaTag) {
                metaTag = document.createElement('meta');
                metaTag.name = 'csrf-token';
                document.head.appendChild(metaTag);
            }
            metaTag.content = token;
        } else {
            sessionStorage.removeItem(API_CONFIG.CSRF.STORAGE_KEY);

            const metaTag = document.querySelector('meta[name="csrf-token"]');
            if (metaTag) {
                metaTag.remove();
            }
        }
    }

    // ==================== KYC SESSION MANAGEMENT ====================

    getKycSessionId(): string | null {
        return this.kycSessionId;
    }

    setKycSessionId(sessionId: string | null): void {
        this.kycSessionId = sessionId;
    }

    // ==================== UTILITY METHODS ====================

    /**
     * Parse JWT payload without verification
     */
    private parseJwtPayload(token: string): any {
        const parts = token.split('.');
        if (parts.length !== 3) {
            throw new Error('Invalid JWT format');
        }

        const payload = parts[1];

        // Handle base64url encoding
        let base64 = payload.replace(/-/g, '+').replace(/_/g, '/');

        // Add padding
        while (base64.length % 4) {
            base64 += '=';
        }

        try {
            const decoded = atob(base64);
            return JSON.parse(decoded);
        } catch (error) {
            throw new Error('Failed to decode JWT payload');
        }
    }

    /**
     * Check if user has valid authentication
     */
    isAuthenticated(): boolean {
        const tokenInfo = this.getTokenInfo();
        return tokenInfo !== null && !this.isTokenExpired(tokenInfo);
    }

    /**
     * Clear all tokens and stop monitoring
     */
    clearAll(): void {
        this.clearAccessToken();
        this.setCsrfToken(null);
        this.setKycSessionId(null);
        this.isRefreshing = false;
    }

    /**
     * Get comprehensive token status (for debugging)
     */
    getTokenStatus(): {
        hasToken: boolean;
        isExpired: boolean;
        isAuthenticated: boolean;
        timeUntilExpiry: number;
        minutesUntilExpiry: number;
        shouldRefresh: boolean;
        expiresAt: string | null;
    } {
        const tokenInfo = this.getTokenInfo();
        const timeUntilExpiry = this.getTimeUntilExpiry(tokenInfo);

        return {
            hasToken: !!tokenInfo,
            isExpired: this.isTokenExpired(tokenInfo),
            isAuthenticated: this.isAuthenticated(),
            timeUntilExpiry,
            minutesUntilExpiry: Math.floor(timeUntilExpiry / 60000),
            shouldRefresh: this.shouldRefreshToken(tokenInfo),
            expiresAt: tokenInfo ? new Date(tokenInfo.expiresAt).toISOString() : null
        };
    }

    /**
     * Manual refresh trigger (for testing/debugging)
     */
    async forceRefresh(): Promise<boolean> {
        console.log('Manual token refresh triggered');
        await this.triggerProactiveRefresh();
        return this.isAuthenticated();
    }
}

// Export singleton instance
export const tokenManager = TokenManager.getInstance();
export default tokenManager;