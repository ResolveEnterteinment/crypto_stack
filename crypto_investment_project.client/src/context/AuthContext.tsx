// src/context/AuthContext.tsx
import { jwtDecode } from "jwt-decode";
import React, { createContext, useCallback, useContext, useEffect, useState } from "react";
import { apiClient, ApiErrorHandler } from "../services/api";

interface User {
    id: string;
    username: string;
    email: string;
    fullName?: string;
    uniqueName?: string;
    roles?: string[];
}

interface AuthContextType {
    user: User | null;
    token: string | null;
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
    sub: string;
    nameid: string;
    unique_name: string;
    email: string;
    fullName: string;
    role?: string | string[];
    [key: string]: any;
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

const AuthContext = createContext<AuthContextType | undefined>(undefined);

// Configuration constants
const AUTH_CONFIG = {
    STORAGE_KEYS: {
        ACCESS_TOKEN: "access_token",
        REFRESH_TOKEN: "refresh_token",
        TOKEN_EXPIRY: "token_expiry",
        USER: "user"
    },
    TOKEN_REFRESH_THRESHOLD_MS: 5 * 60 * 1000, // 5 minutes before expiry
    TOKEN_CHECK_INTERVAL_MS: 60 * 1000, // Check token every minute
    SESSION_STORAGE_PREFIX: "crypt_inv_"  // Prefix for session storage items
};

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
    const [user, setUser] = useState<User | null>(() => {
        // Load user from localStorage with added security
        try {
            const storedUser = localStorage.getItem(AUTH_CONFIG.STORAGE_KEYS.USER);
            return storedUser ? JSON.parse(storedUser) : null;
        } catch (error) {
            console.error("Failed to parse stored user:", error);
            return null;
        }
    });

    const [token, setToken] = useState<string | null>(() => {
        return localStorage.getItem(AUTH_CONFIG.STORAGE_KEYS.ACCESS_TOKEN);
    });

