// src/components/AuthPage.tsx
import React, { useState } from "react";
import { useNavigate } from "react-router-dom";
import axios from "axios";

const AuthPage: React.FC = () => {
    const [isLogin, setIsLogin] = useState(true);
    const [email, setEmail] = useState("");
    const [password, setPassword] = useState("");
    const [error, setError] = useState("");
    const navigate = useNavigate();

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        setError("");

        const payload = { email, password };
        const url = isLogin
            ? "http://localhost:5000/api/v1/authenticate/login"
            : "http://localhost:5000/api/v1/authenticate/register"; // Adjust port as needed

        try {
            const response = await axios.post(url, payload);
            if (isLogin) {
                // Assuming LoginResponse has a token
                localStorage.setItem("token", response.data.token);
                navigate("/dashboard"); // Redirect to a dashboard (to be built later)
            } else {
                setError("Registration successful! Please check your email to confirm.");
                setIsLogin(true); // Switch to login after signup
            }
        } catch (err: any) {
            setError(err.response?.data?.message || "An error occurred");
        }
    };

    return (
        <div className="min-h-screen bg-gray-900 flex items-center justify-center">
            <div className="bg-white p-8 rounded-lg shadow-lg w-full max-w-md">
                <h2 className="text-3xl font-bold text-center mb-6">
                    {isLogin ? "Login" : "Sign Up"}
                </h2>

                <form onSubmit={handleSubmit} className="space-y-6">
                    <div>
                        <label htmlFor="email" className="block text-sm font-medium text-gray-700">
                            Email
                        </label>
                        <input
                            id="email"
                            type="email"
                            value={email}
                            onChange={(e) => setEmail(e.target.value)}
                            className="mt-1 w-full p-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
                            required
                        />
                    </div>

                    <div>
                        <label htmlFor="password" className="block text-sm font-medium text-gray-700">
                            Password
                        </label>
                        <input
                            id="password"
                            type="password"
                            value={password}
                            onChange={(e) => setPassword(e.target.value)}
                            className="mt-1 w-full p-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
                            required
                        />
                    </div>

                    {error && <p className="text-red-500 text-sm">{error}</p>}

                    <button
                        type="submit"
                        className="w-full bg-blue-600 hover:bg-blue-700 text-white font-semibold py-2 rounded-md transition duration-300"
                    >
                        {isLogin ? "Login" : "Sign Up"}
                    </button>
                </form>

                <p className="mt-4 text-center text-sm text-gray-600">
                    {isLogin ? "Need an account?" : "Already have an account?"}
                    <button
                        onClick={() => setIsLogin(!isLogin)}
                        className="ml-1 text-blue-600 hover:underline"
                    >
                        {isLogin ? "Sign Up" : "Login"}
                    </button>
                </p>
            </div>
        </div>
    );
};

export default AuthPage;