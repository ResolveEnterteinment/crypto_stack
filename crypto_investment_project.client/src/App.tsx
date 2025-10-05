import React from 'react';
import { BrowserRouter, Route, Routes } from 'react-router-dom';
import { AuthProvider } from './context/AuthContext';
import { NotificationProvider } from './context/NotificationContext';

// Pages
import LandingPage from './pages/LandingPage';
import AuthPage from './pages/AuthPage';
import DashboardPage from './pages/DashboardPage';
import SubscriptionCreationPage from './pages/SubscriptionCreationPage';
import AdminPage from './pages/AdminPage';
import WithdrawalPage from './pages/WithdrawalPage';
import KycPage from './pages/KycPage';
import EmailConfirmation from './pages/EmailConfirmation';
import ForgotPasswordPage from './pages/ForgotPasswordPage';
import ResetPasswordPage from './pages/ResetPasswordPage';
import PaymentCancelPage from './pages/PaymentCancelPage';
import PaymentSuccessPage from './pages/PaymentSuccessPage';

// Components
import ProtectedRoute from './components/ProtectedRoute';

/**
 * Main Application Component
 * 
 * Handles routing and provides global context providers for:
 * - Authentication (AuthProvider)
 * - Notifications (NotificationProvider)
 * 
 * Route Structure:
 * - Public Routes: Landing, Login, Email Confirmation, Password Reset
 * - Protected Routes: Dashboard, Subscriptions, KYC, Withdrawals
 * - Admin Routes: Admin Panel (role-based)
 */
const App: React.FC = () => {
    return (
        <AuthProvider>
            <NotificationProvider>
                <BrowserRouter>
                    <Routes>
                        {/* ========================================
                            PUBLIC ROUTES - No authentication required
                            ======================================== */}

                        {/* Landing Page - Clean, professional homepage */}
                        <Route path="/" element={<LandingPage />} />

                        {/* Authentication Routes */}
                        <Route path="/auth/login" element={<AuthPage />} />
                        <Route path="/auth/register" element={<AuthPage />} />
                        <Route path="/login" element={<AuthPage />} />
                        <Route path="/register" element={<AuthPage />} />

                        {/* Email & Password Management */}
                        <Route path="/confirm-email" element={<EmailConfirmation />} />
                        <Route path="/forgot-password" element={<ForgotPasswordPage />} />
                        <Route path="/reset-password" element={<ResetPasswordPage />} />

                        {/* ========================================
                            PROTECTED ROUTES - Authentication required
                            ======================================== */}

                        {/* Dashboard - Main user interface */}
                        <Route
                            path="/dashboard"
                            element={
                                <ProtectedRoute>
                                    <DashboardPage />
                                </ProtectedRoute>
                            }
                        />

                        {/* ========================================
                            KYC VERIFICATION ROUTES
                            ======================================== */}

                        {/* KYC Verification - All levels */}
                        <Route
                            path="/kyc-verification"
                            element={
                                <ProtectedRoute>
                                    <KycPage />
                                </ProtectedRoute>
                            }
                        />

                        {/* KYC Start - Redirects to verification with new session */}
                        <Route
                            path="/kyc"
                            element={
                                <ProtectedRoute>
                                    <KycPage />
                                </ProtectedRoute>
                            }
                        />

                        {/* ========================================
                            SUBSCRIPTION ROUTES - Basic KYC required
                            ======================================== */}

                        {/* Create New Subscription */}
                        <Route
                            path="/subscription/new"
                            element={
                                <ProtectedRoute>
                                    <SubscriptionCreationPage />
                                </ProtectedRoute>
                            }
                        />

                        {/* ========================================
                            PAYMENT ROUTES
                            ======================================== */}

                        {/* Payment Success */}
                        <Route
                            path="/payment/checkout/success"
                            element={
                                <ProtectedRoute>
                                    <PaymentSuccessPage />
                                </ProtectedRoute>
                            }
                        />

                        {/* Payment Cancel */}
                        <Route
                            path="/payment/checkout/cancel"
                            element={
                                <ProtectedRoute>
                                    <PaymentCancelPage />
                                </ProtectedRoute>
                            }
                        />

                        {/* ========================================
                            WITHDRAWAL ROUTES - No KYC required to view the page.
                            Withdrawal form requires Basic KYC
                            ======================================== */}

                        <Route
                            path="/withdraw"
                            element={
                                <ProtectedRoute>
                                    <WithdrawalPage />
                                </ProtectedRoute>
                            }
                        />

                        {/* ========================================
                            TRADING ROUTES - Standard KYC required
                            ======================================== */}

                        {/* Exchange Trading */}
                        <Route
                            path="/exchange"
                            element={
                                <ProtectedRoute requiredKycLevel="STANDARD">
                                    <DashboardPage />
                                </ProtectedRoute>
                            }
                        />

                        {/* Portfolio Management */}
                        <Route
                            path="/portfolio"
                            element={
                                <ProtectedRoute requiredKycLevel="STANDARD">
                                    <DashboardPage />
                                </ProtectedRoute>
                            }
                        />

                        {/* ========================================
                            ADVANCED TRADING - Advanced KYC required
                            ======================================== */}

                        <Route
                            path="/advanced-trading"
                            element={
                                <ProtectedRoute requiredKycLevel="ADVANCED">
                                    <DashboardPage />
                                </ProtectedRoute>
                            }
                        />

                        {/* ========================================
                            ADMIN ROUTES - Admin role required
                            ======================================== */}

                        {/* Admin Panel */}
                        <Route
                            path="/admin"
                            element={
                                <ProtectedRoute requiredRoles={['ADMIN']}>
                                    <AdminPage />
                                </ProtectedRoute>
                            }
                        />

                        {/* Admin KYC Management */}
                        <Route
                            path="/admin/kyc"
                            element={
                                <ProtectedRoute requiredRoles={['ADMIN']}>
                                    <AdminPage />
                                </ProtectedRoute>
                            }
                        />

                        {/* ========================================
                            STATIC/INFO PAGES - Public access
                            ======================================== */}

                        {/* TODO: Implement these pages */}
                        {/* 
                        <Route path="/features" element={<FeaturesPage />} />
                        <Route path="/pricing" element={<PricingPage />} />
                        <Route path="/security" element={<SecurityPage />} />
                        <Route path="/learn" element={<LearnPage />} />
                        <Route path="/terms" element={<TermsPage />} />
                        <Route path="/privacy" element={<PrivacyPage />} />
                        <Route path="/compliance" element={<CompliancePage />} />
                        */}

                        {/* ========================================
                            CATCH-ALL ROUTE - 404 Handler
                            ======================================== */}

                        {/* Catch-all - Redirect to auth page */}
                        <Route path="*" element={<AuthPage />} />
                    </Routes>
                </BrowserRouter>
            </NotificationProvider>
        </AuthProvider>
    );
};

export default App;