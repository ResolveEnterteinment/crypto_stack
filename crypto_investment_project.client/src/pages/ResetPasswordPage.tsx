import React, { useState, useEffect } from "react";
import { Link, useNavigate, useLocation } from "react-router-dom";
import api from "../services/api";

const ResetPasswordPage: React.FC = () => {
    const [newPassword, setNewPassword] = useState("");
    const [confirmPassword, setConfirmPassword] = useState("");
    const [isSubmitting, setIsSubmitting] = useState(false);
    const [passwordError, setPasswordError] = useState("");
    const [confirmPasswordError, setConfirmPasswordError] = useState("");
    const [resetSuccess, setResetSuccess] = useState(false);
    const [error, setError] = useState("");
    const [csrfToken, setCsrfToken] = useState("");
    const navigate = useNavigate();
    const location = useLocation();
    
    // Get email and token from URL
    const queryParams = new URLSearchParams(location.search);
    const email = queryParams.get("email") || "";
    const token = queryParams.get("token") || "";
    
    useEffect(() => {
        // Redirect if email or token are missing
        if (!email || !token) {
            navigate("/auth");
            return;
        }
        
        // Fetch CSRF token
        const fetchCsrfToken = async () => {
            try {
                const response = await api.get("/v1/csrf/refresh");
                setCsrfToken(response.data.token);
            } catch (err) {
                console.error("Failed to fetch CSRF token:", err);
            }
        };

        fetchCsrfToken();
    }, [email, token, navigate]);
    
    const validatePassword = (password: string): boolean => {
        // At least 8 characters, 1 uppercase, 1 lowercase, 1 number, 1 special character
        const passwordRegex = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,}$/;
        const isValid = passwordRegex.test(password);
        setPasswordError(isValid ? "" : "Password must be at least 8 characters and include uppercase, lowercase, number, and special character");
        return isValid;
    };
    
    const validateConfirmPassword = (password: string, confirmPassword: string): boolean => {
        const isValid = password === confirmPassword;
        setConfirmPasswordError(isValid ? "" : "Passwords do not match");
        return isValid;
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setError("");

        // Validate form
        const isPasswordValid = validatePassword(newPassword);
        const isConfirmPasswordValid = validateConfirmPassword(newPassword, confirmPassword);
        
        if (!isPasswordValid || !isConfirmPasswordValid) {
            return;
        }

        setIsSubmitting(true);

        try {
            const response = await api.safeRequest('post', "/v1/auth/reset-password",
                { 
                    email: decodeURIComponent(email),
                    token: decodeURIComponent(token),
                    newPassword,
                    confirmPassword 
                },
                {
                    headers: {
                        'X-CSRF-TOKEN': csrfToken
                    }
                }
            );

            if (response.data.success) {
                setResetSuccess(true);
            } else {
                setError(response.message || "Failed to reset password. The link may have expired.");
            }
        } catch (err: any) {
            if (err.response?.data?.message) {
                setError(err.response.data.message);
            } else if (err.response?.status === 400) {
                setError("The password reset link is invalid or has expired. Please request a new one.");
            } else {
                setError("An error occurred. Please try again later.");
            }
        } finally {
            setIsSubmitting(false);
        }
    };

    if (resetSuccess) {
        return (
            <div className="min-h-screen flex items-center justify-center bg-gradient-to-br from-gray-900 to-gray-800">
                <div className="bg-white p-8 rounded-lg shadow-2xl w-full max-w-md">
                    <div className="text-center py-4">
                        <div className="mx-auto w-16 h-16 mb-4 bg-green-100 rounded-full flex items-center justify-center">
                            <svg xmlns="http://www.w3.org/2000/svg" className="h-10 w-10 text-green-600" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                            </svg>
                        </div>
                        <h2 className="text-2xl font-bold text-gray-800 mb-2">Password Reset Successful!</h2>
                        <p className="text-gray-600 mb-6">
                            Your password has been reset successfully. You can now log in with your new password.
                        </p>

                        <Link 
                            to="/auth" 
                            className="w-full bg-blue-600 hover:bg-blue-700 text-white py-3 rounded-lg transition-colors duration-200 font-medium inline-block"
                        >
                            Go to Login
                        </Link>
                    </div>
                </div>
            </div>
        );
    }

    return (
        <div className="min-h-screen flex items-center justify-center bg-gradient-to-br from-gray-900 to-gray-800">
            <div className="bg-white p-8 rounded-lg shadow-2xl w-full max-w-md">
                <h1 className="text-2xl font-bold text-center mb-6">Set New Password</h1>
                
                {error && (
                    <div className="bg-red-100 border border-red-400 text-red-700 px-4 py-3 rounded mb-4">
                        <span>{error}</span>
                    </div>
                )}
                
                <p className="text-gray-600 mb-6">
                    Enter your new password below.
                </p>
                
                <form onSubmit={handleSubmit} className="space-y-6">
                    <div>
                        <label className="block text-gray-700 font-medium mb-1">New Password</label>
                        <input
                            type="password"
                            value={newPassword}
                            onChange={(e) => {
                                setNewPassword(e.target.value);
                                if (passwordError) validatePassword(e.target.value);
                                if (confirmPasswordError && confirmPassword) validateConfirmPassword(e.target.value, confirmPassword);
                            }}
                            onBlur={() => validatePassword(newPassword)}
                            className={`w-full p-3 border rounded-lg focus:ring-2 focus:ring-blue-500 focus:outline-none transition ${passwordError ? "border-red-500" : "border-gray-300"}`}
                            required
                        />
                        {passwordError && <p className="text-red-500 text-sm mt-1">{passwordError}</p>}
                        <div className="mt-1 text-xs text-gray-500">
                            Password must be at least 8 characters and include uppercase, lowercase, number, and special character.
                        </div>
                    </div>

                    <div>
                        <label className="block text-gray-700 font-medium mb-1">Confirm Password</label>
                        <input
                            type="password"
                            value={confirmPassword}
                            onChange={(e) => {
                                setConfirmPassword(e.target.value);
                                if (confirmPasswordError) validateConfirmPassword(newPassword, e.target.value);
                            }}
                            onBlur={() => validateConfirmPassword(newPassword, confirmPassword)}
                            className={`w-full p-3 border rounded-lg focus:ring-2 focus:ring-blue-500 focus:outline-none transition ${confirmPasswordError ? "border-red-500" : "border-gray-300"}`}
                            required
                        />
                        {confirmPasswordError && <p className="text-red-500 text-sm mt-1">{confirmPasswordError}</p>}
                    </div>

                    <button
                        type="submit"
                        className="w-full bg-blue-600 hover:bg-blue-700 text-white py-3 rounded-lg transition-colors duration-200 font-medium flex justify-center items-center"
                        disabled={isSubmitting}
                    >
                        {isSubmitting ? (
                            <>
                                <svg className="animate-spin -ml-1 mr-3 h-5 w-5 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                                    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                                    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                                </svg>
                                Resetting Password...
                            </>
                        ) : (
                            "Reset Password"
                        )}
                    </button>
                </form>
                
                <div className="mt-6 text-center text-sm text-gray-600">
                    <Link to="/auth" className="text-blue-600 hover:text-blue-800 font-medium">Back to Login</Link>
                </div>
            </div>
        </div>
    );
};

export default ResetPasswordPage;