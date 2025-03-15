import React, { createContext, useContext, useEffect, useState } from "react";

interface AuthContextType {
    user: any;
    login: (tokenData: any) => void;
    logout: () => void;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
    const [user, setUser] = useState(() => {
        // Load user from localStorage
        const storedUser = localStorage.getItem("user");
        return storedUser ? JSON.parse(storedUser) : null;
    });

    const login = async (sessionData: any) => {
        // Save tokens
        localStorage.setItem("access_token", sessionData.accessToken);
        localStorage.setItem("refresh_token", sessionData.refreshToken);
        var userData = {
            id: sessionData.userId,
            username: sessionData.username,
            email: sessionData.email,
        }
        localStorage.setItem("user", JSON.stringify(userData)); // ✅ Store userId properly
        setUser(userData);
    };

    const logout = () => {
        localStorage.removeItem("user");
        localStorage.removeItem("access_token");
        localStorage.removeItem("refresh_token");
        setUser(null);
    };

    useEffect(() => {
        const checkTokenExpiration = () => {
            const accessToken = localStorage.getItem("access_token");
            if (!accessToken) {
                logout();
                return;
            }

            // Decode JWT expiry (if backend supports JWT)
            const tokenPayload = JSON.parse(atob(accessToken.split(".")[1]));
            const expiration = new Date(tokenPayload.exp * 1000);
            if (expiration < new Date()) {
                logout(); // Expired
            }
        };

        checkTokenExpiration();
        const interval = setInterval(checkTokenExpiration, 60 * 1000); // Check every minute
        return () => clearInterval(interval);
    }, []);

    return (
        <AuthContext.Provider value={{ user, login, logout }}>
            {children}
        </AuthContext.Provider>
    );
};

export const useAuth = () => {
    const context = useContext(AuthContext);
    if (!context) {
        throw new Error("useAuth must be used within an AuthProvider");
    }
    return context;
};
