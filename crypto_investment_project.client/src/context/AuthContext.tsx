// Enhanced AuthContext.tsx - Fixes infinite loops and improves error handling
import { jwtDecode } from "jwt-decode";
import React, { createContext, useCallback, useContext, useEffect, useState, useRef } from "react";
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
    isInitialized: boolean; // NEW: Track initialization state
    error: string | null;
    login: (tokenData: any) => Promise<void>;
    logout: () => Promise<void>;
    refreshToken: () => Promise<boolean>;
    hasRole: (role: string) => boolean;
    clearError: () => void; // NEW: Allow manual error clearing
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
    TOKEN_REFRESH_THRESHOLD_MS: 5 * 60 * 1000,
    TOKEN_CHECK_INTERVAL_MS: 60 * 1000,
    SESSION_STORAGE_PREFIX: "crypt_inv_",
    MAX_REFRESH_ATTEMPTS: 3,
    REFRESH_BACKOFF_MS: 2000
};

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
    const [user, setUser] = useState<User | null>(null);
    const [token, setToken] = useState<string | null>(null);
    const [isLoading, setIsLoading] = useState(false);
    const [isInitialized, setIsInitialized] = useState(false);
    const [error, setError] = useState<string | null>(null);

    // Use refs to prevent excessive refresh attempts
    const refreshInProgress = useRef(false);
    const refreshAttempts = useRef(0);
    const initializationStarted = useRef(false);

    const clearError = useCallback(() => {
        setError(null);
    }, []);

    const extractUserInfoFromToken = useCallback((tokenString: string): User | null => {
        try {
            const decoded = jwtDecode<JwtPayload>(tokenString);

            const userId = decoded.sub || decoded.nameid;
            if (!userId) {
                console.error('No user ID found in JWT token');
                return null;
            }

            const username = decoded.unique_name || decoded.email || decoded.preferred_username || decoded.name;
            const email = decoded.email || username;
            const fullName = decoded.fullName || decoded.name || decoded.given_name;
            const uniqueName = decoded.unique_name || email;

            let roles: string[] = [];
            if (decoded.role) {
                roles = Array.isArray(decoded.role) ? decoded.role : [decoded.role];
            }

            return {
                id: userId,
                username: username || email || 'Unknown',
                email: email || username || 'Unknown',
                fullName,
                uniqueName,
                roles
            };
        } catch (error) {
            console.error('Failed to extract user info from JWT token:', error);
            return null;
        }
    }, []);

    const isTokenExpired = useCallback((tokenString: string): boolean => {
        try {
            const decoded = jwtDecode<JwtPayload>(tokenString);
            const currentTime = Date.now();
            const expiryTime = decoded.exp * 1000;
            return currentTime >= (expiryTime - AUTH_CONFIG.TOKEN_REFRESH_THRESHOLD_MS);
        } catch {
            return true;
        }
    }, []);

    const hasRole = useCallback((role: string): boolean => {
        if (!user?.roles) return false;
        return user.roles.includes(role);
    }, [user?.roles]);

    const storeTokenExpiry = useCallback((tokenString: string) => {
        try {
            const decoded = jwtDecode<JwtPayload>(tokenString);
            if (decoded.exp) {
                const expiryTime = decoded.exp * 1000;
                localStorage.setItem(AUTH_CONFIG.STORAGE_KEYS.TOKEN_EXPIRY, expiryTime.toString());
            }
        } catch (error) {
            console.error("Failed to store token expiry:", error);
        }
    }, []);

    const secureStore = useCallback((key: string, value: string) => {
        localStorage.setItem(key, value);
        sessionStorage.setItem(`${AUTH_CONFIG.SESSION_STORAGE_PREFIX}${key}`, value);
    }, []);

    const login = useCallback(async (tokenData: LoginResponse): Promise<void> => {
        setIsLoading(true);
        setError(null);

        try {
            if (!tokenData.accessToken) {
                throw new Error("Invalid authentication response");
            }

            console.log('Processing login with token data...');

            const userInfoFromToken = extractUserInfoFromToken(tokenData.accessToken);
            if (!userInfoFromToken) {
                throw new Error("Failed to extract user information from token");
            }

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

            secureStore(AUTH_CONFIG.STORAGE_KEYS.ACCESS_TOKEN, tokenData.accessToken);
            if (tokenData.refreshToken) {
                secureStore(AUTH_CONFIG.STORAGE_KEYS.REFRESH_TOKEN, tokenData.refreshToken);
            }
            secureStore(AUTH_CONFIG.STORAGE_KEYS.USER, JSON.stringify(userInfo));
            storeTokenExpiry(tokenData.accessToken);

            setUser(userInfo);
            setToken(tokenData.accessToken);
            setIsInitialized(true);

            apiClient.setAuthToken(tokenData.accessToken);
            refreshAttempts.current = 0;

            console.log('Login completed successfully');

        } catch (error: any) {
            console.error("Login error:", error);
            const apiError = ApiErrorHandler.extractError(error);
            const userMessage = ApiErrorHandler.formatUserMessage(apiError);
            setError(userMessage);
            await logout();
        } finally {
            setIsLoading(false);
        }
    }, [extractUserInfoFromToken, secureStore, storeTokenExpiry]);

    const logout = useCallback(async (): Promise<void> => {
        setIsLoading(true);

        try {
            // Only attempt server logout if we have valid authentication
            if (user && token && isInitialized) {
                try {
                    await apiClient.post("/v1/auth/logout", undefined, {
                        skipCsrf: false,
                        timeout: 5000
                    });
                } catch (err) {
                    console.warn("Backend logout failed:", err);
                }
            }
        } catch (error) {
            console.error("Error during logout:", error);
        } finally {
            // Always clear local state
            localStorage.removeItem(AUTH_CONFIG.STORAGE_KEYS.USER);
            localStorage.removeItem(AUTH_CONFIG.STORAGE_KEYS.ACCESS_TOKEN);
            localStorage.removeItem(AUTH_CONFIG.STORAGE_KEYS.REFRESH_TOKEN);
            localStorage.removeItem(AUTH_CONFIG.STORAGE_KEYS.TOKEN_EXPIRY);

            Object.keys(sessionStorage).forEach(key => {
                if (key.startsWith(AUTH_CONFIG.SESSION_STORAGE_PREFIX)) {
                    sessionStorage.removeItem(key);
                }
            });

            setUser(null);
            setToken(null);
            setError(null);
            setIsLoading(false);
            setIsInitialized(true);

            refreshInProgress.current = false;
            refreshAttempts.current = 0;

            apiClient.clearTokens();
        }
    }, [user, token, isInitialized]);

    const refreshToken = useCallback(async (): Promise<boolean> => {
        // Prevent concurrent refresh attempts
        if (refreshInProgress.current) {
            console.log('Refresh already in progress');
            return false;
        }

        if (refreshAttempts.current >= AUTH_CONFIG.MAX_REFRESH_ATTEMPTS) {
            console.log('Max refresh attempts exceeded, logging out');
            await logout();
            return false;
        }

        if (!user || !token) {
            console.log('No user or token for refresh');
            return false;
        }

        const currentToken = localStorage.getItem(AUTH_CONFIG.STORAGE_KEYS.ACCESS_TOKEN);
        if (!currentToken) {
            console.log('No stored token for refresh');
            return false;
        }

        if (!isTokenExpired(currentToken)) {
            return true;
        }

        refreshInProgress.current = true;
        refreshAttempts.current += 1;

        try {
            console.log(`Token refresh attempt ${refreshAttempts.current}/${AUTH_CONFIG.MAX_REFRESH_ATTEMPTS}`);

            if (refreshAttempts.current > 1) {
                const delay = AUTH_CONFIG.REFRESH_BACKOFF_MS * (refreshAttempts.current - 1);
                await new Promise(resolve => setTimeout(resolve, delay));
            }

            const response = await apiClient.post<LoginResponse>("/v1/auth/refresh-token", undefined, {
                skipAuth: false,
                retryCount: 0,
                priority: 'high',
                timeout: 10000
            });

            if (response.success && response.data.accessToken) {
                console.log('Token refresh successful');

                const updatedUserInfo = extractUserInfoFromToken(response.data.accessToken);

                if (updatedUserInfo) {
                    const updatedRoles = updatedUserInfo.roles || [];
                    const currentRoles = user.roles || [];
                    const finalRoles = updatedRoles.length > 0 ? updatedRoles : currentRoles;

                    const mergedUserInfo: User = {
                        ...user,
                        ...updatedUserInfo,
                        roles: finalRoles
                    };

                    secureStore(AUTH_CONFIG.STORAGE_KEYS.ACCESS_TOKEN, response.data.accessToken);
                    secureStore(AUTH_CONFIG.STORAGE_KEYS.USER, JSON.stringify(mergedUserInfo));
                    setToken(response.data.accessToken);
                    setUser(mergedUserInfo);
                    storeTokenExpiry(response.data.accessToken);

                    apiClient.setAuthToken(response.data.accessToken);
                    refreshAttempts.current = 0;
                } else {
                    console.warn('Failed to extract user info from refreshed token');
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
            console.error(`Token refresh attempt ${refreshAttempts.current} failed:`, error);

            const apiError = ApiErrorHandler.extractError(error);

            if (apiError.isAuthError || refreshAttempts.current >= AUTH_CONFIG.MAX_REFRESH_ATTEMPTS) {
                console.log('Token refresh failed permanently, logging out');
                await logout();
            }

            return false;
        } finally {
            refreshInProgress.current = false;
        }
    }, [user, token, isTokenExpired, extractUserInfoFromToken, secureStore, storeTokenExpiry, logout]);

    // SAFE initialization that prevents infinite loops
    useEffect(() => {
        const initAuth = async () => {
            if (initializationStarted.current) {
                return;
            }
            initializationStarted.current = true;

            console.log('Initializing authentication...');

            try {
                const storedToken = localStorage.getItem(AUTH_CONFIG.STORAGE_KEYS.ACCESS_TOKEN);
                const storedUser = localStorage.getItem(AUTH_CONFIG.STORAGE_KEYS.USER);

                if (storedToken && storedUser) {
                    console.log('Found stored authentication data');

                    let parsedUser: User;
                    try {
                        parsedUser = JSON.parse(storedUser);
                    } catch (error) {
                        console.error('Failed to parse stored user:', error);
                        await logout();
                        return;
                    }

                    const tokenUserInfo = extractUserInfoFromToken(storedToken);
                    if (tokenUserInfo && tokenUserInfo.id === parsedUser.id) {
                        console.log('Stored authentication data validated');

                        apiClient.setAuthToken(storedToken);
                        setUser(parsedUser);
                        setToken(storedToken);

                        // Check if token needs refresh (non-blocking)
                        if (isTokenExpired(storedToken)) {
                            console.log('Token expired, will refresh in background');
                            refreshToken().catch(err => {
                                console.error('Background token refresh failed:', err);
                            });
                        }
                    } else {
                        console.log('Stored user data invalid, clearing');
                        await logout();
                    }
                } else {
                    console.log('No stored authentication found');
                }
            } catch (error) {
                console.error('Auth initialization error:', error);
                await logout();
            } finally {
                setIsInitialized(true);
                console.log('Authentication initialization complete');
            }
        };

        initAuth();
    }, []);

    // Token refresh interval - only if authenticated and initialized
    useEffect(() => {
        if (!user || !isInitialized) return;

        const tokenCheckInterval = setInterval(async () => {
            const accessToken = localStorage.getItem(AUTH_CONFIG.STORAGE_KEYS.ACCESS_TOKEN);

            if (accessToken && isTokenExpired(accessToken)) {
                console.log("Token expired, refreshing...");
                const success = await refreshToken();
                if (!success) {
                    console.log("Token refresh failed, user will be logged out");
                }
            }
        }, AUTH_CONFIG.TOKEN_CHECK_INTERVAL_MS);

        return () => clearInterval(tokenCheckInterval);
    }, [user, isInitialized, isTokenExpired, refreshToken]);

    // Update API authorization header when token changes
    useEffect(() => {
        if (token) {
            apiClient.setAuthToken(token);
        } else {
            apiClient.clearTokens();
        }
    }, [token]);

    const contextValue: AuthContextType = {
        user,
        token,
        isAuthenticated: !!user && !!token,
        isLoading,
        isInitialized,
        error,
        login,
        logout,
        refreshToken,
        hasRole,
        clearError
    };

    return (
        <AuthContext.Provider value={contextValue}>
            {children}
        </AuthContext.Provider>
    );
};

export const useAuth = (): AuthContextType => {
    const context = useContext(AuthContext);

    if (context === undefined) {
        throw new Error("useAuth must be used within an AuthProvider");
    }

    return context;
};

export const extractUserInfoFromJWT = (token: string): User | null => {
    try {
        const payload = token.split('.')[1];
        const decodedPayload = JSON.parse(atob(payload));

        let roles: string[] = [];
        if (decodedPayload.role) {
            roles = Array.isArray(decodedPayload.role) ? decodedPayload.role : [decodedPayload.role];
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