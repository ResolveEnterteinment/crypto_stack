// crypto_investment_project.client/src/components/ProtectedRoute.tsx

import React, { useState, useEffect, ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { Spin, Result, Button } from 'antd';

// Define the KYC level type for TypeScript
type KycLevel = 'NONE' | 'BASIC' | 'STANDARD' | 'ADVANCED' | 'ENHANCED';

// Define the props for the component
interface ProtectedRouteProps {
    children: ReactNode;
    requiredRoles?: string[] | null;
    requiredKycLevel?: KycLevel | null;
}

// Define interface for KYC status response
interface KycStatusResponse {
    status: 'APPROVED' | 'PENDING_VERIFICATION' | 'NOT_STARTED' | 'REJECTED';
    verificationLevel: KycLevel;
}

const ProtectedRoute: React.FC<ProtectedRouteProps> = ({
    children,
    requiredRoles = null,
    requiredKycLevel = null
}) => {
    // Use the auth context without type assertion
    const auth = useAuth();

    const [kycVerified, setKycVerified] = useState<boolean | null>(null);
    const [kycLoading, setKycLoading] = useState<boolean>(false);
    const location = useLocation();

    // Safely extract values from auth context
    const isAuthenticated = auth?.isAuthenticated || false;
    const user = auth?.user || null;
    const loading = auth?.isLoading || false;

    useEffect(() => {
        // Skip KYC check if not required
        if (!requiredKycLevel || !isAuthenticated) {
            setKycVerified(true);
            return;
        }

        // Check KYC status
        const checkKycStatus = async (): Promise<void> => {
            setKycLoading(true);
            try {
                const response = await fetch('/api/kyc/status', {
                    headers: {
                        'Authorization': `Bearer ${localStorage.getItem('token')}`,
                    },
                });

                if (!response.ok) {
                    throw new Error('Failed to fetch KYC status');
                }

                const data: KycStatusResponse = await response.json();

                // Determine if verified based on status and level
                const isVerified =
                    data.status === 'APPROVED' &&
                    getKycLevelValue(data.verificationLevel) >= getKycLevelValue(requiredKycLevel);

                setKycVerified(isVerified);
            } catch (err) {
                console.error('Error checking KYC status:', err);
                setKycVerified(false);
            } finally {
                setKycLoading(false);
            }
        };

        checkKycStatus();
    }, [isAuthenticated, requiredKycLevel]);

    // Helper to convert KYC level to numeric value for comparison
    const getKycLevelValue = (level: KycLevel): number => {
        switch (level) {
            case 'NONE': return 0;
            case 'BASIC': return 1;
            case 'STANDARD': return 2;
            case 'ADVANCED': return 3;
            case 'ENHANCED': return 4;
            default: return 0;
        }
    };

    // Check if user has required roles
    const hasRequiredRoles = (): boolean => {
        if (!requiredRoles || requiredRoles.length === 0) {
            return true; // No roles required
        }

        if (!user?.roles || user.roles.length === 0) {
            return false; // User has no roles but roles are required
        }

        // Check if user has at least one of the required roles
        return requiredRoles.some(role => user?.roles?.includes(role));
    };

    if (loading || kycLoading) {
        return (
            <div className="flex items-center justify-center h-screen">
                <Spin size="large" />
            </div>
        );
    }

    if (!isAuthenticated) {
        return <Navigate to="/login" state={{ from: location }} replace />;
    }

    // Check for required roles
    if (requiredRoles && !hasRequiredRoles()) {
        return (
            <Result
                status="403"
                title="Unauthorized"
                subTitle="You do not have the required permissions to access this page."
                extra={
                    <Button type="primary" onClick={() => window.location.href = '/dashboard'}>
                        Return to Dashboard
                    </Button>
                }
            />
        );
    }

    // Check for required KYC level
    if (requiredKycLevel && !kycVerified) {
        return (
            <Result
                status="403"
                title="KYC Verification Required"
                subTitle={`You need to complete KYC verification (level: ${requiredKycLevel}) to access this page.`}
                extra={
                    <Button type="primary" onClick={() => window.location.href = '/kyc-verification'}>
                        Complete Verification
                    </Button>
                }
            />
        );
    }

    // If all checks pass, render the protected content
    return <>{children}</>;
};

export default ProtectedRoute;