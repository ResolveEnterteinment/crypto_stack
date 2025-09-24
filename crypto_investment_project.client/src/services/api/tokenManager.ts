// Simplified Token Manager - Drop-in replacement for your current tokenManager.ts
// src/services/api/tokenManager.ts

import { API_CONFIG } from "./config";

interface TokenInfo {
    token: string;
    expiresAt: number; // Unix timestamp
    issuedAt: number;
}

// Enhanced Token Manager with proactive lifecycle management
class TokenManager {
    private static instance: TokenManager;
    private refreshPromise: Promise<boolean> | null = null;
    private csrfToken: string | null = null;
    private kycSessionId: string | null = null;
    private tokenExpiryCheckInterval: number | null = null;
    private scheduledRefreshTimeout: number | null = null;
    private readonly TOKEN_REFRESH_THRESHOLD = 5 * 60 * 1000; // 5 minutes before expiry
    private readonly TOKEN_EXPIRY_CHECK_INTERVAL = 30 * 1000; // Check every 30 seconds

    static getInstance(): TokenManager {
        if (!TokenManager.instance) {
            TokenManager.instance = new TokenManager();
        }
        return TokenManager.instance;
    }

    constructor() {
        // Only start monitoring if we have a token
        this.initializeTokenMonitoring();
    }

    // Initialize monitoring only if token exists
    private initializeTokenMonitoring(): void {
        const tokenInfo = this.getTokenInfo();
        if (tokenInfo && !this.isTokenExpired(tokenInfo)) {
            this.startTokenExpiryMonitoring();
        }
    }

    // Get token with expiry information
    getAccessToken(): string | null {
        const tokenInfo = this.getTokenInfo();
        if (!tokenInfo) return null;

        // Check if token is expired
        if (this.isTokenExpired(tokenInfo)) {
            console.warn('Access token is expired, removing from storage');
            this.setAccessToken(null);
            return null;
        }

        return tokenInfo.token;
    }

    // Get detailed token information with expiry
    getTokenInfo(): TokenInfo | null {
        const token = localStorage.getItem(API_CONFIG.AUTH.TOKEN_KEY);
        if (!token) return null;

        try {
            const payload = this.parseJwtPayload(token);
            return {
                token,
                expiresAt: payload.exp * 1000, // Convert to milliseconds
                issuedAt: payload.iat * 1000
            };
        } catch (error) {
            console.warn('Failed to parse token payload:', error);
            // Clear invalid token immediately
            this.setAccessToken(null);
            return null;
        }
    }

    // Parse JWT payload without verification
    private parseJwtPayload(token: string): any {
        const parts = token.split('.');
        if (parts.length !== 3) {
            throw new Error('Invalid JWT format');
        }

        const payload = parts[1];
        // Better base64 padding handling
        let base64 = payload.replace(/-/g, '+').replace(/_/g, '/');
        while (base64.length % 4) {
            base64 += '=';
        }

        const decoded = atob(base64);
        return JSON.parse(decoded);
    }

    // Check if token is expired
    isTokenExpired(tokenInfo?: TokenInfo | null): boolean {
        const info = tokenInfo ?? this.getTokenInfo();
        if (!info || !info.expiresAt) return true;

        // Add buffer time for network latency
        const bufferTime = 30 * 1000; // 30 seconds buffer
        return Date.now() >= (info.expiresAt - bufferTime);
    }

    // Check if token should be refreshed
    shouldRefreshToken(tokenInfo?: TokenInfo | null): boolean {
        const info = tokenInfo ?? this.getTokenInfo();
        if (!info || !info.expiresAt) return false;

        const timeUntilExpiry = info.expiresAt - Date.now();
        return timeUntilExpiry <= this.TOKEN_REFRESH_THRESHOLD && timeUntilExpiry > 0;
    }

    // Get time until token expires
    getTimeUntilExpiry(tokenInfo?: TokenInfo | null): number {
        const info = tokenInfo ?? this.getTokenInfo();
        if (!info || !info.expiresAt) return 0;

        return Math.max(0, info.expiresAt - Date.now());
    }

    // Set access token
    setAccessToken(token: string | null): void {
        if (token) {
            localStorage.setItem(API_CONFIG.AUTH.TOKEN_KEY, token);

            // Start monitoring and schedule refresh
            this.startTokenExpiryMonitoring();
            this.scheduleTokenRefresh();
        } else {
            localStorage.removeItem(API_CONFIG.AUTH.TOKEN_KEY);
            this.cancelScheduledRefresh();
        }
    }

