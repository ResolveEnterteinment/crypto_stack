// src/context/AuthContext.tsx
import React, { createContext, useContext, useEffect, useState, useCallback } from "react";
import api from "../services/api";
import { jwtDecode } from "jwt-decode"; // Consider adding this library for JWT parsing

interface User {
    id: string;
    username: string;
    email: string;
    roles?: string[];
}

interface AuthContextType {
    user: User | null;
    isAuthenticated: boolean;
    isLoading: boolean;
    error: string | null;
    login: (tokenData: any) => Promise<void>;
    logout: () => Promise<void>;
    refreshToken: () => Promise<boolean>;
    hasRole: (role: string) => boolean;
}

interface JwtPayload {
    exp: number;
    nbf: number;
    role?: string | string[];
    [key: string]: any;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

// Configuration constants
const AUTH_CONFIG = {
    STORAGE_KEYS: {
        ACCESS_TOKEN: "access_token",
        REFRESH_TOKEN: "refresh_token",
        USER: "user"
    },
    TOKEN_REFRESH_THRESHOLD_MS: 5 * 60 * 1000, // 5 minutes before expiry
    TOKEN_CHECK_INTERVAL_MS: 60 * 1000, // Check token every minute
    SESSION_STORAGE_PREFIX: "crypt_inv_",  // Prefix for session storage items
    CSRF_REFRESH_INTERVAL_MS: 30 * 60 * 1000 // Refresh CSRF token every 30 minutes
};

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
    const [user, setUser] = useState<User | null>(() => {
        // Load user from localStorage with added security
        try {
            const storedUser = localStorage.getItem(AUTH_CONFIG.STORAGE_KEYS.USER);
            return storedUser ? JSON.parse(storedUser) : null;
        } catch (error) {
            console.error("Error parsing stored user data:", error);
            // Clear potentially corrupted data
            localStorage.removeItem(AUTH_CONFIG.STORAGE_KEYS.USER);
            localStorage.removeItem(AUTH_CONFIG.STORAGE_KEYS.ACCESS_TOKEN);
            localStorage.removeItem(AUTH_CONFIG.STORAGE_KEYS.REFRESH_TOKEN);
            return null;
        }
    });

    const [isLoading, setIsLoading] = useState<boolean>(false);
    const [error, setError] = useState<string | null>(null);
    const [csrfToken, setCsrfToken] = useState<string | null>(null);

    // Helper function to securely store sensitive data
    const secureStore = (key: string, value: string): void => {
        try {
            localStorage.setItem(key, value);
        } catch (error) {
            console.error(`Error storing ${key}:`, error);
            // Handle storage error - could use in-memory fallback if needed
        }
    };

    // Helper function to parse JWT token
    const parseJwt = (token: string): JwtPayload | null => {
        try {
            return jwtDecode<JwtPayload>(token);
        } catch (error) {
            console.error("Error parsing JWT token:", error);
            return null;
        }
    };

    // Check if token is expired or about to expire
    const isTokenExpired = (token: string | null): boolean => {
        if (!token) return true;

        try {
            const decoded = parseJwt(token);
            if (!decoded) return true;

            // Check if token is expired or will expire soon
            const currentTime = Date.now() / 1000;
            return decoded.exp <= currentTime + (AUTH_CONFIG.TOKEN_REFRESH_THRESHOLD_MS / 1000);
        } catch (error) {
            console.error("Error checking token expiration:", error);
            return true;
        }
    };

    // Fetch a new CSRF token
    const fetchCsrfToken = useCallback(async (): Promise<string | null> => {
        try {
            const response = await api.get("/v1/csrf");
            const newToken = response.data.token;
            setCsrfToken(newToken);
            return newToken;
        } catch (error) {
            console.error("Failed to fetch CSRF token:", error);
            return null;
        }
    }, []);

    // Extract roles from the JWT token
    const extractRolesFromToken = (token: string): string[] => {
        try {
            const decoded = parseJwt(token);
            if (!decoded) return [];

            // Handle both string and array formats for roles
            if (typeof decoded.role === 'string') {
                return [decoded.role];
            } else if (Array.isArray(decoded.role)) {
                return decoded.role;
            } else {
                return [];
            }
        } catch (error) {
            console.error("Error extracting roles from token:", error);
            return [];
        }
    };

    // Check if the user has a specific role
    const hasRole = useCallback((role: string): boolean => {
        if (!user || !user.roles) return false;
        return user.roles.includes(role);
    }, [user]);

    // Login function with enhanced security
    const login = async (sessionData: any): Promise<void> => {
        setIsLoading(true);
        setError(null);

        try {
            if (!sessionData || !sessionData.accessToken) {
                throw new Error("Invalid session data received");
            }

            // Validate token structure
            const tokenPayload = parseJwt(sessionData.accessToken);
            if (!tokenPayload) {
                throw new Error("Invalid token format");
            }

            // Extract roles from token
            const roles = extractRolesFromToken(sessionData.accessToken);

            // Save tokens
            secureStore(AUTH_CONFIG.STORAGE_KEYS.ACCESS_TOKEN, sessionData.accessToken);
            if (sessionData.refreshToken) {
                secureStore(AUTH_CONFIG.STORAGE_KEYS.REFRESH_TOKEN, sessionData.refreshToken);
            }

            // Create user object with roles
            const userData = {
                id: sessionData.userId,
                username: sessionData.username,
                email: sessionData.email,
                roles: roles
            };

            // Store user data
            secureStore(AUTH_CONFIG.STORAGE_KEYS.USER, JSON.stringify(userData));
            setUser(userData);

            // Fetch CSRF token for subsequent requests
            await fetchCsrfToken();
        } catch (error: any) {
            console.error("Login error:", error);
            setError(error.message || "Authentication failed");
            // Clear any partial data
            logout();
        } finally {
            setIsLoading(false);
        }
    };