    const [isLoading, setIsLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [refreshInProgress, setRefreshInProgress] = useState(false);

    // Secure storage helper
    const secureStore = (key: string, value: string) => {
        localStorage.setItem(key, value);
        // Also store in session storage for the current session
        sessionStorage.setItem(`${AUTH_CONFIG.SESSION_STORAGE_PREFIX}${key}`, value);
    };

    // Enhanced function to extract comprehensive user information from JWT token
    const extractUserInfoFromToken = (tokenString: string): User | null => {
        try {
            const decoded = jwtDecode<JwtPayload>(tokenString);

            console.log('🔍 Extracting user info from JWT token:', {
                sub: decoded.sub ? `${decoded.sub.substring(0, 3)}***${decoded.sub.substring(decoded.sub.length - 3)}` : undefined,
                unique_name: decoded.unique_name ? `${decoded.unique_name.split('@')[0].substring(0, 2)}***@${decoded.unique_name.split('@')[1]}` : undefined,
                fullName: decoded.fullName || 'N/A',
                email: decoded.email ? `${decoded.email.split('@')[0].substring(0, 2)}***@${decoded.email.split('@')[1]}` : undefined
            });

            // Extract user ID from standard JWT claims
            const userId = decoded.sub || decoded.nameid;
            if (!userId) {
                console.error('❌ No user ID found in JWT token');
                return null;
            }

            // Extract username from various possible claims
            const username = decoded.unique_name || decoded.email || decoded.preferred_username || decoded.name;

            // Extract email (fallback to username if email claim not present)
            const email = decoded.email || username;

            // Extract full name from custom claims
            const fullName = decoded.fullName || decoded.name || decoded.given_name;

            // Extract unique name (usually email in your system)
            const uniqueName = decoded.unique_name || email;

            // Extract roles with proper null checking
            let roles: string[] = [];
            if (decoded.role) {
                if (Array.isArray(decoded.role)) {
                    roles = decoded.role;
                } else {
                    roles = [decoded.role];
                }
            }

            const userInfo: User = {
                id: userId,
                username: username || email || 'Unknown',
                email: email || username || 'Unknown',
                fullName,
                uniqueName,
                roles
            };

            console.log('✅ Successfully extracted user info:', {
                id: userInfo.id ? `${userInfo.id.substring(0, 3)}***${userInfo.id.substring(userInfo.id.length - 3)}` : 'N/A',
                username: userInfo.username ? `${userInfo.username.split('@')[0].substring(0, 2)}***@${userInfo.username.split('@')[1]}` : 'N/A',
                fullName: userInfo.fullName || 'N/A',
                email: userInfo.email ? `${userInfo.email.split('@')[0].substring(0, 2)}***@${userInfo.email.split('@')[1]}` : 'N/A',
                uniqueName: userInfo.uniqueName ? `${userInfo.uniqueName.split('@')[0].substring(0, 2)}***@${userInfo.uniqueName.split('@')[1]}` : 'N/A',
                roles: userInfo.roles
            });

            return userInfo;
        } catch (error) {
            console.error('❌ Failed to extract user info from JWT token:', error);
            return null;
        }
    };

    // Store token expiry for validation
    const storeTokenExpiry = (tokenString: string) => {
        try {
            const decoded = jwtDecode<JwtPayload>(tokenString);
            if (decoded.exp) {
                const expiryTime = decoded.exp * 1000; // Convert to milliseconds
                localStorage.setItem(AUTH_CONFIG.STORAGE_KEYS.TOKEN_EXPIRY, expiryTime.toString());
            }
        } catch (error) {
            console.error("Failed to store token expiry:", error);
        }
    };

    // Check if token is expired or about to expire
    const isTokenExpired = (tokenString: string): boolean => {
        try {
            const decoded = jwtDecode<JwtPayload>(tokenString);
            const currentTime = Date.now();
            const expiryTime = decoded.exp * 1000;

            // Check if token expires within the threshold
            return currentTime >= (expiryTime - AUTH_CONFIG.TOKEN_REFRESH_THRESHOLD_MS);
        } catch {
            return true;
        }
    };

    // Check if user has a specific role
    const hasRole = (role: string): boolean => {
        if (!user?.roles) return false;
        return user.roles.includes(role);
    };

    // Enhanced login function that uses JWT token extraction
    const login = async (tokenData: LoginResponse): Promise<void> => {
        setIsLoading(true);
        setError(null);

        try {
            // Validate token data
            if (!tokenData.accessToken) {
                throw new Error("Invalid authentication response");
            }

            console.log('🔐 Processing login with token data...');

            // Extract comprehensive user information from JWT token
            const userInfoFromToken = extractUserInfoFromToken(tokenData.accessToken);

            if (!userInfoFromToken) {
                throw new Error("Failed to extract user information from token");
            }

            // Create enhanced user object combining token data and response data
            // Use roles from JWT token if available and non-empty, otherwise fallback to response data
            const tokenRoles = userInfoFromToken.roles || [];
            const responseRoles = tokenData.roles || [];
            const finalRoles = tokenRoles.length > 0 ? tokenRoles : responseRoles;

            const userInfo: User = {
                id: userInfoFromToken.id,
                username: userInfoFromToken.username,
                email: userInfoFromToken.email,
                fullName: userInfoFromToken.fullName,
                uniqueName: userInfoFromToken.uniqueName,
                roles: finalRoles
            };

            console.log('👤 Created user object from JWT token');

            // Store authentication data
            secureStore(AUTH_CONFIG.STORAGE_KEYS.ACCESS_TOKEN, tokenData.accessToken);
            if (tokenData.refreshToken) {
                secureStore(AUTH_CONFIG.STORAGE_KEYS.REFRESH_TOKEN, tokenData.refreshToken);
            }
            secureStore(AUTH_CONFIG.STORAGE_KEYS.USER, JSON.stringify(userInfo));
            storeTokenExpiry(tokenData.accessToken);

            // Update state
            setUser(userInfo);
            setToken(tokenData.accessToken);

            // Update API client with new token
            apiClient.setAuthToken(tokenData.accessToken);

            console.log('✅ Login completed successfully');

        } catch (error: any) {
            console.error("Login error:", error);

            // Use ApiErrorHandler for consistent error messages
            const apiError = ApiErrorHandler.extractError(error);
            const userMessage = ApiErrorHandler.formatUserMessage(apiError);
            setError(userMessage);

            console.log("AuthContext::login => userMessage:", userMessage);
            // Clear any partial data
            await logout();

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
                await apiClient.post("/v1/auth/logout", undefined, {
                    skipCsrf: false // Ensure CSRF token is included
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
            localStorage.removeItem(AUTH_CONFIG.STORAGE_KEYS.TOKEN_EXPIRY);

            // Clear session storage
            Object.keys(sessionStorage).forEach(key => {
                if (key.startsWith(AUTH_CONFIG.SESSION_STORAGE_PREFIX)) {
                    sessionStorage.removeItem(key);
                }
            });

            // Reset state
            setUser(null);
            setToken(null);
            setError(null);
            setIsLoading(false);

            // Clear tokens in API client
            apiClient.clearTokens();
        }
    }, [user]);

    // Enhanced refresh token function that updates user info from new token
    const refreshToken = async (): Promise<boolean> => {
        // Return true if refresh is already in progress
        if (refreshInProgress) {
            // Wait for the current refresh to finish
            await new Promise(resolve => setTimeout(resolve, 1000));
            return !isTokenExpired(localStorage.getItem(AUTH_CONFIG.STORAGE_KEYS.ACCESS_TOKEN) || '');
        }

        // Check if user is authenticated
        if (!user) {
            return false;
        }

        const currentToken = localStorage.getItem(AUTH_CONFIG.STORAGE_KEYS.ACCESS_TOKEN);

        // If no token, no need to refresh
        if (!currentToken) {
            return false;
        }

        // If token is not expired yet, no need to refresh
        if (!isTokenExpired(currentToken)) {
            return true;
        }

        setRefreshInProgress(true);

        try {
            console.log('🔄 Refreshing token...');

            // Call refresh token endpoint - the new API handles CSRF internally
            const response = await apiClient.post<LoginResponse>("/v1/auth/refresh-token", undefined, {
                skipAuth: false, // Use current token for refresh
                retryCount: 1, // Limited retries for refresh
                priority: 'high' // High priority for auth operations
            });

            if (response.success && response.data.accessToken) {
                console.log('✅ Token refresh successful');

                // Extract updated user information from new token
                const updatedUserInfo = extractUserInfoFromToken(response.data.accessToken);

                if (updatedUserInfo) {
                    // Handle roles with proper null checking
                    const updatedRoles = updatedUserInfo.roles || [];
                    const currentRoles = user.roles || [];
                    const finalRoles = updatedRoles.length > 0 ? updatedRoles : currentRoles;

                    // Preserve existing user data and merge with updated token data
                    const mergedUserInfo: User = {
                        ...user,
                        ...updatedUserInfo,
                        // Ensure roles are updated from the new token
                        roles: finalRoles
                    };

                    // Save new tokens and updated user info
                    secureStore(AUTH_CONFIG.STORAGE_KEYS.ACCESS_TOKEN, response.data.accessToken);
                    secureStore(AUTH_CONFIG.STORAGE_KEYS.USER, JSON.stringify(mergedUserInfo));
                    setToken(response.data.accessToken);
                    setUser(mergedUserInfo);
                    storeTokenExpiry(response.data.accessToken);

                    // Update API client with new token
                    apiClient.setAuthToken(response.data.accessToken);

                    console.log('👤 User info updated from refreshed token');
                } else {
                    console.warn('⚠️ Failed to extract user info from refreshed token, keeping existing user data');

                    // Still save the new token even if user extraction failed
                    secureStore(AUTH_CONFIG.STORAGE_KEYS.ACCESS_TOKEN, response.data.accessToken);
                    setToken(response.data.accessToken);
                    storeTokenExpiry(response.data.accessToken);
                    apiClient.setAuthToken(response.data.accessToken);
                }

                return true;
            } else {
                throw new Error("Token refresh failed");
            }
        } catch (error) {
            console.error("Token refresh error:", error);

            // Use ApiErrorHandler for consistent error messages
            const apiError = ApiErrorHandler.extractError(error);

            // If it's an auth error, logout the user
            if (apiError.isAuthError) {
                await logout();
            }

            return false;
        } finally {
            setRefreshInProgress(false);
        }
    };

    // Initialize authentication on mount
    useEffect(() => {
        const initAuth = async () => {
            const storedToken = localStorage.getItem(AUTH_CONFIG.STORAGE_KEYS.ACCESS_TOKEN);

            if (storedToken) {
                console.log('🔍 Found stored token, validating...');

                // Set token in API client
                apiClient.setAuthToken(storedToken);

                // Validate stored user data against token
                const storedUser = localStorage.getItem(AUTH_CONFIG.STORAGE_KEYS.USER);
                if (storedUser) {
                    try {
                        const parsedUser = JSON.parse(storedUser);
                        // Verify that the stored user data matches the token
                        const tokenUserInfo = extractUserInfoFromToken(storedToken);

                        if (tokenUserInfo && tokenUserInfo.id === parsedUser.id) {
                            console.log('✅ Stored user data validated against token');
                            setUser(parsedUser);
                        } else {
                            console.log('⚠️ Stored user data does not match token, extracting fresh data');
                            if (tokenUserInfo) {
                                setUser(tokenUserInfo);
                                secureStore(AUTH_CONFIG.STORAGE_KEYS.USER, JSON.stringify(tokenUserInfo));
                            }
                        }
                    } catch (error) {
                        console.error('❌ Failed to parse stored user, extracting from token');
                        const tokenUserInfo = extractUserInfoFromToken(storedToken);
                        if (tokenUserInfo) {
                            setUser(tokenUserInfo);
                            secureStore(AUTH_CONFIG.STORAGE_KEYS.USER, JSON.stringify(tokenUserInfo));
                        }
                    }
                }

                // Check if token needs refresh
                if (isTokenExpired(storedToken)) {
                    await refreshToken();
                }
            }
        };

        initAuth();
    }, []); // Run once on mount

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

        // Call once on mount to check token
        refreshToken();

        return () => {
            clearInterval(tokenCheckInterval);
        };
    }, [refreshToken, user]);

