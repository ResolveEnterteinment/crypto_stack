// src/services/authService.ts
import apiClient, { ApiErrorHandler } from './api';
import { tokenManager } from './api/tokenManager';

// Type definitions (keep all existing interfaces unchanged)
interface CsrfResponse {
    token: string;
}
interface LoginRequest {
    email: string;
    password: string;
}

interface LoginResponse {
    accessToken: string;
    refreshToken?: string;
    userId: string;
    username: string;
    emailConfirmed: boolean;
    tokenExpiration?: string;
    roles?: string[];
    isFirstLogin?: boolean;
}

interface RegisterRequest {
    email: string;
    password: string;
    confirmPassword: string;
    firstName?: string;
    lastName?: string;
}

interface RegisterResponse {
    success: boolean;
    message: string;
    userId?: string;
    requiresEmailConfirmation?: boolean;
}

interface UserResponse {
    id: string;
    email: string;
    username: string;
    firstName?: string;
    lastName?: string;
    roles?: string[];
    emailConfirmed: boolean;
    phoneNumberConfirmed?: boolean;
    twoFactorEnabled?: boolean;
    createdAt?: string;
    lastLoginAt?: string;
}

interface PasswordResetRequest {
    email: string;
}

interface PasswordResetConfirmRequest {
    email: string;
    token: string;
    newPassword: string;
    confirmPassword: string;
}

interface ChangePasswordRequest {
    currentPassword: string;
    newPassword: string;
    confirmPassword: string;
}

class AuthService {
    private readonly AUTH_ENDPOINTS = {
        LOGIN: '/v1/auth/login',
        LOGOUT: '/v1/auth/logout',
        REGISTER: '/v1/auth/register',
        REFRESH_TOKEN: '/v1/auth/refresh-token',
        USER: '/v1/auth/user',
        CONFIRM_EMAIL: '/v1/auth/confirm-email',
        RESEND_CONFIRMATION: '/v1/auth/resend-confirmation',
        PASSWORD_RESET: '/v1/auth/reset-password',
        PASSWORD_RESET_CONFIRM: '/v1/auth/reset-password/confirm',
        PASSWORD_FORGOT: '/v1/auth/forgot-password',
        CHANGE_PASSWORD: '/v1/auth/change-password',
        TWO_FACTOR_ENABLE: '/v1/auth/2fa/enable',
        TWO_FACTOR_DISABLE: '/v1/auth/2fa/disable',
        TWO_FACTOR_VERIFY: '/v1/auth/2fa/verify'
    } as const;

    private readonly STORAGE_KEYS = {
        USER: 'user'
    } as const;

    private refreshAttempts = 0;
    private readonly MAX_REFRESH_ATTEMPTS = 3;
    private refreshInProgress = false;

    /**
 * ✅ ENHANCED: Initialize authentication service with debugging and better error handling
 */
    async initialize(): Promise<UserResponse | null> {
        try {
            console.log('🔄 Initializing AuthService...');

            // Clear any invalid auth state first
            await this.cleanupInvalidTokens();

            // Check for valid authentication
            if (!tokenManager.isAuthenticated()) {
                console.log("❌ No valid authentication found");
                return null;
            }

            console.log("✅ Found existing auth token, validating...");

            // Ensure token is set in API client
            const token = tokenManager.getAccessToken();
            if (token) {
                apiClient.setAuthToken(token);
                console.log("🔐 Token set in API client");
            }

            // Validate token by fetching user data with retry logic
            let retryCount = 0;
            const maxRetries = 3;

            while (retryCount < maxRetries) {
                try {
                    const user = await this.checkAuthStatus();
                    if (user) {
                        console.log("✅ Authentication initialized successfully");
                        return user;
                    }
                    break; // Exit retry loop if user check returns null (not an error)
                } catch (error) {
                    retryCount++;
                    console.warn(`Auth check attempt ${retryCount} failed:`, error);

                    if (retryCount < maxRetries) {
                        // Wait before retry with exponential backoff
                        const delay = Math.min(1000 * Math.pow(2, retryCount - 1), 5000);
                        await new Promise(resolve => setTimeout(resolve, delay));
                    } else {
                        console.error("All auth check attempts failed");
                        break;
                    }
                }
            }

            console.log("❌ Token validation failed");
            this.clearAuthentication();
            return null;
        } catch (error) {
            console.error('❌ Failed to initialize auth service:', error);
            this.clearAuthentication();
            return null;
        }
    }

