import React, { useState, useEffect } from "react";
import { useAuth } from "../context/AuthContext";
import { useNavigate } from "react-router-dom";
import api from "../services/api";

const AuthPage: React.FC = () => {
    const { user, login } = useAuth();
    const [isLogin, setIsLogin] = useState(true);
    const [email, setEmail] = useState("");
    const [password, setPassword] = useState("");
    const [fullName, setFullName] = useState("");
    const [error, setError] = useState("");
    const [isLoading, setIsLoading] = useState(false);
    const [showPassword, setShowPassword] = useState(false);
    const [csrfToken, setCsrfToken] = useState("");
    const navigate = useNavigate();

    // Validation states
    const [emailError, setEmailError] = useState("");
    const [passwordError, setPasswordError] = useState("");
    const [fullNameError, setFullNameError] = useState("");

    useEffect(() => {
        if (user) {
            navigate("/dashboard");
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
    }, [user, navigate]);

    const validateEmail = (email: string): boolean => {
        const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
        const isValid = emailRegex.test(email);
        setEmailError(isValid ? "" : "Please enter a valid email address");
        return isValid;
    };

    const validatePassword = (password: string): boolean => {
        const isValid = password.length >= 8;
        setPasswordError(isValid ? "" : "Password must be at least 8 characters long");
        return isValid;
    };

    const validateFullName = (name: string): boolean => {
        const isValid = name.trim().length > 0;
        setFullNameError(isValid ? "" : "Please enter your full name");
        return isValid;
    };

    const handleLogin = async (e: React.FormEvent) => {
        e.preventDefault();
        setError("");

        // Validate form
        if (!validateEmail(email) || !validatePassword(password)) {
            return;
        }

        setIsLoading(true);

        try {
            const { data } = await api.post("/v1/auth/login",
                { email, password },
                {
                    headers: {
                        'X-CSRF-TOKEN': csrfToken
                    }
                }
            );

            if (data.success) {
                login(data);
                navigate("/dashboard");
            } else {
                setError(data.message || "Login failed. Please try again.");
            }
        } catch (err: any) {
            if (err.response?.data?.message) {
                setError(err.response.data.message);
            } else if (err.response?.status === 401) {
                setError("Invalid email or password. Please try again.");
            } else {
                setError("An error occurred during login. Please try again later.");
            }
        } finally {
            setIsLoading(false);
        }
    };

    const handleRegister = async (e: React.FormEvent) => {
        e.preventDefault();
        setError("");

        // Validate form
        if (!validateEmail(email) || !validatePassword(password) || !validateFullName(fullName)) {
            return;
        }

        setIsLoading(true);

        try {
            const { data } = await api.post("/v1/auth/register",
                { fullName, email, password },
                {
                    headers: {
                        'X-CSRF-TOKEN': csrfToken
                    }
                }
            );

            if (data.success) {
                setIsLogin(true);
                setError("");
                setPassword("");
                // Show success message instead of error
                setIsLogin(true);
                // Use a more friendly success message
                alert("Registration successful! Please check your email to verify your account, then log in.");
            } else {
                setError(data.message || "Registration failed. Please try again.");
            }
        } catch (err: any) {
            if (err.response?.data?.validationErrors) {
                // Handle validation errors from the server
                const validationErrors = err.response.data.validationErrors;
                if (validationErrors.Email) {
                    setEmailError(validationErrors.Email[0]);
                }
                if (validationErrors.Password) {
                    setPasswordError(validationErrors.Password[0]);
                }
                if (validationErrors.FullName) {
                    setFullNameError(validationErrors.FullName[0]);
                }
                setError("Please correct the errors below.");
            } else if (err.response?.data?.message) {
                setError(err.response.data.message);
            } else {
                setError("An error occurred during registration. Please try again later.");
            }
        } finally {
            setIsLoading(false);
        }
    };

    return (
        <div className="min-h-screen flex items-center justify-center bg-gradient-to-br from-gray-900 to-gray-800">
            <div className="bg-white p-8 rounded-lg shadow-2xl w-full max-w-md">
                <h1 className="text-2xl font-bold text-center mb-6">
                    {isLogin ? "Sign In to Your Account" : "Create Your Account"}
                </h1>

                {/* Tabs */}
                <div className="flex mb-6">
                    <button
                        className={`flex-1 py-2 text-lg font-medium transition-colors duration-200 ${isLogin
                                ? "border-b-2 border-blue-600 text-blue-600"
                                : "text-gray-500 hover:text-gray-700"
                            }`}
                        onClick={() => setIsLogin(true)}
                    >
                        Login
                    </button>
                    <button
                        className={`flex-1 py-2 text-lg font-medium transition-colors duration-200 ${!isLogin
                                ? "border-b-2 border-blue-600 text-blue-600"
                                : "text-gray-500 hover:text-gray-700"
                            }`}
                        onClick={() => setIsLogin(false)}
                    >
                        Sign Up
                    </button>
                </div>

                {/* Error Alert */}
                {error && (
                    <div className="bg-red-100 border border-red-400 text-red-700 px-4 py-3 rounded mb-4">
                        <span>{error}</span>
                    </div>
                )}

                {/* Form */}
                <form onSubmit={isLogin ? handleLogin : handleRegister} className="space-y-6">
                    {!isLogin && (
                        <div>
                            <label className="block text-gray-700 font-medium mb-1">Full Name</label>
                            <input
                                type="text"
                                value={fullName}
                                onChange={(e) => {
                                    setFullName(e.target.value);
                                    if (fullNameError) validateFullName(e.target.value);
                                }}
                                onBlur={() => validateFullName(fullName)}
                                className={`w-full p-3 border rounded-lg focus:ring-2 focus:ring-blue-500 focus:outline-none transition ${fullNameError ? "border-red-500" : "border-gray-300"
                                    }`}
                                required
                            />
                            {fullNameError && <p className="text-red-500 text-sm mt-1">{fullNameError}</p>}
                        </div>
                    )}

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
                            className={`w-full p-3 border rounded-lg focus:ring-2 focus:ring-blue-500 focus:outline-none transition ${emailError ? "border-red-500" : "border-gray-300"
                                }`}
                            required
                        />
                        {emailError && <p className="text-red-500 text-sm mt-1">{emailError}</p>}
                    </div>

                    <div>
                        <label className="block text-gray-700 font-medium mb-1">Password</label>
                        <div className="relative">
                            <input
                                type={showPassword ? "text" : "password"}
                                value={password}
                                onChange={(e) => {
                                    setPassword(e.target.value);
                                    if (passwordError) validatePassword(e.target.value);
                                }}
                                onBlur={() => validatePassword(password)}
                                className={`w-full p-3 border rounded-lg focus:ring-2 focus:ring-blue-500 focus:outline-none pr-10 transition ${passwordError ? "border-red-500" : "border-gray-300"
                                    }`}
                                required
                            />
                            <button
                                type="button"
                                className="absolute inset-y-0 right-0 pr-3 flex items-center text-gray-600"
                                onClick={() => setShowPassword(!showPassword)}
                            >
                                {showPassword ? (
                                    <svg xmlns="http://www.w3.org/2000/svg" className="h-5 w-5" viewBox="0 0 20 20" fill="currentColor">
                                        <path d="M10 12a2 2 0 100-4 2 2 0 000 4z" />
                                        <path fillRule="evenodd" d="M.458 10C1.732 5.943 5.522 3 10 3s8.268 2.943 9.542 7c-1.274 4.057-5.064 7-9.542 7S1.732 14.057.458 10zM14 10a4 4 0 11-8 0 4 4 0 018 0z" clipRule="evenodd" />
                                    </svg>
                                ) : (
                                    <svg xmlns="http://www.w3.org/2000/svg" className="h-5 w-5" viewBox="0 0 20 20" fill="currentColor">
                                        <path fillRule="evenodd" d="M3.707 2.293a1 1 0 00-1.414 1.414l14 14a1 1 0 001.414-1.414l-1.473-1.473A10.014 10.014 0 0019.542 10C18.268 5.943 14.478 3 10 3a9.958 9.958 0 00-4.512 1.074l-1.78-1.781zm4.261 4.26l1.514 1.515a2.003 2.003 0 012.45 2.45l1.514 1.514a4 4 0 00-5.478-5.478z" clipRule="evenodd" />
                                        <path d="M12.454 16.697L9.75 13.992a4 4 0 01-3.742-3.741L2.335 6.578A9.98 9.98 0 00.458 10c1.274 4.057 5.065 7 9.542 7 .847 0 1.669-.105 2.454-.303z" />
                                    </svg>
                                )}
                            </button>
                        </div>
                        {passwordError && <p className="text-red-500 text-sm mt-1">{passwordError}</p>}

                        {!isLogin && (
                            <div className="mt-2 text-xs text-gray-500">
                                Password must be at least 8 characters long and contain a mix of letters, numbers, and special characters.
                            </div>
                        )}

                        {isLogin && (
                            <div className="mt-2 text-right">
                                <a href="#" className="text-sm text-blue-600 hover:text-blue-800">Forgot password?</a>
                            </div>
                        )}
                    </div>

                    <button
                        type="submit"
                        className="w-full bg-blue-600 hover:bg-blue-700 text-white py-3 rounded-lg transition-colors duration-200 font-medium flex justify-center items-center"
                        disabled={isLoading}
                    >
                        {isLoading ? (
                            <>
                                <svg className="animate-spin -ml-1 mr-3 h-5 w-5 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                                    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                                    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                                </svg>
                                {isLogin ? "Signing in..." : "Creating account..."}
                            </>
                        ) : (
                            isLogin ? "Sign In" : "Create Account"
                        )}
                    </button>
                </form>

                <div className="mt-6 text-center text-sm text-gray-600">
                    {isLogin ? (
                        <>Don't have an account? <button onClick={() => setIsLogin(false)} className="text-blue-600 hover:text-blue-800 font-medium">Sign up now</button></>
                    ) : (
                        <>Already have an account? <button onClick={() => setIsLogin(true)} className="text-blue-600 hover:text-blue-800 font-medium">Sign in</button></>
                    )}
                </div>
            </div>
        </div>
    );
};

export default AuthPage;