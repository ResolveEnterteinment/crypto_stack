// src/components/ProtectedRoute.tsx
import React, { useState, useEffect } from "react";
import { Navigate, useLocation } from "react-router-dom";
import { useAuth } from "../context/AuthContext";

interface ProtectedRouteProps {
    children: React.ReactNode;
    requiredRoles?: string[];
    redirectPath?: string;
}

/**
 * Enhanced Protected Route component with role-based authorization and token verification
 * @param children Components to render if user is authenticated
 * @param requiredRoles Optional array of roles required to access this route
 * @param redirectPath Path to redirect to if authentication fails (default: /auth)
 */
const ProtectedRoute: React.FC<ProtectedRouteProps> = ({
    children,
    requiredRoles = [],
    redirectPath = "/auth"
}) => {
    const { isAuthenticated, refreshToken, hasRole } = useAuth();
    const [isVerifying, setIsVerifying] = useState(true);
    const [isAuthorized, setIsAuthorized] = useState(false);
    const location = useLocation();

    useEffect(() => {
        // Function to verify token and check roles
        const verifyAccess = async () => {
            setIsVerifying(true);

            // If not authenticated at all, no need for further checks
            if (!isAuthenticated) {
                setIsAuthorized(false);
                setIsVerifying(false);
                return;
            }

            // Try to refresh token if needed
            const isValidToken = await refreshToken();

            if (!isValidToken) {
                setIsAuthorized(false);
                setIsVerifying(false);
                return;
            }

            // Check role-based access if required
            if (requiredRoles.length > 0) {
                const hasRequiredRole = requiredRoles.some(role => hasRole(role));
                setIsAuthorized(hasRequiredRole);
            } else {
                // No specific roles required, just authentication
                setIsAuthorized(true);
            }

            setIsVerifying(false);
        };

        verifyAccess();
    }, [isAuthenticated, refreshToken, requiredRoles, hasRole]);

    // Show loading state while verifying
    if (isVerifying) {
        return (
            <div className="flex justify-center items-center h-screen bg-gray-100">
                <div className="bg-white p-6 rounded-lg shadow-md text-center">
                    <div className="animate-spin w-10 h-10 border-4 border-blue-500 border-t-transparent rounded-full mx-auto mb-4"></div>
                    <p className="text-gray-600">Verifying authentication...</p>
                </div>
            </div>
        );
    }

    // If not authorized, redirect to login with return URL
    if (!isAuthorized) {
        return <Navigate to={redirectPath} state={{ from: location.pathname }} replace />;
    }

    // If user is authenticated and authorized, render children
    return <>{children}</>;
};

export default ProtectedRoute;