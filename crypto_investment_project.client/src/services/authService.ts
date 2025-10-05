// src/services/authService.ts
import api from "./api";

// ==================== TYPES ====================

export interface LoginRequest {
    email: string;
    password: string;
}

export interface RegisterRequest {
    email: string;
    password: string;
    fullName: string;
}

export interface LoginResponse {
    accessToken: string;
    refreshToken?: string; // ✅ Ignored - in HTTP-only cookie
    userId: string;
    username: string;
    emailConfirmed: boolean;
    tokenExpiration?: string;
    roles?: string[];
    isFirstLogin?: boolean;
}

export interface UserDataResponse {
    id: string;
    email: string;
    username: string;
    emailConfirmed: boolean;
    roles: string[];
    isFirstLogin?: boolean;
}

export interface CsrfResponse {
    token: string;
}

export interface ForgotPasswordRequest {
    email: string;
}

export interface ResetPasswordRequest {
    email: string;
    token: string;
    newPassword: string;
}

export interface ConfirmEmailRequest {
    token: string;
}

export interface ResendConfirmationRequest {
    email: string;
}

export interface AddRoleRequest {
    userId: string;
    role: string;
}

export interface CreateRoleRequest {
    role: string;
}

// ==================== ENDPOINTS ====================

const ENDPOINTS = {
    // Authentication
    LOGIN: '/v1/auth/login',
    REGISTER: '/v1/auth/register',
    LOGOUT: '/v1/auth/logout',
    REFRESH_TOKEN: '/v1/auth/refresh-token',

    // User
    CURRENT_USER: '/v1/auth/user',

    // Email verification
    CONFIRM_EMAIL: '/v1/auth/confirm-email',
    RESEND_CONFIRMATION: '/v1/auth/resend-confirmation',

    // Password reset
    FORGOT_PASSWORD: '/v1/auth/forgot-password',
    RESET_PASSWORD: '/v1/auth/reset-password',

    // Roles (admin)
    CREATE_ROLE: '/v1/auth/roles',
    ADD_ROLE: '/v1/auth/add-role',
    REMOVE_ROLE: '/v1/auth/remove-role',

    // CSRF
    CSRF_REFRESH: '/v1/csrf/refresh',
} as const;

// ==================== AUTH SERVICE ====================

/**
 * Centralized Authentication Service
 * 
 * SECURITY ARCHITECTURE:
 * - Access tokens: Short-lived JWT in localStorage (15 min)
 * - Refresh tokens: Long-lived token in HTTP-only cookie (14 days)
 * - CSRF tokens: Session storage for mutation requests
 * 
 * All methods handle errors consistently and return typed responses.
 */
class AuthService {
    // ==================== AUTHENTICATION ====================

    /**
     * Authenticate user with email and password
     * Sets refresh token in HTTP-only cookie automatically
     */
    async login(credentials: LoginRequest): Promise<LoginResponse> {
        try {
            const response = await api.post<LoginResponse>(
                ENDPOINTS.LOGIN,
                credentials,
                {
                    skipAuth: true, // No auth needed for login
                    includeHeaders: false
                }
            );

            if (!response.success || !response.data) {
                throw new Error(response.message || 'Login failed');
            }

            return response.data;
        } catch (error: any) {
            console.error('AuthService: Login failed', error);
            throw this.handleError(error, 'Failed to login');
        }
    }

    /**
     * Register new user account
     * Sends verification email automatically
     */
    async register(registrationData: RegisterRequest): Promise<void> {
        try {
            const response = await api.post(
                ENDPOINTS.REGISTER,
                registrationData,
                {
                    skipAuth: true, // No auth needed for registration
                    includeHeaders: false
                }
            );

            if (!response.success) {
                throw new Error(response.message || 'Registration failed');
            }

            // Registration successful - verification email sent
        } catch (error: any) {
            console.error('AuthService: Registration failed', error);
            throw this.handleError(error, 'Failed to register');
        }
    }

    /**
     * Logout user and clear refresh token cookie
     */
    async logout(): Promise<void> {
        try {
            await api.post(
                ENDPOINTS.LOGOUT,
                undefined,
                {
                    //timeout: 5000,
                    retryCount: 0 // Don't retry logout
                }
            );
        } catch (error: any) {
            console.warn('AuthService: Logout request failed', error);
            // Don't throw - local logout should still work
        }
    }

    /**
     * Refresh access token using refresh token from HTTP-only cookie
     * Backend automatically reads refresh token from cookie
     */
    async refreshToken(): Promise<LoginResponse> {
        try {
            const response = await api.post<LoginResponse>(
                ENDPOINTS.REFRESH_TOKEN,
                undefined,
                {
                    skipAuth: false, // Send current access token
                    retryCount: 0,   // Don't retry refresh
                    timeout: 10000,
                    priority: 'high'
                }
            );

            if (!response.success || !response.data) {
                throw new Error(response.message || 'Token refresh failed');
            }

            return response.data;
        } catch (error: any) {
            console.error('AuthService: Token refresh failed', error);
            throw this.handleError(error, 'Failed to refresh token');
        }
    }

    // ==================== USER ====================

    /**
     * Get current authenticated user data
     */
    async getCurrentUser(): Promise<UserDataResponse> {
        try {
            const response = await api.get<UserDataResponse>(
                ENDPOINTS.CURRENT_USER,
                {
                    includeHeaders: false
                }
            );

            if (!response.success || !response.data) {
                throw new Error(response.message || 'Failed to get user data');
            }

            return response.data;
        } catch (error: any) {
            console.error('AuthService: Get current user failed', error);
            throw this.handleError(error, 'Failed to get user data');
        }
    }

