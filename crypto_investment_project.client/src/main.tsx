import React from 'react';
import ReactDOM from 'react-dom/client';
import './styles/reset.css';
import './styles/variables.css';
import './styles/global.css';
import App from './App';
import './index.css';

// Performance monitoring
const PERFORMANCE_CONFIG = {
    enableTracking: import.meta.env.PROD,
    trackPageLoad: true,
    trackUserInteraction: true,
};

// Track initial page load performance
if (PERFORMANCE_CONFIG.enableTracking && PERFORMANCE_CONFIG.trackPageLoad) {
    window.addEventListener('load', () => {
        const perfData = performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming;
        if (perfData) {
            console.log('Page Load Performance:', {
                dns: perfData.domainLookupEnd - perfData.domainLookupStart,
                tcp: perfData.connectEnd - perfData.connectStart,
                request: perfData.responseStart - perfData.requestStart,
                response: perfData.responseEnd - perfData.responseStart,
                dom: perfData.domComplete - perfData.domInteractive,
                total: perfData.loadEventEnd - perfData.fetchStart,
            });
        }
    });
}

// Global error boundary handler
window.addEventListener('error', (event) => {
    console.error('Global Error:', {
        message: event.message,
        filename: event.filename,
        lineno: event.lineno,
        colno: event.colno,
        error: event.error,
    });

    // In production, send to error tracking service
    if (import.meta.env.PROD) {
        // TODO: Send to Sentry, LogRocket, or your error tracking service
    }
});

// Global promise rejection handler
window.addEventListener('unhandledrejection', (event) => {
    console.error('Unhandled Promise Rejection:', {
        reason: event.reason,
        promise: event.promise,
    });

    // In production, send to error tracking service
    if (import.meta.env.PROD) {
        // TODO: Send to error tracking service
    }
});

// Show loading indicator
const showLoadingIndicator = () => {
    const root = document.getElementById('root');
    if (root && root.children.length === 0) {
        root.innerHTML = `
            <div style="
                display: flex;
                align-items: center;
                justify-content: center;
                min-height: 100vh;
                background: #ffffff;
            ">
                <div style="text-align: center;">
                    <div style="
                        width: 48px;
                        height: 48px;
                        border: 4px solid #f0f0f0;
                        border-top-color: #1890ff;
                        border-radius: 50%;
                        animation: spin 1s linear infinite;
                        margin: 0 auto 16px;
                    "></div>
                    <p style="
                        color: #595959;
                        font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
                        font-size: 14px;
                        margin: 0;
                    ">Loading application...</p>
                </div>
            </div>
            <style>
                @keyframes spin {
                    to { transform: rotate(360deg); }
                }
            </style>
        `;
    }
};

// Show error message
const showErrorMessage = (error: Error) => {
    const root = document.getElementById('root');
    if (root) {
        root.innerHTML = `
            <div style="
                display: flex;
                align-items: center;
                justify-content: center;
                min-height: 100vh;
                background: #f0f2f5;
                padding: 24px;
            ">
                <div style="
                    max-width: 500px;
                    background: white;
                    border-radius: 8px;
                    padding: 32px;
                    box-shadow: 0 2px 8px rgba(0,0,0,0.1);
                    text-align: center;
                ">
                    <div style="
                        width: 64px;
                        height: 64px;
                        border-radius: 50%;
                        background: #fff1f0;
                        margin: 0 auto 24px;
                        display: flex;
                        align-items: center;
                        justify-content: center;
                    ">
                        <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#ff4d4f" stroke-width="2">
                            <circle cx="12" cy="12" r="10"></circle>
                            <line x1="12" y1="8" x2="12" y2="12"></line>
                            <line x1="12" y1="16" x2="12.01" y2="16"></line>
                        </svg>
                    </div>
                    <h1 style="
                        font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
                        font-size: 24px;
                        font-weight: 600;
                        color: #262626;
                        margin: 0 0 16px 0;
                    ">Failed to Initialize</h1>
                    <p style="
                        font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
                        color: #595959;
                        margin: 0 0 24px 0;
                        line-height: 1.6;
                    ">${error.message || 'An unexpected error occurred while loading the application.'}</p>
                    <button
                        onclick="window.location.reload()"
                        style="
                            background: #1890ff;
                            color: white;
                            border: none;
                            padding: 12px 32px;
                            border-radius: 6px;
                            font-size: 14px;
                            font-weight: 600;
                            cursor: pointer;
                            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
                        "
                        onmouseover="this.style.background='#40a9ff'"
                        onmouseout="this.style.background='#1890ff'"
                    >Reload Page</button>
                </div>
            </div>
        `;
    }
};

/**
 * Initialize the application
 * Handles authentication setup, CSRF token initialization, and app rendering
 */
async function initializeApp() {
    const startTime = performance.now();

    try {
        // Show loading indicator
        showLoadingIndicator();

        console.log('[App Init] Starting application initialization...');

        // Check if root element exists
        const rootElement = document.getElementById('root');
        if (!rootElement) {
            throw new Error('Root element not found. Please ensure index.html has a div with id="root"');
        }

        // Create React root and render app
        console.log('[App Init] Rendering application...');
        const root = ReactDOM.createRoot(rootElement);

        root.render(
            <React.StrictMode>
                <App />
            </React.StrictMode>
        );

        const initTime = performance.now() - startTime;
        console.log(`[App Init] ✓ Application initialized successfully in ${initTime.toFixed(2)}ms`);

        // Track initialization time
        if (PERFORMANCE_CONFIG.enableTracking) {
            console.log('[Performance] App initialization time:', {
                duration: initTime,
                timestamp: new Date().toISOString(),
            });
        }

    } catch (error) {
        const initError = error as Error;
        console.error('[App Init] ✗ Critical initialization error:', initError);

        // Show error message to user
        showErrorMessage(initError);

        // In production, send to error tracking service
        if (import.meta.env.PROD) {
            // TODO: Send to error tracking service
            console.error('[Error Tracking] Send to monitoring service:', {
                error: initError.message,
                stack: initError.stack,
                timestamp: new Date().toISOString(),
            });
        }
    }
}

// Start the application
console.log('[App] Starting application...');
console.log('[App] Environment:', import.meta.env.MODE);
console.log('[App] API URL:', import.meta.env.VITE_API_BASE_URL);

initializeApp();

// Export for debugging in development
if (import.meta.env.DEV) {
    (window as any).__APP_DEBUG__ = {
        version: '1.0.0',
        environment: import.meta.env.MODE,
        apiUrl: import.meta.env.VITE_API_BASE_URL,
        reinitialize: initializeApp,
    };
    console.log('[Dev] Debug tools available at window.__APP_DEBUG__');
}