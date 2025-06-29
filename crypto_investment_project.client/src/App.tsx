// src/App.tsx
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
                        <Route path="/" element={<LandingPage />} />
                        <Route path="/login" element={<AuthPage />} />
                        <Route path="/dashboard" element={
                            <ProtectedRoute>
                                <DashboardPage />
                            </ProtectedRoute>} />

                        {/* Admin route with role-based access control */}
                        <Route path="/admin" element={
                            <ProtectedRoute requiredRoles={['ADMIN']}>
                                <AdminPage />
                            </ProtectedRoute>
                        } />

                        {/* Standard subscription requires KYC */}
                        <Route path="/subscription/new" element={
                            <ProtectedRoute>
                                <SubscriptionCreationPage />
                            </ProtectedRoute>
                        } />

                        {/* Payment checkout success */}
                        <Route path="/payment/checkout/success" element={
                            <ProtectedRoute>
                                <PaymentSuccessPage />
                            </ProtectedRoute>
                        } />

                        {/* Payment checkout cancelled */}
                        <Route path="/payment/checkout/cancel" element={
                            <ProtectedRoute>
                                <PaymentCancelPage />
                            </ProtectedRoute>
                        } />

                        {/* Standard subscription requires KYC */}
                        <Route path="/withdraw" element={
                            <ProtectedRoute /*requiredKycLevel="STANDARD"*/>
                                <WithdrawalPage />
                            </ProtectedRoute>
                        } />

                        {/* KYC verification page */}
                        <Route path="/kyc-verification" element={
                            <ProtectedRoute>
                                <KycPage />
                            </ProtectedRoute>
                        } />

                        {/* Email confirmation routes */}
                        <Route path="/confirm-email" element={<EmailConfirmation />} />
                        <Route path="/forgot-password" element={<ForgotPasswordPage />} />
                        <Route path="/reset-password" element={<ResetPasswordPage />} />

                        {/* Redirect all other routes to auth page */}
                        <Route path="*" element={<AuthPage />} />
                                </Routes>
                </BrowserRouter>
            </NotificationProvider>
        </AuthProvider>
    );
};

export default App;