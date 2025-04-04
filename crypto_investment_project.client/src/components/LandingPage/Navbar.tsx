// src/components/LandingPage/Navbar.tsx
import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import Button from './Button';

interface NavbarProps {
    transparent?: boolean;
}

const Navbar: React.FC<NavbarProps> = ({ transparent = true }) => {
    const navigate = useNavigate();
    const { isAuthenticated } = useAuth();
    const [isScrolled, setIsScrolled] = useState(false);
    const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);

    // Track scroll position to change navbar style
    useEffect(() => {
        const handleScroll = () => {
            setIsScrolled(window.scrollY > 20);
        };

        window.addEventListener('scroll', handleScroll);
        return () => window.removeEventListener('scroll', handleScroll);
    }, []);

    // Handle navigation
    const navigateTo = (path: string) => {
        setIsMobileMenuOpen(false);
        navigate(path);
    };

    // Base navbar styles
    const navbarClasses = `fixed w-full z-50 top-0 transition-all duration-300 ${isScrolled
            ? 'py-2 bg-white shadow-md text-gray-900'
            : transparent
                ? 'py-4 bg-transparent text-white'
                : 'py-4 bg-white text-gray-900'
        }`;

    return (
        <>
            <nav className={navbarClasses}>
                <div className="container mx-auto px-4 flex justify-between items-center">
                    {/* Logo */}
                    <div
                        className="flex items-center cursor-pointer"
                        onClick={() => navigateTo('/')}
                    >
                        <div className={`text-2xl font-bold flex items-center ${isScrolled ? 'text-blue-600' : transparent ? 'text-white' : 'text-blue-600'
                            }`}>
                            <svg
                                className="w-8 h-8 mr-2"
                                viewBox="0 0 24 24"
                                fill="none"
                                xmlns="http://www.w3.org/2000/svg"
                            >
                                <path
                                    d="M12 22C17.5228 22 22 17.5228 22 12C22 6.47715 17.5228 2 12 2C6.47715 2 2 6.47715 2 12C2 17.5228 6.47715 22 12 22Z"
                                    stroke="currentColor"
                                    strokeWidth="2"
                                    strokeLinecap="round"
                                    strokeLinejoin="round"
                                />
                                <path
                                    d="M9.5 9C9.5 8.17157 10.1716 7.5 11 7.5H13C13.8284 7.5 14.5 8.17157 14.5 9C14.5 9.82843 13.8284 10.5 13 10.5H11C10.1716 10.5 9.5 11.1716 9.5 12C9.5 12.8284 10.1716 13.5 11 13.5H13C13.8284 13.5 14.5 14.1716 14.5 15C14.5 15.8284 13.8284 16.5 13 16.5H11C10.1716 16.5 9.5 15.8284 9.5 15"
                                    stroke="currentColor"
                                    strokeWidth="2"
                                    strokeLinecap="round"
                                    strokeLinejoin="round"
                                />
                                <path
                                    d="M12 7.5V6"
                                    stroke="currentColor"
                                    strokeWidth="2"
                                    strokeLinecap="round"
                                    strokeLinejoin="round"
                                />
                                <path
                                    d="M12 18V16.5"
                                    stroke="currentColor"
                                    strokeWidth="2"
                                    strokeLinecap="round"
                                    strokeLinejoin="round"
                                />
                            </svg>
                            CryptoInvest
                        </div>
                    </div>

                    {/* Desktop menu */}
                    <div className="hidden md:flex items-center space-x-10">
                        <div className="flex space-x-8">
                            {[
                                { label: 'Features', path: '/features' },
                                { label: 'Pricing', path: '/pricing' },
                                { label: 'Learn', path: '/learn' },
                                { label: 'Blog', path: '/blog' }
                            ].map((item) => (
                                <button
                                    key={item.path}
                                    onClick={() => navigateTo(item.path)}
                                    className={`font-medium hover:text-blue-500 transition-colors ${isScrolled ? 'text-gray-700' : transparent ? 'text-gray-100 hover:text-white' : 'text-gray-700'
                                        }`}
                                >
                                    {item.label}
                                </button>
                            ))}
                        </div>

                        <div className="flex items-center space-x-3">
                            {isAuthenticated ? (
                                <Button
                                    variant="primary"
                                    size="md"
                                    onClick={() => navigateTo('/dashboard')}
                                >
                                    Dashboard
                                </Button>
                            ) : (
                                <>
                                    <button
                                        onClick={() => navigateTo('/auth/login')}
                                        className={`font-medium hover:text-blue-500 transition-colors ${isScrolled ? 'text-gray-700' : transparent ? 'text-gray-100 hover:text-white' : 'text-gray-700'
                                            }`}
                                    >
                                        Log In
                                    </button>
                                    <Button
                                        variant="primary"
                                        size="md"
                                        onClick={() => navigateTo('/auth/register')}
                                    >
                                        Sign Up
                                    </Button>
                                </>
                            )}
                        </div>
                    </div>

                    {/* Mobile menu button */}
                    <button
                        className="md:hidden flex items-center"
                        onClick={() => setIsMobileMenuOpen(!isMobileMenuOpen)}
                        aria-label="Toggle menu"
                    >
                        <div className={`hamburger-btn ${isMobileMenuOpen ? 'open' : ''}`}>
                            <span className={`${isScrolled ? 'bg-gray-900' : transparent ? 'bg-white' : 'bg-gray-900'}`}></span>
                            <span className={`${isScrolled ? 'bg-gray-900' : transparent ? 'bg-white' : 'bg-gray-900'}`}></span>
                            <span className={`${isScrolled ? 'bg-gray-900' : transparent ? 'bg-white' : 'bg-gray-900'}`}></span>
                        </div>
                    </button>
                </div>
            </nav>

            {/* Mobile menu */}
            <div className={`mobile-menu ${isMobileMenuOpen ? 'open' : ''}`}>
                <div className="py-6 px-4">
                    <div className="flex justify-between items-center mb-6">
                        <div className="text-2xl font-bold text-white flex items-center">
                            <svg
                                className="w-8 h-8 mr-2"
                                viewBox="0 0 24 24"
                                fill="none"
                                xmlns="http://www.w3.org/2000/svg"
                            >
                                <path
                                    d="M12 22C17.5228 22 22 17.5228 22 12C22 6.47715 17.5228 2 12 2C6.47715 2 2 6.47715 2 12C2 17.5228 6.47715 22 12 22Z"
                                    stroke="currentColor"
                                    strokeWidth="2"
                                    strokeLinecap="round"
                                    strokeLinejoin="round"
                                />
                                <path
                                    d="M9.5 9C9.5 8.17157 10.1716 7.5 11 7.5H13C13.8284 7.5 14.5 8.17157 14.5 9C14.5 9.82843 13.8284 10.5 13 10.5H11C10.1716 10.5 9.5 11.1716 9.5 12C9.5 12.8284 10.1716 13.5 11 13.5H13C13.8284 13.5 14.5 14.1716 14.5 15C14.5 15.8284 13.8284 16.5 13 16.5H11C10.1716 16.5 9.5 15.8284 9.5 15"
                                    stroke="currentColor"
                                    strokeWidth="2"
                                    strokeLinecap="round"
                                    strokeLinejoin="round"
                                />
                                <path
                                    d="M12 7.5V6"
                                    stroke="currentColor"
                                    strokeWidth="2"
                                    strokeLinecap="round"
                                    strokeLinejoin="round"
                                />
                                <path
                                    d="M12 18V16.5"
                                    stroke="currentColor"
                                    strokeWidth="2"
                                    strokeLinecap="round"
                                    strokeLinejoin="round"
                                />
                            </svg>
                            CryptoInvest
                        </div>
                        <button
                            onClick={() => setIsMobileMenuOpen(false)}
                            className="text-white"
                            aria-label="Close menu"
                        >
                            <svg
                                className="w-6 h-6"
                                fill="none"
                                stroke="currentColor"
                                viewBox="0 0 24 24"
                            >
                                <path
                                    strokeLinecap="round"
                                    strokeLinejoin="round"
                                    strokeWidth="2"
                                    d="M6 18L18 6M6 6l12 12"
                                />
                            </svg>
                        </button>
                    </div>

                    <div className="flex flex-col space-y-4 mb-8">
                        {[
                            { label: 'Features', path: '/features' },
                            { label: 'Pricing', path: '/pricing' },
                            { label: 'Learn', path: '/learn' },
                            { label: 'Blog', path: '/blog' }
                        ].map((item) => (
                            <button
                                key={item.path}
                                onClick={() => navigateTo(item.path)}
                                className="text-white text-lg py-2 hover:bg-gray-800 px-3 rounded-lg transition-colors text-left"
                            >
                                {item.label}
                            </button>
                        ))}
                    </div>

                    <div className="flex flex-col space-y-3">
                        {isAuthenticated ? (
                            <Button
                                variant="primary"
                                size="lg"
                                isFullWidth
                                onClick={() => navigateTo('/dashboard')}
                            >
                                Dashboard
                            </Button>
                        ) : (
                            <>
                                <Button
                                    variant="primary"
                                    size="lg"
                                    isFullWidth
                                    onClick={() => navigateTo('/auth/register')}
                                >
                                    Sign Up
                                </Button>
                                <Button
                                    variant="outline"
                                    size="lg"
                                    isFullWidth
                                    onClick={() => navigateTo('/auth/login')}
                                    className="border-white text-white hover:bg-white hover:text-gray-900"
                                >
                                    Log In
                                </Button>
                            </>
                        )}
                    </div>
                </div>
            </div>

            {/* Mobile menu overlay */}
            <div
                className={`mobile-menu-overlay ${isMobileMenuOpen ? 'open' : ''}`}
                onClick={() => setIsMobileMenuOpen(false)}
            ></div>
        </>
    );
};

export default Navbar;