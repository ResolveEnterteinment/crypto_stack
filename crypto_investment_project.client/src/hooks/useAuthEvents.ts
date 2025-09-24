// Custom hook for handling authentication events
import { useEffect } from 'react';
import { useAuth } from '../context/AuthContext';

export const useAuthEvents = () => {
    const { logout } = useAuth();

    useEffect(() => {
        const handleTokenExpired = () => {
            console.log('🚨 Token expired event received');
            logout();
        };

        const handleAuthFailure = () => {
            console.log('🚨 Auth failure event received');
            logout();
        };

        window.addEventListener('token-expired', handleTokenExpired);
        window.addEventListener('auth-failure', handleAuthFailure);

        return () => {
            window.removeEventListener('token-expired', handleTokenExpired);
            window.removeEventListener('auth-failure', handleAuthFailure);
        };
    }, [logout]);
};