    // Update API authorization header when token changes
    useEffect(() => {
        const accessToken = localStorage.getItem(AUTH_CONFIG.STORAGE_KEYS.ACCESS_TOKEN);

        if (accessToken) {
            apiClient.setAuthToken(accessToken);
        } else {
            apiClient.clearTokens();
        }
    }, [user, token]);

    // Context value
    const contextValue: AuthContextType = {
        user,
        token,
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

// Export additional utility functions for advanced use cases
export const extractUserInfoFromJWT = (token: string): User | null => {
    try {
        const payload = token.split('.')[1];
        const decodedPayload = JSON.parse(atob(payload));

        // Extract roles with proper null checking
        let roles: string[] = [];
        if (decodedPayload.role) {
            if (Array.isArray(decodedPayload.role)) {
                roles = decodedPayload.role;
            } else {
                roles = [decodedPayload.role];
            }
        }

        return {
            id: decodedPayload.sub || decodedPayload.nameid,
            username: decodedPayload.unique_name || decodedPayload.email,
            email: decodedPayload.email || decodedPayload.unique_name,
            fullName: decodedPayload.fullName,
            uniqueName: decodedPayload.unique_name,
            roles: roles
        };
    } catch (error) {
        console.error('Failed to extract user info from JWT:', error);
        return null;
    }
};