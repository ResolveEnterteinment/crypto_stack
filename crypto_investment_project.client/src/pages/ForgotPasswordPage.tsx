import React, { useState } from "react";
import { Link } from "react-router-dom";
import api from "../services/api";

const ForgotPasswordPage: React.FC = () => {
    const [email, setEmail] = useState("");
    const [isSubmitting, setIsSubmitting] = useState(false);
    const [emailError, setEmailError] = useState("");
    const [requestSent, setRequestSent] = useState(false);
    const [error, setError] = useState("");
    const [csrfToken, setCsrfToken] = useState("");
    
    React.useEffect(() => {
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
    }, []);

    const validateEmail = (email: string): boolean => {
        const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
        const isValid = emailRegex.test(email);
        setEmailError(isValid ? "" : "Please enter a valid email address");
        return isValid;
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setError("");

        // Validate form
        if (!validateEmail(email)) {
            return;
        }

        setIsSubmitting(true);

        try {
            const response = await api.safeRequest('post', "/v1/auth/forgot-password",
                { email },
                {
                    headers: {
                        'X-CSRF-TOKEN': csrfToken
                    }
                }
            );

            if (response.data.success) {
                setRequestSent(true);
            } else {
                setError(response.message || "Failed to send password reset email. Please try again.");
            }
        } catch (err: any) {
            if (err.response?.data?.message) {
                setError(err.response.data.message);
            } else {
                setError("An error occurred. Please try again later.");
            }
        } finally {
            setIsSubmitting(false);
        }
    };

    if (requestSent) {
        return (
            <div className="min-h-screen flex items-center justify-center bg-gradient-to-br from-gray-900 to-gray-800">
                <div className="bg-white p-8 rounded-lg shadow-2xl w-full max-w-md">
                    <div className="text-center py-4">
                        <div className="mx-auto w-16 h-16 mb-4 bg-green-100 rounded-full flex items-center justify-center">
                            <svg xmlns="http://www.w3.org/2000/svg" className="h-10 w-10 text-green-600" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                            </svg>
                        </div>
                        <h2 className="text-2xl font-bold text-gray-800 mb-2">Check Your Email</h2>
                        <p className="text-gray-600 mb-6">
                            We've sent password reset instructions to <span className="font-medium text-gray-800">{email}</span>.
                            Please check your email to reset your password.
                        </p>

                        <div className="bg-blue-50 p-4 rounded-lg mb-6 text-sm text-blue-800">
                            <p>
                                <svg className="inline-block w-5 h-5 mr-1 -mt-1" fill="currentColor" viewBox="0 0 20 20" xmlns="http://www.w3.org/2000/svg">
                                    <path fillRule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7-4a1 1 0 11-2 0 1 1 0 012 0zM9 9a1 1 0 000 2v3a1 1 0 001 1h1a1 1 0 100-2v-3a1 1 0 00-1-1H9z" clipRule="evenodd" />
                                </svg>
                                If you don't see the email in your inbox, please check your spam folder.
                            </p>
                        </div>

                        <Link 
                            to="/auth" 
                            className="w-full bg-blue-600 hover:bg-blue-700 text-white py-3 rounded-lg transition-colors duration-200 font-medium inline-block"
                        >
                            Return to Login
                        </Link>
                    </div>
                </div>
            </div>
        );
    }

    return (
        <div className="min-h-screen flex items-center justify-center bg-gradient-to-br from-gray-900 to-gray-800">
            <div className="bg-white p-8 rounded-lg shadow-2xl w-full max-w-md">
                <h1 className="text-2xl font-bold text-center mb-6">Reset Your Password</h1>
                
                {error && (
                    <div className="bg-red-100 border border-red-400 text-red-700 px-4 py-3 rounded mb-4">
                        <span>{error}</span>
                    </div>
                )}
                
                <p className="text-gray-600 mb-6">
                    Enter your email address below and we'll send you instructions to reset your password.
                </p>
                
                <form onSubmit={handleSubmit} className="space-y-6">
                    <div>
                        <label className="block text-gray-700 font-medium mb-1">Email Address</label>
                        <input
                            type="email"
                            value={email}
                            onChange={(e) => {
                                setEmail(e.target.value);
                                if (emailError) validateEmail(e.target.value);
                            }}
                            onBlur={() => validateEmail(email)}
                            className={`w-full p-3 border rounded-lg focus:ring-2 focus:ring-blue-500 focus:outline-none transition ${emailError ? "border-red-500" : "border-gray-300"}`}
                            required
                        />
                        {emailError && <p className="text-red-500 text-sm mt-1">{emailError}</p>}
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
                                Sending...
                            </>
                        ) : (
                            "Send Reset Instructions"
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

export default ForgotPasswordPage;