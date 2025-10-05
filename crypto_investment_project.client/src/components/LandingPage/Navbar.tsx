// src/components/LandingPage/Navbar.tsx
import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import { LockOutlined, MenuOutlined, CloseOutlined } from '@ant-design/icons';

interface NavbarProps {
    transparent?: boolean;
}

/**
 * Professional Navbar for Financial Platform
 * 
 * Design principles:
 * - Minimal, clean design
 * - Consistent with landing page (Ant Design)
 * - Security-first messaging
 * - Simple navigation
 * - No unnecessary animations
 */
const Navbar: React.FC<NavbarProps> = ({ transparent = true }) => {
    const navigate = useNavigate();
    const { isAuthenticated } = useAuth();
    const [isScrolled, setIsScrolled] = useState(false);
    const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);

    // Track scroll for navbar background
    useEffect(() => {
        const handleScroll = () => {
            setIsScrolled(window.scrollY > 20);
        };

        handleScroll(); // Set initial state
        window.addEventListener('scroll', handleScroll);
        return () => window.removeEventListener('scroll', handleScroll);
    }, []);

    // Close mobile menu on ESC
    useEffect(() => {
        const handleEsc = (e: KeyboardEvent) => {
            if (e.key === 'Escape') setIsMobileMenuOpen(false);
        };
        if (isMobileMenuOpen) {
            document.addEventListener('keydown', handleEsc);
            return () => document.removeEventListener('keydown', handleEsc);
        }
    }, [isMobileMenuOpen]);

    const navigateTo = (path: string) => {
        setIsMobileMenuOpen(false);
        navigate(path);
    };

    // Determine navbar background
    const navStyle: React.CSSProperties = {
        position: 'fixed',
        top: 0,
        left: 0,
        right: 0,
        zIndex: 1000,
        transition: 'all 0.3s ease',
        backgroundColor: isScrolled ? '#ffffff' : (transparent ? 'transparent' : '#ffffff'),
        borderBottom: isScrolled ? '1px solid #e5e7eb' : (transparent ? 'none' : '1px solid #e5e7eb'),
        boxShadow: isScrolled ? '0 1px 3px rgba(0,0,0,0.05)' : 'none'
    };

    const textColor = isScrolled ? '#1a1a1a' : (transparent ? '#ffffff' : '#1a1a1a');
    const linkColor = isScrolled ? '#4a5568' : (transparent ? 'rgba(255,255,255,0.9)' : '#4a5568');

    return (
        <>
            <nav style={navStyle}>
                <div style={{
                    maxWidth: 1200,
                    margin: '0 auto',
                    padding: '16px 24px',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between'
                }}>
                    {/* Logo */}
                    <div
                        onClick={() => navigateTo('/')}
                        style={{
                            display: 'flex',
                            alignItems: 'center',
                            cursor: 'pointer',
                            gap: 12
                        }}
                    >
                        <LockOutlined style={{ fontSize: 24, color: '#2563eb' }} />
                        <span style={{
                            fontSize: '1.25rem',
                            fontWeight: 600,
                            color: textColor,
                            letterSpacing: '-0.01em'
                        }}>
                            CryptoInvest
                        </span>
                    </div>

                    {/* Desktop Navigation */}
                    <div style={{
                        display: 'none',
                        alignItems: 'center',
                        gap: 40
                    }}
                        className="desktop-nav">
                        <div style={{ display: 'flex', gap: 32 }}>
                            {[
                                { label: 'Security', path: '/security' },
                                { label: 'Pricing', path: '/pricing' },
                                { label: 'Learn', path: '/learn' }
                            ].map((item) => (
                                <button
                                    key={item.path}
                                    onClick={() => navigateTo(item.path)}
                                    style={{
                                        background: 'none',
                                        border: 'none',
                                        color: linkColor,
                                        fontSize: '0.9375rem',
                                        fontWeight: 500,
                                        cursor: 'pointer',
                                        padding: 0,
                                        transition: 'color 0.2s'
                                    }}
                                    onMouseEnter={(e) => e.currentTarget.style.color = '#2563eb'}
                                    onMouseLeave={(e) => e.currentTarget.style.color = linkColor}
                                >
                                    {item.label}
                                </button>
                            ))}
                        </div>

                        <div style={{ display: 'flex', gap: 12, alignItems: 'center' }}>
                            {isAuthenticated ? (
                                <button
                                    onClick={() => navigateTo('/dashboard')}
                                    style={{
                                        backgroundColor: '#2563eb',
                                        color: '#ffffff',
                                        border: 'none',
                                        padding: '10px 24px',
                                        borderRadius: 4,
                                        fontSize: '0.9375rem',
                                        fontWeight: 500,
                                        cursor: 'pointer',
                                        transition: 'background-color 0.2s'
                                    }}
                                    onMouseEnter={(e) => e.currentTarget.style.backgroundColor = '#1d4ed8'}
                                    onMouseLeave={(e) => e.currentTarget.style.backgroundColor = '#2563eb'}
                                >
                                    Dashboard
                                </button>
                            ) : (
                                <>
                                    <button
                                        onClick={() => navigateTo('/auth/login')}
                                        style={{
                                            background: 'none',
                                            border: 'none',
                                            color: linkColor,
                                            fontSize: '0.9375rem',
                                            fontWeight: 500,
                                            cursor: 'pointer',
                                            padding: 0,
                                            transition: 'color 0.2s'
                                        }}
                                        onMouseEnter={(e) => e.currentTarget.style.color = '#2563eb'}
                                        onMouseLeave={(e) => e.currentTarget.style.color = linkColor}
                                    >
                                        Log In
                                    </button>
                                    <button
                                        onClick={() => navigateTo('/auth/register')}
                                        style={{
                                            backgroundColor: '#2563eb',
                                            color: '#ffffff',
                                            border: 'none',
                                            padding: '10px 24px',
                                            borderRadius: 4,
                                            fontSize: '0.9375rem',
                                            fontWeight: 500,
                                            cursor: 'pointer',
                                            transition: 'background-color 0.2s'
                                        }}
                                        onMouseEnter={(e) => e.currentTarget.style.backgroundColor = '#1d4ed8'}
                                        onMouseLeave={(e) => e.currentTarget.style.backgroundColor = '#2563eb'}
                                    >
                                        Sign Up
                                    </button>
                                </>
                            )}
                        </div>
                    </div>

                    {/* Mobile Menu Button */}
                    <button
                        onClick={() => setIsMobileMenuOpen(!isMobileMenuOpen)}
                        style={{
                            display: 'none',
                            background: 'none',
                            border: 'none',
                            color: textColor,
                            fontSize: 24,
                            cursor: 'pointer',
                            padding: 8
                        }}
                        className="mobile-menu-btn"
                        aria-label={isMobileMenuOpen ? 'Close menu' : 'Open menu'}
                    >
                        {isMobileMenuOpen ? <CloseOutlined /> : <MenuOutlined />}
                    </button>
                </div>

                {/* Mobile Menu */}
                {isMobileMenuOpen && (
                    <div style={{
                        backgroundColor: '#ffffff',
                        borderTop: '1px solid #e5e7eb',
                        padding: '24px'
                    }}
                        className="mobile-menu">
                        <div style={{
                            display: 'flex',
                            flexDirection: 'column',
                            gap: 16
                        }}>
                            {[
                                { label: 'Security', path: '/security' },
                                { label: 'Pricing', path: '/pricing' },
                                { label: 'Learn', path: '/learn' }
                            ].map((item) => (
                                <button
                                    key={item.path}
                                    onClick={() => navigateTo(item.path)}
                                    style={{
                                        background: 'none',
                                        border: 'none',
                                        color: '#1a1a1a',
                                        fontSize: '1rem',
                                        fontWeight: 500,
                                        cursor: 'pointer',
                                        padding: '12px 0',
                                        textAlign: 'left',
                                        borderBottom: '1px solid #f3f4f6'
                                    }}
                                >
                                    {item.label}
                                </button>
                            ))}

                            <div style={{
                                display: 'flex',
                                flexDirection: 'column',
                                gap: 12,
                                marginTop: 16
                            }}>
                                {isAuthenticated ? (
                                    <button
                                        onClick={() => navigateTo('/dashboard')}
                                        style={{
                                            backgroundColor: '#2563eb',
                                            color: '#ffffff',
                                            border: 'none',
                                            padding: '12px 24px',
                                            borderRadius: 4,
                                            fontSize: '1rem',
                                            fontWeight: 500,
                                            cursor: 'pointer',
                                            width: '100%'
                                        }}
                                    >
                                        Dashboard
                                    </button>
                                ) : (
                                    <>
                                        <button
                                            onClick={() => navigateTo('/auth/register')}
                                            style={{
                                                backgroundColor: '#2563eb',
                                                color: '#ffffff',
                                                border: 'none',
                                                padding: '12px 24px',
                                                borderRadius: 4,
                                                fontSize: '1rem',
                                                fontWeight: 500,
                                                cursor: 'pointer',
                                                width: '100%'
                                            }}
                                        >
                                            Sign Up
                                        </button>
                                        <button
                                            onClick={() => navigateTo('/auth/login')}
                                            style={{
                                                backgroundColor: 'transparent',
                                                color: '#2563eb',
                                                border: '1px solid #2563eb',
                                                padding: '12px 24px',
                                                borderRadius: 4,
                                                fontSize: '1rem',
                                                fontWeight: 500,
                                                cursor: 'pointer',
                                                width: '100%'
                                            }}
                                        >
                                            Log In
                                        </button>
                                    </>
                                )}
                            </div>
                        </div>
                    </div>
                )}
            </nav>

            {/* Add responsive CSS */}
            <style>{`
                @media (min-width: 768px) {
                    .desktop-nav {
                        display: flex !important;
                    }
                    .mobile-menu-btn {
                        display: none !important;
                    }
                    .mobile-menu {
                        display: none !important;
                    }
                }
                @media (max-width: 767px) {
                    .mobile-menu-btn {
                        display: block !important;
                    }
                }
            `}</style>
        </>
    );
};

export default Navbar;