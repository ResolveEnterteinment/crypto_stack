// src/App.tsx
import React from "react";
import { Route, Routes } from "react-router-dom";
import LandingPage from "./pages/LandingPage";
import AuthPage from "./pages/AuthPage";
import DashboardPage from "./pages/DashboardPage";
import ProtectedRoute from "./components/ProtectedRoute";
import SubscriptionCreationPage from "./pages/SubscriptionCreationPage";
import AdminPage from "./pages/AdminPage";
import KycVerification from "./components/KYC/KycVerification";
import WithdrawalForm from "./components/Withdrawal/WithdrawalForm";

const App: React.FC = () => {
    return (
        <Routes>
            <Route path="/" element={<LandingPage />} />
            <Route path="/login" element={<AuthPage />} />
            <Route path="/dashboard" element={<ProtectedRoute><DashboardPage /></ProtectedRoute>} />

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
                <ProtectedRoute requiredKycLevel="BASIC">
                    <WithdrawalForm />
                </ProtectedRoute>
            } />

            {/* KYC verification page */}
            <Route path="/kyc-verification" element={
                <ProtectedRoute>
                    <KycVerification />
                </ProtectedRoute>
            } />

            {/* Redirect all other routes to auth page */}
            <Route path="*" element={<AuthPage />} />
        </Routes>
    );
};

export default App;