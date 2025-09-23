// src/services/authService.ts
import apiClient, { ApiErrorHandler } from './api';

// Type definitions

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
        ACCESS_TOKEN: 'access_token',
        REFRESH_TOKEN: 'refresh_token',
        USER: 'user'
    } as const;

    private refreshAttempts = 0;
    private readonly MAX_REFRESH_ATTEMPTS = 3;
    private refreshInProgress = false;

    /**
     * Initialize authentication service
     * Checks for existing authentication and validates token
     */
    async initialize(): Promise<UserResponse | null> {
        try {
            // Check if user is already authenticated
            const token = this.getStoredToken();

            if (token) {
                console.log("Found existing auth token, validating...");

                // Set token in API client
                apiClient.setAuthToken(token);

                // Validate token by fetching user data
                const user = await this.checkAuthStatus();

                if (user) {
                    console.log("Authentication initialized successfully");
                    return user;
                }
            }

            console.log("No valid authentication found");
            return null;
        } catch (error) {
            console.error('Failed to initialize auth service:', error);

            // Clear invalid tokens
            this.clearAuthentication();
            return null;
        }
    }

    /**
     * Login with email and password
     */
    async login(email: string, password: string): Promise<LoginResponse> {
        try {
            // Clear any existing auth before login
            this.clearAuthentication();

            // Perform login with high priority and no retry for security
            const response = await apiClient.post<LoginResponse>(
                this.AUTH_ENDPOINTS.LOGIN,
                { email, password } as LoginRequest,
                {
                    skipAuth: true, // Don't send auth header for login
                    priority: 'high',
                    retryCount: 0, // No retry for login attempts
                    idempotencyKey: `login-${Date.now()}` // Unique key for each login
                }
            );

            if (response.success && response.data.accessToken) {
                // Store authentication data
                this.storeAuthentication(response.data);

                console.log("Login successful");
                return response.data;
            } else {
                throw new Error(response.message || 'Login failed');
            }
        } catch (error) {
            console.error("Login error:", error);

            // Format error for user display
            const apiError = ApiErrorHandler.extractError(error);
            const userMessage = ApiErrorHandler.formatUserMessage(apiError);

            throw new Error(userMessage);
        }
    }

    /**
     * Register a new user
     */
    async register(data: RegisterRequest): Promise<RegisterResponse> {
        try {
            const response = await apiClient.post<RegisterResponse>(
                this.AUTH_ENDPOINTS.REGISTER,
                data,
                {
                    skipAuth: true,
                    priority: 'high',
                    retryCount: 0 // No retry for registration
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

    /**
     * Logout user
     */
    async logout(): Promise<void> {
        try {
            // ✅ FIXED: Only call logout endpoint if we have a valid token
            const hasToken = this.getStoredToken();

            if (hasToken) {
                // Call logout endpoint to invalidate server-side session
                // Use fire-and-forget approach - don't wait for response
                apiClient.post(
                    this.AUTH_ENDPOINTS.LOGOUT,
                    undefined,
                    {
                        priority: 'high',
                        retryCount: 0,
                        debounceMs: 0, // Execute immediately
                        skipAuth: false // ✅ FIXED: Allow auth header for logout
                    }
                ).catch(err => {
                    // Log but don't throw - local cleanup should still happen
                    console.warn("Backend logout failed:", err);
                });
            }

            console.log("Logout initiated");
        } catch (error) {
            console.error('Logout error:', error);
        } finally {
            // ✅ FIXED: Always clear local authentication without recursion
            this.clearAuthenticationWithoutLogout();
        }
    }

    /**
 * Clear authentication data without calling logout endpoint
 */
    private clearAuthenticationWithoutLogout(): void {
        console.log("Clearing authentication data");

        // Reset refresh attempts
        this.refreshAttempts = 0;
        this.refreshInProgress = false;

        // Clear localStorage (only access token and user data)
        localStorage.removeItem(this.STORAGE_KEYS.ACCESS_TOKEN);
        localStorage.removeItem(this.STORAGE_KEYS.USER);

        // Clear sessionStorage
        sessionStorage.removeItem(this.STORAGE_KEYS.ACCESS_TOKEN);
        sessionStorage.removeItem(this.STORAGE_KEYS.USER);

        // Clear API client tokens
        apiClient.clearTokens();
    }

    /**
     * Check if user is authenticated and get user data
     */
    async checkAuthStatus(): Promise<UserResponse | null> {
        const token = this.getStoredToken();

        if (!token) {
            console.log("No authentication token found");
            return null;
        }

        try {
            // Ensure token is set in API client
            apiClient.setAuthToken(token);

            // Fetch user data with deduplication to prevent multiple simultaneous checks
            const response = await apiClient.get<UserResponse>(
                this.AUTH_ENDPOINTS.USER,
                {
                    dedupe: true,
                    dedupeKey: 'auth-status-check',
                    priority: 'high'
                }
            );

            if (response.success && response.data) {
                console.log("Auth check successful");

                // Update stored user data
                this.storeUserData(response.data);

                return response.data;
            } else {
                console.log("Auth check failed: Invalid response");
                this.clearAuthentication();
                return null;
            }
        } catch (error) {
            const apiError = ApiErrorHandler.extractError(error);

            // Clear auth on 401 errors
            if (apiError.isAuthError) {
                console.log("Auth check failed: Token invalid");
                this.clearAuthentication();
            } else {
                console.error("Auth check error:", error);
            }

            return null;
        }
    }

    /**
     * Refresh the authentication token with exponential backoff - FIXED
     */
    async refreshToken(): Promise<boolean> {
        try {
            // Prevent concurrent refresh attempts
            if (this.refreshInProgress) {
                return false;
            }

            this.refreshInProgress = true;

            const currentToken = this.getStoredToken();
            if (!currentToken) {
                this.refreshAttempts = 0;
                return false;
            }

            // Check if we've exceeded max attempts
            if (this.refreshAttempts >= this.MAX_REFRESH_ATTEMPTS) {
                console.log("Max refresh attempts exceeded, clearing authentication");
                this.clearAuthenticationWithoutLogout(); // ✅ FIXED: Don't call logout()
                return false;
            }

            // Calculate exponential backoff delay
            const backoffDelay = Math.min(1000 * Math.pow(2, this.refreshAttempts), 30000);
            if (this.refreshAttempts > 0) {
                console.log(`Waiting ${backoffDelay}ms before refresh attempt ${this.refreshAttempts + 1}`);
                await new Promise(resolve => setTimeout(resolve, backoffDelay));
            }

            this.refreshAttempts++;

            // Use deduplication to prevent concurrent refresh attempts
            const response = await apiClient.post<LoginResponse>(
                this.AUTH_ENDPOINTS.REFRESH_TOKEN,
                undefined,
                {
                    dedupe: true,
                    dedupeKey: 'token-refresh',
                    priority: 'high',
                    retryCount: 0, // No API-level retry, we handle it here
                    headers: {
                        'Authorization': `Bearer ${currentToken}`
                    },
                    skipAuth: false // ✅ FIXED: Don't skip auth for refresh token requests
                }
            );

            if (response.success && response.data.accessToken) {
                // Reset attempts on success
                this.refreshAttempts = 0;

                // Update stored authentication (only access token)
                this.storeAuthentication(response.data);

                console.log("Token refresh successful");
                return true;
            }

            return false;
        } catch (error) {
            console.error("Token refresh failed:", error);

            const apiError = ApiErrorHandler.extractError(error);

            // Clear auth immediately on auth errors (401, 403)
            if (apiError.isAuthError) {
                console.log("Auth error during refresh, clearing authentication");
                this.refreshAttempts = 0;
                this.clearAuthenticationWithoutLogout(); // ✅ FIXED: Don't call logout()
                return false;
            }

            // For other errors, don't clear auth immediately - let backoff handle it
            return false;

        } finally {
            this.refreshInProgress = false;
        }
    }

    /**
     * Request password reset
     */
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

    /**
     * Confirm password reset with token
     */
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

    /**
     * Change password for authenticated user
     */
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

    /**
     * Verify email with token
     */
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

    /**
     * Resend email verification
     */
    async resendVerificationEmail(email: string): Promise<void> {
        try {
            const response = await apiClient.post(
                this.AUTH_ENDPOINTS.RESEND_CONFIRMATION,
                { email },
                {
                    skipAuth: true,
                    retryCount: 0,
                    throttleMs: 60000 // Throttle to once per minute
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

    // ==================== Helper Methods ====================

    /**
 * Store authentication data
 */
    private storeAuthentication(data: LoginResponse): void {
        console.log("Storing authentication data");

        // Store access token in localStorage
        localStorage.setItem(this.STORAGE_KEYS.ACCESS_TOKEN, data.accessToken);
        sessionStorage.setItem(this.STORAGE_KEYS.ACCESS_TOKEN, data.accessToken); // Backup

        // ❌ REMOVE: Don't store refresh token in localStorage
        // if (data.refreshToken) {
        //     localStorage.setItem(this.STORAGE_KEYS.REFRESH_TOKEN, data.refreshToken);
        // }

        // ✅ NEW: Refresh token is now handled by HTTP-only cookies on backend
        // No need to store refresh token on frontend

        // Store user data
        const userData = {
            id: data.userId,
            username: data.username,
            email: data.username, // Assuming username is email
            roles: data.roles,
            emailConfirmed: data.emailConfirmed
        };

        this.storeUserData(userData);

        // Update API client
        apiClient.setAuthToken(data.accessToken);
    }

    /**
     * Store user data
     */
    private storeUserData(user: any): void {
        localStorage.setItem(this.STORAGE_KEYS.USER, JSON.stringify(user));
        sessionStorage.setItem(this.STORAGE_KEYS.USER, JSON.stringify(user)); // Backup
    }

    /**
     * Get stored authentication token
     */
    private getStoredToken(): string | null {
        return localStorage.getItem(this.STORAGE_KEYS.ACCESS_TOKEN) ||
            sessionStorage.getItem(this.STORAGE_KEYS.ACCESS_TOKEN);
    }

    /**
     * Clear all authentication data - FIXED to prevent recursion
     */
    private clearAuthentication(): void {
        console.log("Clearing authentication data");

        // Reset refresh attempts
        this.refreshAttempts = 0;
        this.refreshInProgress = false;

        // Clear localStorage (only access token and user data)
        localStorage.removeItem(this.STORAGE_KEYS.ACCESS_TOKEN);
        localStorage.removeItem(this.STORAGE_KEYS.USER);

        // Clear sessionStorage
        sessionStorage.removeItem(this.STORAGE_KEYS.ACCESS_TOKEN);
        sessionStorage.removeItem(this.STORAGE_KEYS.USER);

        // Clear API client tokens
        apiClient.clearTokens();

        // ✅ FIXED: DON'T call logout() here to prevent recursion
        // The logout() method should be called explicitly by user action
    }

    /**
     * Check if user is currently authenticated
     */
    isAuthenticated(): boolean {
        return apiClient.isAuthenticated();
    }

    /**
     * Get current user from storage
     */
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
