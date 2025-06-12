// src/App.tsx
import React from "react";
import { Route, Routes } from "react-router-dom";
import LandingPage from "./pages/LandingPage";
import AuthPage from "./pages/AuthPage";
import DashboardPage from "./pages/DashboardPage";
import ProtectedRoute from "./components/ProtectedRoute";
import SubscriptionCreationPage from "./pages/SubscriptionCreationPage";
import AdminPage from "./pages/AdminPage";
import WithdrawalPage from "./pages/WithdrawalPage";
import KycPage from "./pages/KycPage";
import EmailConfirmation from './pages/EmailConfirmation';
import Navbar from "./components/Navbar";
import ForgotPasswordPage from "./pages/ForgotPasswordPage";
import ResetPasswordPage from "./pages/ResetPasswordPage";

const App: React.FC = () => {
    return (
        <Routes>
            <Route path="/" element={<LandingPage />} />
            <Route path="/login" element={<AuthPage />} />
            <Route path="/dashboard" element={
                <ProtectedRoute>
                    <Navbar />
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
    );
};

export default App;