    // ==================== EMAIL VERIFICATION ====================

    /**
     * Confirm user email with verification token
     */
    async confirmEmail(request: ConfirmEmailRequest): Promise<void> {
        try {
            const response = await api.post(
                ENDPOINTS.CONFIRM_EMAIL,
                request,
                {
                    skipAuth: true, // Email confirmation before login
                    includeHeaders: false
                }
            );

            if (!response.success) {
                throw new Error(response.message || 'Email confirmation failed');
            }
        } catch (error: any) {
            console.error('AuthService: Email confirmation failed', error);
            throw this.handleError(error, 'Failed to confirm email');
        }
    }

    /**
     * Resend verification email to user
     */
    async resendConfirmation(request: ResendConfirmationRequest): Promise<void> {
        try {
            const response = await api.post(
                ENDPOINTS.RESEND_CONFIRMATION,
                request,
                {
                    skipAuth: true,
                    includeHeaders: false
                }
            );

            if (!response.success) {
                throw new Error(response.message || 'Failed to resend confirmation');
            }
        } catch (error: any) {
            console.error('AuthService: Resend confirmation failed', error);
            throw this.handleError(error, 'Failed to resend confirmation email');
        }
    }

    // ==================== PASSWORD RESET ====================

    /**
     * Request password reset email
     */
    async forgotPassword(request: ForgotPasswordRequest): Promise<void> {
        try {
            const response = await api.post(
                ENDPOINTS.FORGOT_PASSWORD,
                request,
                {
                    skipAuth: true,
                    includeHeaders: false
                }
            );

            if (!response.success) {
                throw new Error(response.message || 'Failed to request password reset');
            }
        } catch (error: any) {
            console.error('AuthService: Forgot password failed', error);
            throw this.handleError(error, 'Failed to request password reset');
        }
    }

    /**
     * Reset password with token from email
     */
    async resetPassword(request: ResetPasswordRequest): Promise<void> {
        try {
            const response = await api.post(
                ENDPOINTS.RESET_PASSWORD,
                request,
                {
                    skipAuth: true,
                    includeHeaders: false
                }
            );

            if (!response.success) {
                throw new Error(response.message || 'Password reset failed');
            }
        } catch (error: any) {
            console.error('AuthService: Reset password failed', error);
            throw this.handleError(error, 'Failed to reset password');
        }
    }

    // ==================== ROLES (ADMIN) ====================

    /**
     * Create new role (admin only)
     */
    async createRole(request: CreateRoleRequest): Promise<void> {
        try {
            const response = await api.post(
                ENDPOINTS.CREATE_ROLE,
                request,
                {
                    includeHeaders: false
                }
            );

            if (!response.success) {
                throw new Error(response.message || 'Failed to create role');
            }
        } catch (error: any) {
            console.error('AuthService: Create role failed', error);
            throw this.handleError(error, 'Failed to create role');
        }
    }

    /**
     * Add role to user (admin only)
     */
    async addRoleToUser(request: AddRoleRequest): Promise<void> {
        try {
            const response = await api.post(
                ENDPOINTS.ADD_ROLE,
                request,
                {
                    includeHeaders: false
                }
            );

            if (!response.success) {
                throw new Error(response.message || 'Failed to add role');
            }
        } catch (error: any) {
            console.error('AuthService: Add role failed', error);
            throw this.handleError(error, 'Failed to add role to user');
        }
    }

    /**
     * Remove role from user (admin only)
     */
    async removeRoleFromUser(request: AddRoleRequest): Promise<void> {
        try {
            const response = await api.post(
                ENDPOINTS.REMOVE_ROLE,
                request,
                {
                    includeHeaders: false
                }
            );

            if (!response.success) {
                throw new Error(response.message || 'Failed to remove role');
            }
        } catch (error: any) {
            console.error('AuthService: Remove role failed', error);
            throw this.handleError(error, 'Failed to remove role from user');
        }
    }

    // ==================== CSRF ====================

    /**
     * Get CSRF token for mutation requests
     */
    async getCsrfToken(): Promise<string> {
        try {
            const response = await api.get<CsrfResponse>(
                ENDPOINTS.CSRF_REFRESH,
                {
                    skipAuth: true,
                    skipCsrf: true,
                    includeHeaders: false
                }
            );

            if (!response.success || !response.data?.token) {
                throw new Error('Failed to get CSRF token');
            }

            return response.data.token;
        } catch (error: any) {
            console.error('AuthService: Get CSRF token failed', error);
            throw this.handleError(error, 'Failed to get CSRF token');
        }
    }

    // ==================== ERROR HANDLING ====================

    /**
     * Centralized error handling with consistent error messages
     */
    private handleError(error: any, defaultMessage: string): Error {
        // Extract meaningful error message
        if (error.response?.data?.message) {
            return new Error(error.response.data.message);
        }

        if (error.response?.data?.validationErrors) {
            const validationErrors = error.response.data.validationErrors;
            const firstError = Object.values(validationErrors)[0];
            if (Array.isArray(firstError) && firstError.length > 0) {
                return new Error(firstError[0]);
            }
        }

        if (error.message) {
            return new Error(error.message);
        }

        return new Error(defaultMessage);
    }
}

// ==================== EXPORT SINGLETON ====================

export const authService = new AuthService();
export default authService;