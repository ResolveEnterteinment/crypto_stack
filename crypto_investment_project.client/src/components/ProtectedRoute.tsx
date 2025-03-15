import React, { JSX } from "react";
import { Navigate } from "react-router-dom";
import { useAuth } from "../context/AuthContext";

const ProtectedRoute: React.FC<{ children: JSX.Element }> = ({ children }) => {
    const { user } = useAuth();

    return user ? children : <Navigate to="/auth" replace />;
};

export default ProtectedRoute;