    // Logout function
    const logout = useCallback(async (): Promise<void> => {
        setIsLoading(true);

        try {
            // Try to call the backend logout endpoint to invalidate the refresh token
            if (user) {
                const token = csrfToken || await fetchCsrfToken();
                await api.post("/v1/auth/logout", null, {
                    headers: {
                        'X-CSRF-TOKEN': token || ''
                    }
                }).catch(err => {
                    // Log but don't prevent logout if server call fails
                    console.warn("Backend logout failed:", err);
                });
            }
        } catch (error) {
            console.error("Error during logout:", error);
        } finally {
            // Clear local storage
            localStorage.removeItem(AUTH_CONFIG.STORAGE_KEYS.USER);
            localStorage.removeItem(AUTH_CONFIG.STORAGE_KEYS.ACCESS_TOKEN);
            localStorage.removeItem(AUTH_CONFIG.STORAGE_KEYS.REFRESH_TOKEN);

            // Reset state
            setUser(null);
            setCsrfToken(null);
            setError(null);
            setIsLoading(false);

            // Update authorization header in API service
            api.instance.defaults.headers.common['Authorization'] = '';
        }
    }, [user, csrfToken, fetchCsrfToken]);

    // Refresh token function
    const refreshToken = async (): Promise<boolean> => {
        const currentToken = localStorage.getItem(AUTH_CONFIG.STORAGE_KEYS.ACCESS_TOKEN);

        // If no token or token not expired yet, no need to refresh
        if (!currentToken || !isTokenExpired(currentToken)) {
            return true;
        }

        try {
            const token = csrfToken || await fetchCsrfToken();

            // Call refresh token endpoint
            const { data } = await api.post("/v1/auth/refresh-token", null, {
                headers: {
                    'Authorization': `Bearer ${currentToken}`,
                    'X-CSRF-TOKEN': token || ''
                }
            });

            if (data.success && data.accessToken) {
                // Save new tokens
                secureStore(AUTH_CONFIG.STORAGE_KEYS.ACCESS_TOKEN, data.accessToken);

                // Update user roles from new token
                const roles = extractRolesFromToken(data.accessToken);

                // Update user object with new roles if needed
                if (user) {
                    const updatedUser = {
                        ...user,
                        roles: roles
                    };

                    secureStore(AUTH_CONFIG.STORAGE_KEYS.USER, JSON.stringify(updatedUser));
                    setUser(updatedUser);
                }

                return true;
            } else {
                throw new Error("Token refresh failed");
            }
        } catch (error) {
            console.error("Token refresh error:", error);

            // Session expired, logout the user
            await logout();

            return false;
        }
    };

    // Setup token refresh at regular intervals
    useEffect(() => {
        if (!user) return;

        // Check token expiration at regular intervals
        const tokenCheckInterval = setInterval(async () => {
            const accessToken = localStorage.getItem(AUTH_CONFIG.STORAGE_KEYS.ACCESS_TOKEN);

            if (accessToken && isTokenExpired(accessToken)) {
                console.log("Token expired or about to expire, refreshing...");
                await refreshToken();
            }
        }, AUTH_CONFIG.TOKEN_CHECK_INTERVAL_MS);

        // Fetch CSRF token at regular intervals
        const csrfRefreshInterval = setInterval(async () => {
            await fetchCsrfToken();
        }, AUTH_CONFIG.CSRF_REFRESH_INTERVAL_MS);

        // Call once on mount to check token
        refreshToken();

        return () => {
            clearInterval(tokenCheckInterval);
            clearInterval(csrfRefreshInterval);
        };
    }, [refreshToken, fetchCsrfToken, user]);

    // Update API authorization header when token changes
    useEffect(() => {
        const accessToken = localStorage.getItem(AUTH_CONFIG.STORAGE_KEYS.ACCESS_TOKEN);

        if (accessToken) {
            api.instance.defaults.headers.common['Authorization'] = `Bearer ${accessToken}`;
        } else {
            delete api.instance.defaults.headers.common['Authorization'];
        }
    }, [user]);

    // Context value
    const contextValue: AuthContextType = {
        user,
        isAuthenticated: !!user,
        isLoading,
        error,
        login,
        logout,
        refreshToken,
        hasRole
    };

    return (
        <AuthContext.Provider value={contextValue}>
            {children}
        </AuthContext.Provider>
    );
};

// Custom hook to use the auth context
export const useAuth = (): AuthContextType => {
    const context = useContext(AuthContext);

    if (context === undefined) {
        throw new Error("useAuth must be used within an AuthProvider");
    }

    return context;
};