    /**
     * ✅ NEW: Clean up invalid tokens
     */
    private async cleanupInvalidTokens(): Promise<void> {
        const tokenInfo = tokenManager.getTokenInfo();
        if (tokenInfo && tokenManager.isTokenExpired(tokenInfo)) {
            console.log("🧹 Cleaning up expired token");
            tokenManager.clearAll();
            apiClient.clearTokens();
        }
    }

    /**
     * ✅ ENHANCED: Login with better debugging
     */
    async login(email: string, password: string): Promise<LoginResponse> {
        try {
            console.log('🔐 Starting login process...');

            // Clear any existing auth before login
            this.clearAuthentication();

            // Perform login
            const response = await apiClient.post<LoginResponse>(
                this.AUTH_ENDPOINTS.LOGIN,
                { email, password } as LoginRequest,
                {
                    skipAuth: true,
                    priority: 'high',
                    retryCount: 0,
                    idempotencyKey: `login-${Date.now()}`
                }
            );

            console.log('📤 Login response received:', {
                success: response.success,
                hasToken: !!response.data?.accessToken,
                tokenLength: response.data?.accessToken?.length
            });

            if (response.success && response.data.accessToken) {
                // Store authentication data
                this.storeAuthentication(response.data);

                console.log("✅ Login successful, auth data stored");
                return response.data;
            } else {
                throw new Error(response.message || 'Login failed');
            }
        } catch (error) {
            console.error("❌ Login error:", error);

            const apiError = ApiErrorHandler.extractError(error);
            const userMessage = ApiErrorHandler.formatUserMessage(apiError);

            throw new Error(userMessage);
        }
    }

    // Keep all other methods unchanged but add better debugging to storeAuthentication
    /**
     * ✅ ENHANCED: Store authentication data with debugging
     */
    private storeAuthentication(data: LoginResponse): void {
        console.log("📦 Storing authentication data...");
        console.log("🎫 Token length:", data.accessToken?.length);

        // Store token using tokenManager
        tokenManager.setAccessToken(data.accessToken);

        // Store user data
        const userData = {
            id: data.userId,
            username: data.username,
            email: data.username,
            roles: data.roles,
            emailConfirmed: data.emailConfirmed
        };

        this.storeUserData(userData);

        // Update API client with the token
        apiClient.setAuthToken(data.accessToken);

        // Verify storage worked
        const storedToken = tokenManager.getAccessToken();
        const isClientAuth = apiClient.isAuthenticated();

        console.log("✅ Authentication data stored:");
        console.log("  - Token stored:", !!storedToken);
        console.log("  - Token matches:", storedToken === data.accessToken);
        console.log("  - API client authenticated:", isClientAuth);
        console.log("  - TokenManager authenticated:", tokenManager.isAuthenticated());
    }

    /**
     * ✅ ENHANCED: Auth status check with better debugging
     */
    async checkAuthStatus(): Promise<UserResponse | null> {
        console.log("🔍 Checking auth status...");

        // First check if we have a valid token
        const tokenInfo = tokenManager.getTokenInfo();
        if (!tokenInfo) {
            console.log("❌ No authentication token found");
            return null;
        }

        console.log("🎫 Token info:", {
            hasToken: !!tokenInfo.token,
            expiresAt: new Date(tokenInfo.expiresAt).toLocaleString(),
            isExpired: tokenManager.isTokenExpired(tokenInfo)
        });

        // If token is expired, try refresh first
        if (tokenManager.isTokenExpired(tokenInfo)) {
            console.log("⏰ Token expired, attempting refresh...");
            const refreshed = await this.refreshToken();
            if (!refreshed) {
                this.clearAuthentication();
                return null;
            }
        }

        try {
            // Ensure token is set in API client
            const currentToken = tokenManager.getAccessToken();

            if (currentToken) {
                apiClient.setAuthToken(currentToken);
            } else {
                console.log("❌ No current token available for auth status check");
                return null;
            }

            console.log("📤 Making auth status request...");

            const response = await apiClient.get<UserResponse>(
                this.AUTH_ENDPOINTS.USER,
                {
                    dedupe: true,
                    dedupeKey: 'auth-status-check',
                    priority: 'high'
                }
            );

            console.log("📥 Auth status response:", {
                success: response.success,
                hasData: !!response.data
            });

            if (response.success && response.data) {
                console.log("✅ Auth check successful");
                this.storeUserData(response.data);
                return response.data;
            } else {
                console.log("❌ Auth check failed: Invalid response");
                this.clearAuthentication();
                return null;
            }
        } catch (error) {
            console.error("❌ Auth check error:", error);

            const apiError = ApiErrorHandler.extractError(error);

            if (apiError.isAuthError) {
                console.log("🔒 Auth check failed: Token invalid");
                this.clearAuthentication();
            }

            return null;
        }
    }