    // Schedule proactive token refresh
    private scheduleTokenRefresh(): void {
        // Clear any existing scheduled refresh
        if (this.scheduledRefreshTimeout) {
            clearTimeout(this.scheduledRefreshTimeout);
            this.scheduledRefreshTimeout = null;
        }

        try {
            const tokenInfo = this.getTokenInfo();
            if (!tokenInfo || !tokenInfo.expiresAt) return;

            const timeUntilRefresh = Math.max(0,
                tokenInfo.expiresAt - Date.now() - this.TOKEN_REFRESH_THRESHOLD
            );

            if (timeUntilRefresh > 0 && timeUntilRefresh < 24 * 60 * 60 * 1000) { // Max 24 hours
                this.scheduledRefreshTimeout = window.setTimeout(() => {
                    console.log('🔄 Proactively refreshing token before expiry');
                    this.triggerProactiveRefresh();
                }, timeUntilRefresh);
            }
        } catch (error) {
            console.warn('Failed to schedule token refresh:', error);
        }
    }

    // Proactive refresh trigger
    private async triggerProactiveRefresh(): Promise<void> {
        if (this.refreshPromise) return; // Already refreshing

        try {
            // Import dynamically to avoid circular dependency
            const { InterceptorManager } = await import('./interceptors');
            const { default: axios } = await import('axios');

            await InterceptorManager.performProactiveRefresh(axios);
        } catch (error) {
            console.error('Proactive token refresh failed:', error);
        }
    }

    // Start monitoring token expiry
    private startTokenExpiryMonitoring(): void {
        if (this.tokenExpiryCheckInterval) return; // Already monitoring

        this.tokenExpiryCheckInterval = window.setInterval(() => {
            const tokenInfo = this.getTokenInfo();
            if (!tokenInfo) {
                this.cancelScheduledRefresh();
                return;
            }

            if (this.isTokenExpired(tokenInfo)) {
                console.warn('Token expired during monitoring, clearing tokens');
                this.clearAll();
                window.dispatchEvent(new CustomEvent('token-expired'));
            } else if (this.shouldRefreshToken(tokenInfo)) {
                console.log('Token needs refresh during monitoring');
                this.triggerProactiveRefresh();
            }
        }, this.TOKEN_EXPIRY_CHECK_INTERVAL);
    }

    // Cancel scheduled refresh
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

    // CSRF Token Management
    getCsrfToken(): string | null {
        if (this.csrfToken) return this.csrfToken;

        const metaToken = document.querySelector('meta[name="csrf-token"]')?.getAttribute('content');
        if (metaToken) {
            this.csrfToken = metaToken;
            return metaToken;
        }

        const storedToken = sessionStorage.getItem(API_CONFIG.CSRF.STORAGE_KEY);
        if (storedToken) {
            this.csrfToken = storedToken;
        }

        return this.csrfToken;
    }

    setCsrfToken(token: string | null): void {
        this.csrfToken = token;

        if (token) {
            sessionStorage.setItem(API_CONFIG.CSRF.STORAGE_KEY, token);

            let metaTag = document.querySelector('meta[name="csrf-token"]') as HTMLMetaElement;
            if (!metaTag) {
                metaTag = document.createElement('meta');
                metaTag.name = 'csrf-token';
                document.head.appendChild(metaTag);
            }
            metaTag.content = token;
        } else {
            sessionStorage.removeItem(API_CONFIG.CSRF.STORAGE_KEY);
        }
    }

    // KYC Session Management
    getKycSessionId(): string | null {
        return this.kycSessionId;
    }

    setKycSessionId(sessionId: string | null): void {
        this.kycSessionId = sessionId;
    }

    // Clear all tokens with cleanup
    clearAll(): void {
        this.setAccessToken(null);
        this.setCsrfToken(null);
        this.setKycSessionId(null);
        this.refreshPromise = null;
        this.cancelScheduledRefresh();
    }

    // Clear just the tokens
    clearTokens(): void {
        this.setAccessToken(null);
        this.cancelScheduledRefresh();
    }

    // Refresh promise management
    getRefreshPromise(): Promise<boolean> | null {
        return this.refreshPromise;
    }

    setRefreshPromise(promise: Promise<boolean> | null): void {
        this.refreshPromise = promise;
    }

    // Check if user has valid authentication
    isAuthenticated(): boolean {
        const tokenInfo = this.getTokenInfo();
        return tokenInfo !== null && !this.isTokenExpired(tokenInfo);
    }

    // Get token status for debugging
    getTokenStatus(): {
        hasToken: boolean;
        isExpired: boolean;
        timeUntilExpiry: number;
        shouldRefresh: boolean;
    } {
        const tokenInfo = this.getTokenInfo();
        return {
            hasToken: !!tokenInfo,
            isExpired: this.isTokenExpired(tokenInfo),
            timeUntilExpiry: this.getTimeUntilExpiry(tokenInfo),
            shouldRefresh: this.shouldRefreshToken(tokenInfo)
        };
    }
}

export const tokenManager = TokenManager.getInstance();
export default tokenManager;