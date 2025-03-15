// src/components/LandingPage.tsx
import React from "react";
import { Link } from "react-router-dom";


const LandingPage: React.FC = () => {
    return (
        <div className="min-h-screen bg-gradient-to-br from-gray-900 via-blue-900 to-black text-white flex flex-col justify-center items-center">
            {/* Header */}
            <header className="absolute top-0 w-full p-6 flex justify-between items-center">
                <h1 className="text-2xl font-bold">Stackfi</h1>
                <Link to="/auth" className="text-blue-400 hover:text-blue-300">
                    Login / Sign Up
                </Link>
            </header>

            {/* Main Content */}
            <main className="text-center px-4">
                <h2 className="text-5xl font-extrabold mb-4">
                    Grow Your Wealth with Crypto
                </h2>
                <p className="text-lg mb-8 max-w-md mx-auto">
                    Subscribe to tailored investment plans and watch your crypto portfolio thrive. Start today with daily, weekly, or monthly investments.
                </p>
                <Link
                    to="/auth"
                    className="bg-blue-600 hover:bg-blue-700 text-white font-semibold py-3 px-6 rounded-lg shadow-lg transition duration-300"
                >
                    Get Started
                </Link>
            </main>

            {/* Footer */}
            <footer className="absolute bottom-0 w-full p-4 text-center text-gray-400">
                © 2025 Stackfi. All rights reserved.
            </footer>
        </div>
    );
};

export default LandingPage;