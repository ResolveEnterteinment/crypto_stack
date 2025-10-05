// Updated AuthContext.tsx - Uses Centralized AuthService
import { jwtDecode } from "jwt-decode";
import React, { createContext, useCallback, useContext, useEffect, useState, useRef } from "react";
import { apiClient } from "../services/api";
import authService, { LoginResponse } from "../services/authService";

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
    isInitialized: boolean;
    error: string | null;
    login: (tokenData: LoginResponse) => Promise<void>;
    logout: () => Promise<void>;
    refreshToken: () => Promise<boolean>;
    hasRole: (role: string) => boolean;
    clearError: () => void;
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

const AuthContext = createContext<AuthContextType | undefined>(undefined);

const AUTH_CONFIG = {
    STORAGE_KEYS: {
        ACCESS_TOKEN: "access_token",
        TOKEN_EXPIRY: "token_expiry",
        USER: "user"
    },
    TOKEN_REFRESH_THRESHOLD_MS: 5 * 60 * 1000, // 5 minutes
    TOKEN_CHECK_INTERVAL_MS: 60 * 1000, // 1 minute
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

    // ✅ FIXED: Only store in localStorage - no duplication to sessionStorage
    const secureStore = useCallback((key: string, value: string) => {
        localStorage.setItem(key, value);
    }, []);

    const login = useCallback(async (tokenData: LoginResponse): Promise<void> => {
        setIsLoading(true);
        setError(null);

        try {
            if (!tokenData.accessToken) {
                throw new Error("Invalid authentication response");
            }

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

            // Store only access token
            secureStore(AUTH_CONFIG.STORAGE_KEYS.ACCESS_TOKEN, tokenData.accessToken);
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
            setError(error.message || 'Login failed');
            await logout();
        } finally {
            setIsLoading(false);
        }
    }, [extractUserInfoFromToken, secureStore, storeTokenExpiry]);

    const logout = useCallback(async (): Promise<void> => {
        setIsLoading(true);

        try {
            // Call backend logout via AuthService
            if (user && token && isInitialized) {
                try {
                    await authService.logout();
                } catch (err) {
                    console.warn("Backend logout failed:", err);
                }
            }
        } catch (error) {
            console.error("Error during logout:", error);
        } finally {
            // Clear local state
            localStorage.removeItem(AUTH_CONFIG.STORAGE_KEYS.USER);
            localStorage.removeItem(AUTH_CONFIG.STORAGE_KEYS.ACCESS_TOKEN);
            localStorage.removeItem(AUTH_CONFIG.STORAGE_KEYS.TOKEN_EXPIRY);

            // ✅ FIXED: Clean up session storage properly
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

            const response = await authService.refreshToken();

            if (response.accessToken) {
                console.log('Token refresh successful');

                const updatedUserInfo = extractUserInfoFromToken(response.accessToken);

                if (updatedUserInfo) {
                    const updatedRoles = updatedUserInfo.roles || [];
                    const currentRoles = user.roles || [];
                    const finalRoles = updatedRoles.length > 0 ? updatedRoles : currentRoles;

                    const mergedUserInfo: User = {
                        ...user,
                        ...updatedUserInfo,
                        roles: finalRoles
                    };

                    secureStore(AUTH_CONFIG.STORAGE_KEYS.ACCESS_TOKEN, response.accessToken);
                    secureStore(AUTH_CONFIG.STORAGE_KEYS.USER, JSON.stringify(mergedUserInfo));

                    setToken(response.accessToken);
                    setUser(mergedUserInfo);
                    storeTokenExpiry(response.accessToken);

                    apiClient.setAuthToken(response.accessToken);
                    refreshAttempts.current = 0;
                } else {
                    secureStore(AUTH_CONFIG.STORAGE_KEYS.ACCESS_TOKEN, response.accessToken);
                    setToken(response.accessToken);
                    storeTokenExpiry(response.accessToken);
                    apiClient.setAuthToken(response.accessToken);
                }

                return true;
            }

            throw new Error("Token refresh failed");
        } catch (error: any) {
            console.error(`Token refresh attempt ${refreshAttempts.current} failed:`, error);

            if (refreshAttempts.current >= AUTH_CONFIG.MAX_REFRESH_ATTEMPTS) {
                console.log('Token refresh failed permanently, logging out');
                await logout();
            }

            return false;
        } finally {
            refreshInProgress.current = false;
        }
    }, [user, token, isTokenExpired, extractUserInfoFromToken, secureStore, storeTokenExpiry, logout]);

    // SAFE initialization
    useEffect(() => {
        const initAuth = async () => {
            if (initializationStarted.current) return;
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

    // Token refresh interval
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

    // Update API authorization header
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