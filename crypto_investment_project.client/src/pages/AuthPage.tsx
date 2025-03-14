import { useState } from "react";
import { useNavigate } from "react-router-dom";
import api from "../services/api";

const AuthPage = () => {
    const [isLogin, setIsLogin] = useState(true);
    const [email, setEmail] = useState("");
    const [password, setPassword] = useState("");
    const [fullname, setFullname] = useState(""); // For registration
    const [error, setError] = useState("");
    const navigate = useNavigate();

    const handleLogin = async (e: React.FormEvent) => {
        e.preventDefault();
        try {
            const url = "v1/authenticate/login";
            const payload = { email, password };
            const response = await api.post(url, payload);
            const { token } = response.data; // Assuming LoginResponse returns a token
            localStorage.setItem("token", token); // Store token for authenticated requests
            navigate("/dashboard"); // Redirect to a dashboard (to be implemented)
        } catch (err) {
            setError("Login failed. Check your credentials.");
        }
    };

    const handleRegister = async (e: React.FormEvent) => {
        e.preventDefault();
        try {
            const url = "v1/authenticate/register";
            const payload = { fullname, email, password };
            const response = await api.post(url, payload);

            if (response.data.success) {
                setIsLogin(true); // Switch to login after successful registration
                setError("Registration successful! Please log in.");
            }
        } catch (err) {
            setError("Registration failed. Try again.");
        }
    };

    return (
        <div className="min-h-screen bg-gray-900 flex items-center justify-center">
            <div className="bg-white p-8 rounded-lg shadow-lg w-full max-w-md">
                {/* Tabs */}
                <div className="flex mb-6">
                    <button
                        className={`flex-1 py-2 text-lg font-semibold ${isLogin ? "border-b-2 border-blue-600" : "text-gray-500"}`}
                        onClick={() => setIsLogin(true)}
                    >
                        Login
                    </button>
                    <button
                        className={`flex-1 py-2 text-lg font-semibold ${!isLogin ? "border-b-2 border-blue-600" : "text-gray-500"}`}
                        onClick={() => setIsLogin(false)}
                    >
                        Sign Up
                    </button>
                </div>

                {/* Form */}
                <form onSubmit={isLogin ? handleLogin : handleRegister}>
                    {!isLogin && (
                        <div className="mb-4">
                            <label className="block text-gray-700">Full Name</label>
                            <input
                                type="text"
                                value={fullname}
                                onChange={(e) => setFullname(e.target.value)}
                                className="w-full p-2 border rounded-lg"
                                required
                            />
                        </div>
                    )}
                    <div className="mb-4">
                        <label className="block text-gray-700">Email</label>
                        <input
                            type="email"
                            value={email}
                            onChange={(e) => setEmail(e.target.value)}
                            className="w-full p-2 border rounded-lg"
                            required
                        />
                    </div>
                    <div className="mb-6">
                        <label className="block text-gray-700">Password</label>
                        <input
                            type="password"
                            value={password}
                            onChange={(e) => setPassword(e.target.value)}
                            className="w-full p-2 border rounded-lg"
                            required
                        />
                    </div>
                    {error && <p className="text-red-500 mb-4">{error}</p>}
                    <button
                        type="submit"
                        className="w-full bg-blue-600 hover:bg-blue-700 text-white py-2 rounded-lg transition"
                    >
                        {isLogin ? "Login" : "Sign Up"}
                    </button>
                </form>
            </div>
        </div>
    );
};

export default AuthPage;