    // Keep all other methods unchanged...
    // (logout, register, refreshToken, clearAuthenticationWithoutLogout, etc.)

    async logout(): Promise<void> {
        try {
            const hasToken = tokenManager.isAuthenticated();

            if (hasToken) {
                apiClient.post(
                    this.AUTH_ENDPOINTS.LOGOUT,
                    undefined,
                    {
                        priority: 'high',
                        retryCount: 0,
                        debounceMs: 0,
                        skipAuth: false
                    }
                ).catch(err => {
                    console.warn("Backend logout failed:", err);
                });
            }

            console.log("Logout initiated");
        } catch (error) {
            console.error('Logout error:', error);
        } finally {
            this.clearAuthenticationWithoutLogout();
        }
    }

    private clearAuthenticationWithoutLogout(): void {
        console.log("Clearing authentication data");

        this.refreshAttempts = 0;
        this.refreshInProgress = false;

        tokenManager.clearAll();
        localStorage.removeItem(this.STORAGE_KEYS.USER);
        sessionStorage.removeItem(this.STORAGE_KEYS.USER);
        apiClient.clearTokens();
    }

    async refreshToken(): Promise<boolean> {
        try {
            if (this.refreshInProgress) {
                return false;
            }

            this.refreshInProgress = true;

            const tokenInfo = tokenManager.getTokenInfo();
            if (!tokenInfo) {
                this.refreshAttempts = 0;
                return false;
            }

            if (tokenManager.isTokenExpired(tokenInfo)) {
                console.log('Token is expired, attempting refresh');
            }

            if (this.refreshAttempts >= this.MAX_REFRESH_ATTEMPTS) {
                console.log("Max refresh attempts exceeded, clearing authentication");
                this.clearAuthenticationWithoutLogout();
                return false;
            }

            const backoffDelay = Math.min(1000 * Math.pow(2, this.refreshAttempts), 30000);
            if (this.refreshAttempts > 0) {
                console.log(`Waiting ${backoffDelay}ms before refresh attempt ${this.refreshAttempts + 1}`);
                await new Promise(resolve => setTimeout(resolve, backoffDelay));
            }

            this.refreshAttempts++;

            const { InterceptorManager } = await import('./api/interceptors');
            const { default: axios } = await import('axios');

            const success = await InterceptorManager.performTokenRefresh(axios);

            if (success) {
                this.refreshAttempts = 0;
                console.log("✅ Token refresh successful via AuthService");
                return true;
            }

            return false;
        } catch (error) {
            console.error("❌ Token refresh failed via AuthService:", error);
            return false;
        } finally {
            this.refreshInProgress = false;
        }
    }

    private storeUserData(user: any): void {
        localStorage.setItem(this.STORAGE_KEYS.USER, JSON.stringify(user));
        sessionStorage.setItem(this.STORAGE_KEYS.USER, JSON.stringify(user));
    }

    private clearAuthentication(): void {
        this.clearAuthenticationWithoutLogout();
    }

    isAuthenticated(): boolean {
        return tokenManager.isAuthenticated();
    }

    getCurrentUser(): any {
        const userStr = localStorage.getItem(this.STORAGE_KEYS.USER) ||
            sessionStorage.getItem(this.STORAGE_KEYS.USER);

        if (userStr) {
            try {
                return JSON.parse(userStr);
            } catch {
                return null;
            }
        }

        return null;
    }

