// src/App.tsx - Enhanced with improved KYC integration
import React from "react";
import { BrowserRouter, Route, Routes } from "react-router-dom";
import LandingPage from "./pages/LandingPage";
import AuthPage from "./pages/AuthPage";
import DashboardPage from "./pages/DashboardPage";
import ProtectedRoute from "./components/ProtectedRoute";
import SubscriptionCreationPage from "./pages/SubscriptionCreationPage";
import AdminPage from "./pages/AdminPage";
import WithdrawalPage from "./pages/WithdrawalPage";
import KycPage from "./pages/KycPage";
import EmailConfirmation from './pages/EmailConfirmation';
import ForgotPasswordPage from "./pages/ForgotPasswordPage";
import ResetPasswordPage from "./pages/ResetPasswordPage";
import { AuthProvider } from "./context/AuthContext";
import { NotificationProvider } from "./context/NotificationContext";
import PaymentCancelPage from "./pages/PaymentCancelPage";
import PaymentSuccessPage from "./pages/PaymentSuccessPage";

const App: React.FC = () => {
    return (
        <AuthProvider>
            <NotificationProvider>
                <BrowserRouter>
                    <Routes>
                        {/* Public Routes */}
                        <Route path="/" element={<LandingPage />} />
                        <Route path="/login" element={<AuthPage />} />
                        <Route path="/confirm-email" element={<EmailConfirmation />} />
                        <Route path="/forgot-password" element={<ForgotPasswordPage />} />
                        <Route path="/reset-password" element={<ResetPasswordPage />} />

                        {/* Protected Routes - Basic Authentication Required */}
                        <Route path="/dashboard" element={
                            <ProtectedRoute>
                                <DashboardPage />
                            </ProtectedRoute>
                        } />

                        {/* KYC Verification Route - Authentication Required */}
                        <Route path="/kyc-verification" element={
                            <ProtectedRoute>
                                <KycPage />
                            </ProtectedRoute>
                        } />

                        {/* KYC Start Route - Redirects to verification with new session */}
                        <Route path="/kyc" element={
                            <ProtectedRoute>
                                <KycPage />
                            </ProtectedRoute>
                        } />

                        {/* Admin Routes - Role-based Access Control */}
                        <Route path="/admin" element={
                            <ProtectedRoute requiredRoles={['ADMIN']}>
                                <AdminPage />
                            </ProtectedRoute>
                        } />

                        <Route path="/admin/kyc" element={
                            <ProtectedRoute requiredRoles={['ADMIN']}>
                                <AdminPage />
                            </ProtectedRoute>
                        } />

                        {/* Subscription Routes - KYC Basic Level Required */}
                        <Route path="/subscription/new" element={
                            <ProtectedRoute>
                                <SubscriptionCreationPage />
                            </ProtectedRoute>
                        } />

                        {/* Payment Routes - KYC Basic Level Required */}
                        <Route path="/payment/checkout/success" element={
                            <ProtectedRoute>
                                <PaymentSuccessPage />
                            </ProtectedRoute>
                        } />

                        <Route path="/payment/checkout/cancel" element={
                            <ProtectedRoute>
                                <PaymentCancelPage />
                            </ProtectedRoute>
                        } />

                        {/* Withdrawal Routes - KYC Standard Level Required */}
                        <Route path="/withdraw" element={
                                <WithdrawalPage />
                        } />

                        {/* Trading Routes - KYC Standard Level Required */}
                        <Route path="/exchange" element={
                            <ProtectedRoute requiredKycLevel="STANDARD">
                                <DashboardPage />
                            </ProtectedRoute>
                        } />

                        {/* Portfolio Routes - KYC Standard Level Required */}
                        <Route path="/portfolio" element={
                            <ProtectedRoute requiredKycLevel="STANDARD">
                                <DashboardPage />
                            </ProtectedRoute>
                        } />

                        {/* Advanced Trading Routes - KYC Advanced Level Required */}
                        <Route path="/advanced-trading" element={
                            <ProtectedRoute requiredKycLevel="ADVANCED">
                                <DashboardPage />
                            </ProtectedRoute>
                        } />

                        {/* Catch-all Route */}
                        <Route path="*" element={<AuthPage />} />
                    </Routes>
                </BrowserRouter>
            </NotificationProvider>
        </AuthProvider>
    );
};

export default App;