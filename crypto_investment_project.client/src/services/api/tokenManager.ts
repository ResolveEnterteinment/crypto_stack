import { API_CONFIG } from "./config";

// src/services/api/tokenManager.ts
class TokenManager {
    private static instance: TokenManager;
    private refreshPromise: Promise<boolean> | null = null;
    private csrfToken: string | null = null;
    private kycSessionId: string | null = null;

    static getInstance(): TokenManager {
        if (!TokenManager.instance) {
            TokenManager.instance = new TokenManager();
        }
        return TokenManager.instance;
    }

    // Access Token Management
    getAccessToken(): string | null {
        return localStorage.getItem(API_CONFIG.AUTH.TOKEN_KEY);
    }

    setAccessToken(token: string | null): void {
        if (token) {
            localStorage.setItem(API_CONFIG.AUTH.TOKEN_KEY, token);
        } else {
            localStorage.removeItem(API_CONFIG.AUTH.TOKEN_KEY);
        }
    }

    // Refresh Token Management
    getRefreshToken(): string | null {
        return localStorage.getItem(API_CONFIG.AUTH.REFRESH_TOKEN_KEY);
    }

    setRefreshToken(token: string | null): void {
        if (token) {
            localStorage.setItem(API_CONFIG.AUTH.REFRESH_TOKEN_KEY, token);
        } else {
            localStorage.removeItem(API_CONFIG.AUTH.REFRESH_TOKEN_KEY);
        }
    }

    // CSRF Token Management
    getCsrfToken(): string | null {
        if (this.csrfToken) return this.csrfToken;

        // Try meta tag first
        const metaToken = document.querySelector('meta[name="csrf-token"]')?.getAttribute('content');
        if (metaToken) {
            this.csrfToken = metaToken;
            return metaToken;
        }

        // Fallback to session storage
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
        }
    }

    // KYC Session Management
    getKycSessionId(): string | null {
        return this.kycSessionId;
    }

    setKycSessionId(sessionId: string | null): void {
        this.kycSessionId = sessionId;
    }

    // Clear all tokens
    clearAll(): void {
        this.setAccessToken(null);
        this.setRefreshToken(null);
        this.setCsrfToken(null);
        this.setKycSessionId(null);
        this.refreshPromise = null;
    }

    // Get/Set refresh promise for preventing race conditions
    getRefreshPromise(): Promise<boolean> | null {
        return this.refreshPromise;
    }

    setRefreshPromise(promise: Promise<boolean> | null): void {
        this.refreshPromise = promise;
    }
}

export const tokenManager = TokenManager.getInstance();