    getAuthState(): {
        isAuthenticated: boolean;
        hasToken: boolean;
        tokenExpired: boolean;
        user: any;
        tokenStatus: any;
    } {
        return {
            isAuthenticated: this.isAuthenticated(),
            hasToken: !!tokenManager.getAccessToken(),
            tokenExpired: tokenManager.isTokenExpired(),
            user: this.getCurrentUser(),
            tokenStatus: tokenManager.getTokenStatus()
        };
    }

    // Keep all remaining methods unchanged (password reset, email verification, etc.)
    async register(data: RegisterRequest): Promise<RegisterResponse> {
        try {
            const response = await apiClient.post<RegisterResponse>(
                this.AUTH_ENDPOINTS.REGISTER,
                data,
                {
                    skipAuth: true,
                    priority: 'high',
                    retryCount: 0
                }
            );

            if (response.success) {
                console.log("Registration successful");
                return response.data;
            } else {
                throw new Error(response.message || 'Registration failed');
            }
        } catch (error) {
            console.error("Registration error:", error);

            const apiError = ApiErrorHandler.extractError(error);
            const userMessage = ApiErrorHandler.formatUserMessage(apiError);

            throw new Error(userMessage);
        }
    }

    async requestPasswordReset(email: string): Promise<void> {
        try {
            const response = await apiClient.post(
                this.AUTH_ENDPOINTS.PASSWORD_RESET,
                { email } as PasswordResetRequest,
                {
                    skipAuth: true,
                    retryCount: 0
                }
            );

            if (!response.success) {
                throw new Error(response.message || 'Password reset request failed');
            }
        } catch (error) {
            const apiError = ApiErrorHandler.extractError(error);
            throw new Error(ApiErrorHandler.formatUserMessage(apiError));
        }
    }

    async confirmPasswordReset(data: PasswordResetConfirmRequest): Promise<void> {
        try {
            const response = await apiClient.post(
                this.AUTH_ENDPOINTS.PASSWORD_RESET_CONFIRM,
                data,
                {
                    skipAuth: true,
                    retryCount: 0
                }
            );

            if (!response.success) {
                throw new Error(response.message || 'Password reset failed');
            }
        } catch (error) {
            const apiError = ApiErrorHandler.extractError(error);
            throw new Error(ApiErrorHandler.formatUserMessage(apiError));
        }
    }

    async changePassword(data: ChangePasswordRequest): Promise<void> {
        try {
            const response = await apiClient.post(
                this.AUTH_ENDPOINTS.CHANGE_PASSWORD,
                data,
                {
                    priority: 'high',
                    retryCount: 0
                }
            );

            if (!response.success) {
                throw new Error(response.message || 'Password change failed');
            }
        } catch (error) {
            const apiError = ApiErrorHandler.extractError(error);
            throw new Error(ApiErrorHandler.formatUserMessage(apiError));
        }
    }

    async verifyEmail(token: string, userId: string): Promise<void> {
        try {
            const response = await apiClient.post(
                this.AUTH_ENDPOINTS.CONFIRM_EMAIL,
                { token, userId },
                {
                    skipAuth: true,
                    retryCount: 0
                }
            );

            if (!response.success) {
                throw new Error(response.message || 'Email verification failed');
            }
        } catch (error) {
            const apiError = ApiErrorHandler.extractError(error);
            throw new Error(ApiErrorHandler.formatUserMessage(apiError));
        }
    }

    async resendVerificationEmail(email: string): Promise<void> {
        try {
            const response = await apiClient.post(
                this.AUTH_ENDPOINTS.RESEND_CONFIRMATION,
                { email },
                {
                    skipAuth: true,
                    retryCount: 0,
                    throttleMs: 60000
                }
            );

            if (!response.success) {
                throw new Error(response.message || 'Failed to resend verification email');
            }
        } catch (error) {
            const apiError = ApiErrorHandler.extractError(error);
            throw new Error(ApiErrorHandler.formatUserMessage(apiError));
        }
    }
}

// Export singleton instance
export const authService = new AuthService();

// Export type definitions
export type {
    ChangePasswordRequest, CsrfResponse, LoginRequest,
    LoginResponse, PasswordResetConfirmRequest, PasswordResetRequest, RegisterRequest,
    RegisterResponse,
    UserResponse
};