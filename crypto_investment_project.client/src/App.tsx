// src/App.tsx
import React from "react";
import { Route, Routes } from "react-router-dom";
import LandingPage from "./pages/LandingPage";
import AuthPage from "./pages/AuthPage";
import DashboardPage from "./pages/DashboardPage";
import ProtectedRoute from "./components/ProtectedRoute";
import SubscriptionCreationPage from "./pages/SubscriptionCreationPage";
import AdminPage from "./pages/AdminPage";

const App: React.FC = () => {
    return (
        <Routes>
            <Route path="/" element={<LandingPage />} />
            <Route path="*" element={<AuthPage />} />
            <Route path="/dashboard" element={<ProtectedRoute><DashboardPage /></ProtectedRoute>} />
            <Route path="/admin" element={<ProtectedRoute /*requiredRoles={['Admin']}*/><AdminPage /></ProtectedRoute>} />
            <Route path="/subscription/new" element={<ProtectedRoute><SubscriptionCreationPage /></ProtectedRoute>} />
        </Routes>
    );
};

export default App;
