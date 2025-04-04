import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';

const Navbar = () => {
    const navigate = useNavigate();
    const { isAuthenticated, user, logout } = useAuth();
    const [isMenuOpen, setIsMenuOpen] = useState(false);
    const [scrolled, setScrolled] = useState(false);
    const [isDropdownOpen, setIsDropdownOpen] = useState(false);

    // Track scroll position for navbar appearance
    useEffect(() => {
        const handleScroll = () => {
            const isScrolled = window.scrollY > 20;
            if (isScrolled !== scrolled) {
                setScrolled(isScrolled);
            }
        };

        window.addEventListener('scroll', handleScroll);

        return () => {
            window.removeEventListener('scroll', handleScroll);
        };
    }, [scrolled]);

    // Close menu when navigating
    const handleNavigation = (path) => {
        setIsMenuOpen(false);
        navigate(path);
    };

    // Handle logout
    const handleLogout = async () => {
        setIsMenuOpen(false);
        setIsDropdownOpen(false);
        await logout();
        navigate('/');
    };

    // Handle click outside to close dropdown
    useEffect(() => {
        const handleClickOutside = (event) => {
            if (isDropdownOpen && !event.target.closest('.user-dropdown')) {
                setIsDropdownOpen(false);
            }
        };

        document.addEventListener('mousedown', handleClickOutside);
        return () => {
            document.removeEventListener('mousedown', handleClickOutside);
        };
    }, [isDropdownOpen]);

    return (
        <>
            <header className={`sticky-header ${scrolled ? 'scrolled' : ''} dark-header py-4 text-white`}>
                <div className="container mx-auto px-4">
                    <div className="flex justify-between items-center">
                        {/* Logo */}
                        <div
                            className="flex items-center cursor-pointer"
                            onClick={() => handleNavigation('/')}
                        >
                            <div className="w-10 h-10 rounded-lg bg-gradient-to-r from-blue-500 to-indigo-600 flex items-center justify-center mr-3">
                                <svg className="w-6 h-6 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M13 7h8m0 0v8m0-8l-8 8-4-4-6 6"></path>
                                </svg>
                            </div>
                            <span className="text-xl font-bold">CryptoInvest</span>
                        </div>

                        {/* Desktop Navigation */}
                        <nav className="hidden md:flex items-center space-x-6">
                            <a
                                href="#"
                                onClick={(e) => { e.preventDefault(); handleNavigation('/'); }}
                                className="text-white hover:text-blue-300 transition-colors"
                            >
                                Home
                            </a>
                            <a
                                href="#"
                                onClick={(e) => { e.preventDefault(); handleNavigation('/features'); }}
                                className="text-white hover:text-blue-300 transition-colors"
                            >
                                Features
                            </a>
                            <a
                                href="#"
                                onClick={(e) => { e.preventDefault(); handleNavigation('/pricing'); }}
                                className="text-white hover:text-blue-300 transition-colors"
                            >
                                Pricing
                            </a>
                            <a
                                href="#"
                                onClick={(e) => { e.preventDefault(); handleNavigation('/learn'); }}
                                className="text-white hover:text-blue-300 transition-colors"
                            >
                                Learning Center
                            </a>
                            <a
                                href="#"
                                onClick={(e) => { e.preventDefault(); handleNavigation('/about'); }}
                                className="text-white hover:text-blue-300 transition-colors"
                            >
                                About Us
                            </a>
                        </nav>

                        {/* Authentication buttons for desktop */}
                        <div className="hidden md:flex items-center space-x-4">
                            {isAuthenticated ? (
                                <div className="relative user-dropdown">
                                    <button
                                        onClick={() => setIsDropdownOpen(!isDropdownOpen)}
                                        className="flex items-center space-x-2 bg-white bg-opacity-10 rounded-lg px-4 py-2 hover:bg-opacity-20 transition-all"
                                    >
                                        <div className="w-8 h-8 rounded-full bg-blue-600 flex items-center justify-center text-white">
                                            {user?.fullname?.charAt(0) || 'U'}
                                        </div>
                                        <span>{user?.fullname || 'User'}</span>
                                        <svg className={`w-4 h-4 transition-transform ${isDropdownOpen ? 'rotate-180' : ''}`} fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M19 9l-7 7-7-7" />
                                        </svg>
                                    </button>

                                    {/* User dropdown menu */}
                                    {isDropdownOpen && (
                                        <div className="absolute right-0 mt-2 w-48 bg-gray-900 rounded-lg shadow-xl py-2 z-50 animate-fade-in">
                                            <a
                                                href="#"
                                                onClick={(e) => { e.preventDefault(); handleNavigation('/dashboard'); }}
                                                className="block px-4 py-2 text-white hover:bg-gray-800 transition-colors"
                                            >
                                                Dashboard
                                            </a>
                                            <a
                                                href="#"
                                                onClick={(e) => { e.preventDefault(); handleNavigation('/profile'); }}
                                                className="block px-4 py-2 text-white hover:bg-gray-800 transition-colors"
                                            >
                                                Profile
                                            </a>
                                            <a
                                                href="#"
                                                onClick={(e) => { e.preventDefault(); handleNavigation('/subscriptions'); }}
                                                className="block px-4 py-2 text-white hover:bg-gray-800 transition-colors"
                                            >
                                                My Subscriptions
                                            </a>
                                            <hr className="my-2 border-gray-700" />
                                            <a
                                                href="#"
                                                onClick={(e) => { e.preventDefault(); handleLogout(); }}
                                                className="block px-4 py-2 text-red-400 hover:bg-gray-800 transition-colors"
                                            >
                                                Logout
                                            </a>
                                        </div>
                                    )}
                                </div>
                            ) : (
                                <>
                                    <button
                                        onClick={() => handleNavigation('/auth/login')}
                                        className="text-white hover:text-blue-300 transition-colors"
                                    >
                                        Login
                                    </button>
                                    <button
                                        onClick={() => handleNavigation('/auth/register')}
                                        className="bg-blue-600 hover:bg-blue-700 text-white px-4 py-2 rounded-lg transition-colors"
                                    >
                                        Get Started
                                    </button>
                                </>
                            )}
                        </div>

                        {/* Mobile menu button */}
                        <button
                            className="md:hidden hamburger-btn focus:outline-none text-white"
                            onClick={() => setIsMenuOpen(!isMenuOpen)}
                            aria-label="Toggle menu"
                        >
                            <span className={isMenuOpen ? 'rotate-45 translate-y-2.5' : ''}></span>
                            <span className={isMenuOpen ? 'opacity-0' : ''}></span>
                            <span className={isMenuOpen ? '-rotate-45 -translate-y-2.5' : ''}></span>
                        </button>
                    </div>
                </div>
            </header>

            {/* Mobile menu overlay */}
            <div
                className={`mobile-menu-overlay ${isMenuOpen ? 'open' : ''}`}
                onClick={() => setIsMenuOpen(false)}
            ></div>

            {/* Mobile menu */}
            <nav className={`mobile-menu ${isMenuOpen ? 'open' : ''} p-6`}>
                <div className="flex flex-col space-y-6">
                    <a
                        href="#"
                        onClick={(e) => { e.preventDefault(); handleNavigation('/'); }}
                        className="text-white text-lg hover:text-blue-300 transition-colors"
                    >
                        Home
                    </a>
                    <a
                        href="#"
                        onClick={(e) => { e.preventDefault(); handleNavigation('/features'); }}
                        className="text-white text-lg hover:text-blue-300 transition-colors"
                    >
                        Features
                    </a>
                    <a
                        href="#"
                        onClick={(e) => { e.preventDefault(); handleNavigation('/pricing'); }}
                        className="text-white text-lg hover:text-blue-300 transition-colors"
                    >
                        Pricing
                    </a>
                    <a
                        href="#"
                        onClick={(e) => { e.preventDefault(); handleNavigation('/learn'); }}
                        className="text-white text-lg hover:text-blue-300 transition-colors"
                    >
                        Learning Center
                    </a>
                    <a
                        href="#"
                        onClick={(e) => { e.preventDefault(); handleNavigation('/about'); }}
                        className="text-white text-lg hover:text-blue-300 transition-colors"
                    >
                        About Us
                    </a>

                    <div className="border-t border-gray-700 my-4 pt-4">
                        {isAuthenticated ? (
                            <>
                                <div className="flex items-center mb-6">
                                    <div className="w-10 h-10 rounded-full bg-blue-600 flex items-center justify-center text-white mr-3">
                                        {user?.fullname?.charAt(0) || 'U'}
                                    </div>
                                    <div>
                                        <p className="text-white">{user?.fullname || 'User'}</p>
                                        <p className="text-gray-400 text-sm">{user?.email || 'user@example.com'}</p>
                                    </div>
                                </div>

                                <div className="space-y-4">
                                    <a
                                        href="#"
                                        onClick={(e) => { e.preventDefault(); handleNavigation('/dashboard'); }}
                                        className="flex items-center text-white hover:text-blue-300 transition-colors"
                                    >
                                        <svg className="w-5 h-5 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M16 8v8m-4-5v5m-4-2v2m-2 4h12a2 2 0 002-2V6a2 2 0 00-2-2H6a2 2 0 00-2 2v12a2 2 0 002 2z" />
                                        </svg>
                                        Dashboard
                                    </a>
                                    <a
                                        href="#"
                                        onClick={(e) => { e.preventDefault(); handleNavigation('/profile'); }}
                                        className="flex items-center text-white hover:text-blue-300 transition-colors"
                                    >
                                        <svg className="w-5 h-5 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" />
                                        </svg>
                                        Profile
                                    </a>
                                    <a
                                        href="#"
                                        onClick={(e) => { e.preventDefault(); handleNavigation('/subscriptions'); }}
                                        className="flex items-center text-white hover:text-blue-300 transition-colors"
                                    >
                                        <svg className="w-5 h-5 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
                                        </svg>
                                        My Subscriptions
                                    </a>
                                    <a
                                        href="#"
                                        onClick={(e) => { e.preventDefault(); handleLogout(); }}
                                        className="flex items-center text-red-400 hover:text-red-300 transition-colors"
                                    >
                                        <svg className="w-5 h-5 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M17 16l4-4m0 0l-4-4m4 4H7m6 4v1a3 3 0 01-3 3H6a3 3 0 01-3-3V7a3 3 0 013-3h4a3 3 0 013 3v1" />
                                        </svg>
                                        Logout
                                    </a>
                                </div>
                            </>
                        ) : (
                            <div className="flex flex-col space-y-4">
                                <button
                                    onClick={() => handleNavigation('/auth/login')}
                                    className="w-full bg-transparent border border-blue-500 text-blue-500 py-3 rounded-lg hover:bg-blue-500 hover:text-white transition-colors"
                                >
                                    Login
                                </button>
                                <button
                                    onClick={() => handleNavigation('/auth/register')}
                                    className="w-full bg-blue-600 text-white py-3 rounded-lg hover:bg-blue-700 transition-colors"
                                >
                                    Get Started
                                </button>
                            </div>
                        )}
                    </div>

                    {/* Social media links */}
                    <div className="mt-auto border-t border-gray-700 pt-6">
                        <div className="flex justify-between">
                            <a href="#" className="text-gray-400 hover:text-white transition-colors">
                                <svg className="w-6 h-6" fill="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                                    <path fillRule="evenodd" d="M22 12c0-5.523-4.477-10-10-10S2 6.477 2 12c0 4.991 3.657 9.128 8.438 9.878v-6.987h-2.54V12h2.54V9.797c0-2.506 1.492-3.89 3.777-3.89 1.094 0 2.238.195 2.238.195v2.46h-1.26c-1.243 0-1.63.771-1.63 1.562V12h2.773l-.443 2.89h-2.33v6.988C18.343 21.128 22 16.991 22 12z" clipRule="evenodd" />
                                </svg>
                            </a>
                            <a href="#" className="text-gray-400 hover:text-white transition-colors">
                                <svg className="w-6 h-6" fill="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                                    <path d="M8.29 20.251c7.547 0 11.675-6.253 11.675-11.675 0-.178 0-.355-.012-.53A8.348 8.348 0 0022 5.92a8.19 8.19 0 01-2.357.646 4.118 4.118 0 001.804-2.27 8.224 8.224 0 01-2.605.996 4.107 4.107 0 00-6.993 3.743 11.65 11.65 0 01-8.457-4.287 4.106 4.106 0 001.27 5.477A4.072 4.072 0 012.8 9.713v.052a4.105 4.105 0 003.292 4.022 4.095 4.095 0 01-1.853.07 4.108 4.108 0 003.834 2.85A8.233 8.233 0 012 18.407a11.616 11.616 0 006.29 1.84" />
                                </svg>
                            </a>
                            <a href="#" className="text-gray-400 hover:text-white transition-colors">
                                <svg className="w-6 h-6" fill="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                                    <path fillRule="evenodd" d="M12.315 2c2.43 0 2.784.013 3.808.06 1.064.049 1.791.218 2.427.465a4.902 4.902 0 011.772 1.153 4.902 4.902 0 011.153 1.772c.247.636.416 1.363.465 2.427.048 1.067.06 1.407.06 4.123v.08c0 2.643-.012 2.987-.06 4.043-.049 1.064-.218 1.791-.465 2.427a4.902 4.902 0 01-1.153 1.772 4.902 4.902 0 01-1.772 1.153c-.636.247-1.363.416-2.427.465-1.067.048-1.407.06-4.123.06h-.08c-2.643 0-2.987-.012-4.043-.06-1.064-.049-1.791-.218-2.427-.465a4.902 4.902 0 01-1.772-1.153 4.902 4.902 0 01-1.153-1.772c-.247-.636-.416-1.363-.465-2.427-.047-1.024-.06-1.379-.06-3.808v-.63c0-2.43.013-2.784.06-3.808.049-1.064.218-1.791.465-2.427a4.902 4.902 0 011.153-1.772A4.902 4.902 0 015.45 2.525c.636-.247 1.363-.416 2.427-.465C8.901 2.013 9.256 2 11.685 2h.63zm-.081 1.802h-.468c-2.456 0-2.784.011-3.807.058-.975.045-1.504.207-1.857.344-.467.182-.8.398-1.15.748-.35.35-.566.683-.748 1.15-.137.353-.3.882-.344 1.857-.047 1.023-.058 1.351-.058 3.807v.468c0 2.456.011 2.784.058 3.807.045.975.207 1.504.344 1.857.182.466.399.8.748 1.15.35.35.683.566 1.15.748.353.137.882.3 1.857.344 1.054.048 1.37.058 4.041.058h.08c2.597 0 2.917-.01 3.96-.058.976-.045 1.505-.207 1.858-.344.466-.182.8-.398 1.15-.748.35-.35.566-.683.748-1.15.137-.353.3-.882.344-1.857.048-1.055.058-1.37.058-4.041v-.08c0-2.597-.01-2.917-.058-3.96-.045-.976-.207-1.505-.344-1.858a3.097 3.097 0 00-.748-1.15 3.098 3.098 0 00-1.15-.748c-.353-.137-.882-.3-1.857-.344-1.023-.047-1.351-.058-3.807-.058zM12 6.865a5.135 5.135 0 110 10.27 5.135 5.135 0 010-10.27zm0 1.802a3.333 3.333 0 100 6.666 3.333 3.333 0 000-6.666zm5.338-3.205a1.2 1.2 0 110 2.4 1.2 1.2 0 010-2.4z" clipRule="evenodd" />
                                </svg>
                            </a>
                            <a href="#" className="text-gray-400 hover:text-white transition-colors">
                                <svg className="w-6 h-6" fill="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                                    <path fillRule="evenodd" d="M12 2C6.477 2 2 6.484 2 12.017c0 4.425 2.865 8.18 6.839 9.504.5.092.682-.217.682-.483 0-.237-.008-.868-.013-1.703-2.782.605-3.369-1.343-3.369-1.343-.454-1.158-1.11-1.466-1.11-1.466-.908-.62.069-.608.069-.608 1.003.07 1.531 1.032 1.531 1.032.892 1.53 2.341 1.088 2.91.832.092-.647.35-1.088.636-1.338-2.22-.253-4.555-1.113-4.555-4.951 0-1.093.39-1.988 1.029-2.688-.103-.253-.446-1.272.098-2.65 0 0 .84-.27 2.75 1.026A9.564 9.564 0 0112 6.844c.85.004 1.705.115 2.504.337 1.909-1.296 2.747-1.027 2.747-1.027.546 1.379.202 2.398.1 2.651.64.7 1.028 1.595 1.028 2.688 0 3.848-2.339 4.695-4.566 4.943.359.309.678.92.678 1.855 0 1.338-.012 2.419-.012 2.747 0 .268.18.58.688.482A10.019 10.019 0 0022 12.017C22 6.484 17.522 2 12 2z" clipRule="evenodd" />
                                </svg>
                            </a>
                        </div>
                        <p className="mt-4 text-center text-gray-400 text-sm">
                            © {new Date().getFullYear()} CryptoInvest
                        </p>
                    </div>
                </div>
            </nav>
        </>
    );
};

export default Navbar;