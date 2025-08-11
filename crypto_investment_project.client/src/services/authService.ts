import api from './api';

class AuthService {
    // Initialize authentication and encryption
    async initialize() {
        try {
            // Set up API with encryption
            await api.initialize();

            // Check if user is already authenticated BEFORE enabling encryption
            const token = localStorage.getItem('access_token');
            if (token) {
                console.log("Found existing auth token, setting up API...");
                api.setAuthToken(token);
            }

            // Check authentication status
            return this.checkAuthStatus();
        } catch (error: unknown) {
            console.error('Failed to initialize auth service:', error);
            return false;
        }
    }

    // Login with email and password
    async login(email: string, password: string) {
        try {
            const response = await api.post('/v1/auth/login', { email, password });
            console.log("Login response:", response.status, response.data);

            // Store the token
            if (response.data.accessToken) {
                // Store token safely
                this.storeAuthToken(response.data.accessToken);

                return response.data;
            } else {
                console.error("No access token in login response");
                return response.data;
            }
        } catch (error: unknown) {
            console.error("Login error:", error);
            throw error;
        }
    }

    // Store auth token in a consistent way
    storeAuthToken(token: string): void {
        console.log("Storing auth token securely");
        localStorage.setItem('access_token', token);
        sessionStorage.setItem('access_token', token); // Backup in session storage
        api.setAuthToken(token);
    }

    // Logout user
    async logout() {
        try {
            // Call logout endpoint if available
            await api.post('/v1/auth/logout');
        } catch (error: unknown) {
            console.error('Logout error:', error);
        } finally {
            // Always clear storage and auth token
            localStorage.removeItem('access_token');
            sessionStorage.removeItem('access_token');
            api.setAuthToken(null);
        }
    }

    // Check if user is authenticated
    async checkAuthStatus() {
        // Try to get token from multiple sources
        let token = localStorage.getItem('access_token') ||
            sessionStorage.getItem('access_token');

        console.log("Auth status check - token:", token ? `${token.substring(0, 15)}...` : "No token found");

        if (!token) {
            return null;
        }

        try {
            // Set token for API calls
            api.setAuthToken(token);

            // Get current user data
            try {
                const response = await api.get('/v1/auth/user');
                console.log("Auth check successful:", response.status);
                return response.data;
            } catch (error: unknown) {
                // Specific handling for 401 errors
                if (error && typeof error === 'object' && 'response' in error) {
                    const axiosError = error as { response?: { status?: number } };
                    if (axiosError.response && axiosError.response.status === 401) {
                        console.log("Token expired or invalid, attempting refresh...");
                        const refreshed = await this.refreshToken();
                        if (refreshed) {
                            // Try again with new token
                            const retryResponse = await api.get('/v1/auth/user');
                            return retryResponse.data;
                        }
                    }
                }
                throw error; // Re-throw if not handled
            }
        } catch (error: unknown) {
            console.error("Auth check failed:", error);
            // If token is invalid, clear it
            localStorage.removeItem('access_token');
            sessionStorage.removeItem('access_token');
            api.setAuthToken(null);
            return null;
        }
    }

    // Refresh the authentication token
    async refreshToken() {
        try {
            const response = await api.post('/v1/auth/refresh-token');

            if (response.data && response.data.accessToken) {
                // Store new token
                this.storeAuthToken(response.data.accessToken);

                return true;
            }
            return false;
        } catch (error: unknown) {
            console.error("Token refresh failed:", error);
            localStorage.removeItem('access_token');
            sessionStorage.removeItem('access_token');
            api.setAuthToken(null);
            return false;
        }
    }

    // Register a new user
    async register(userData: any) {
        const response = await api.post('/v1/auth/register', userData);
        return response.data;
    }

    // Request password reset
    async forgotPassword(email: string) {
        const response = await api.post('/v1/auth/forgot-password', { email });
        return response.data;
    }

    // Reset password with token
    async resetPassword(email: string, token: string, password: string, confirmPassword: string) {
        const response = await api.post('/v1/auth/reset-password', {
            email,
            token,
            newPassword: password,
            confirmPassword
        });
        return response.data;
    }
}

export default new AuthService();