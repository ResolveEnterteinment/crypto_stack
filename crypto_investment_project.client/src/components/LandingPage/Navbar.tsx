/**
 * NAVBAR COMPONENT
 * 
 * Professional navigation for crypto DCA platform
 * 
 * Features:
 * - Ant Design integration
 * - Global styling system (variables.css, global.css)
 * - Smooth scroll behavior
 * - Responsive mobile menu
 * - Accessibility compliant
 * - Dark mode support
 * 
 * Design principles:
 * - Minimal, clean design consistent with landing page
 * - Security-first messaging
 * - Professional appearance
 */

import React, { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import { LockOutlined, MenuOutlined, CloseOutlined } from '@ant-design/icons';
import { Button, Drawer, Space } from 'antd';
import '../../styles/LandingPage/Navbar.css';

/* ========================================
   TYPE DEFINITIONS
   ======================================== */

interface NavbarProps {
    transparent?: boolean;
}

interface NavLink {
    label: string;
    path: string;
}

/* ========================================
   CONSTANTS
   ======================================== */

const NAV_LINKS: NavLink[] = [
    { label: 'Security', path: '/security' },
    { label: 'Pricing', path: '/pricing' },
    { label: 'Learn', path: '/learn' }
];

/* ========================================
   MAIN NAVBAR COMPONENT
   ======================================== */

const Navbar: React.FC<NavbarProps> = ({ transparent = true }) => {
    const navigate = useNavigate();
    const { isAuthenticated } = useAuth();
    const [isScrolled, setIsScrolled] = useState(false);
    const [isMobileMenuOpen, setIsMobileMenuOpen] = useState(false);

    /* ========================================
       SCROLL HANDLER
       ======================================== */

    useEffect(() => {
        const handleScroll = () => {
            setIsScrolled(window.scrollY > 20);
        };

        handleScroll(); // Set initial state
        window.addEventListener('scroll', handleScroll, { passive: true });
        return () => window.removeEventListener('scroll', handleScroll);
    }, []);

    /* ========================================
       KEYBOARD NAVIGATION
       ======================================== */

    useEffect(() => {
        const handleEsc = (e: KeyboardEvent) => {
            if (e.key === 'Escape' && isMobileMenuOpen) {
                setIsMobileMenuOpen(false);
            }
        };

        if (isMobileMenuOpen) {
            document.addEventListener('keydown', handleEsc);
            // Prevent body scroll when menu is open
            document.body.style.overflow = 'hidden';
            return () => {
                document.removeEventListener('keydown', handleEsc);
                document.body.style.overflow = '';
            };
        }
    }, [isMobileMenuOpen]);

    /* ========================================
       NAVIGATION HANDLERS
       ======================================== */

    const navigateTo = useCallback((path: string) => {
        setIsMobileMenuOpen(false);
        navigate(path);
    }, [navigate]);

    const toggleMobileMenu = useCallback(() => {
        setIsMobileMenuOpen(prev => !prev);
    }, []);

    /* ========================================
       DYNAMIC STYLES
       ======================================== */

    const navClassName = `navbar ${isScrolled ? 'navbar-scrolled' : ''} ${transparent && !isScrolled ? 'navbar-transparent' : ''}`;

    /* ========================================
       RENDER
       ======================================== */

    return (
        <>
            <nav className={navClassName} role="navigation" aria-label="Main navigation">
                <div className="navbar-container">
                    {/* Logo */}
                    <button
                        onClick={() => navigateTo('/')}
                        className="navbar-logo"
                        aria-label="Go to homepage"
                    >
                        <LockOutlined className="logo-icon" aria-hidden="true" />
                        <span className="logo-text">CryptoInvest</span>
                    </button>

                    {/* Desktop Navigation */}
                    <div className="navbar-desktop">
                        <nav className="navbar-links" aria-label="Primary navigation">
                            {NAV_LINKS.map((item) => (
                                <Button
                                    key={item.path}
                                    type="link"
                                    onClick={() => navigateTo(item.path)}
                                    className="nav-link"
                                    aria-label={`Navigate to ${item.label}`}
                                >
                                    {item.label}
                                </Button>
                            ))}
                        </nav>

                        <Space size="middle" className="navbar-actions">
                            {isAuthenticated ? (
                                <Button
                                    type="primary"
                                    onClick={() => navigateTo('/dashboard')}
                                    className="nav-button-primary"
                                    aria-label="Go to dashboard"
                                >
                                    Dashboard
                                </Button>
                            ) : (
                                <>
                                    <Button
                                        type="link"
                                        onClick={() => navigateTo('/auth/login')}
                                        className="nav-link"
                                        aria-label="Log in to your account"
                                    >
                                        Log In
                                    </Button>
                                    <Button
                                        type="primary"
                                        onClick={() => navigateTo('/auth/register')}
                                        className="nav-button-primary"
                                        aria-label="Create a new account"
                                    >
                                        Sign Up
                                    </Button>
                                </>
                            )}
                        </Space>
                    </div>

                    {/* Mobile Menu Button */}
                    <Button
                        type="text"
                        icon={isMobileMenuOpen ? <CloseOutlined /> : <MenuOutlined />}
                        onClick={toggleMobileMenu}
                        className="navbar-mobile-toggle"
                        aria-label={isMobileMenuOpen ? 'Close menu' : 'Open menu'}
                        aria-expanded={isMobileMenuOpen}
                        aria-controls="mobile-menu"
                    />
                </div>
            </nav>

            {/* Mobile Menu Drawer */}
            <Drawer
                id="mobile-menu"
                placement="right"
                open={isMobileMenuOpen}
                onClose={() => setIsMobileMenuOpen(false)}
                className="navbar-mobile-drawer"
                width={300}
                closeIcon={<CloseOutlined />}
                aria-label="Mobile navigation menu"
            >
                <nav className="mobile-menu-content" aria-label="Mobile primary navigation">
                    <div className="mobile-menu-links">
                        {NAV_LINKS.map((item) => (
                            <Button
                                key={item.path}
                                type="text"
                                onClick={() => navigateTo(item.path)}
                                className="mobile-nav-link"
                                block
                                size="large"
                                aria-label={`Navigate to ${item.label}`}
                            >
                                {item.label}
                            </Button>
                        ))}
                    </div>

                    <div className="mobile-menu-actions">
                        {isAuthenticated ? (
                            <Button
                                type="primary"
                                onClick={() => navigateTo('/dashboard')}
                                className="mobile-nav-button-primary"
                                block
                                size="large"
                                aria-label="Go to dashboard"
                            >
                                Dashboard
                            </Button>
                        ) : (
                            <Space direction="vertical" style={{ width: '100%' }} size="middle">
                                <Button
                                    type="primary"
                                    onClick={() => navigateTo('/auth/register')}
                                    className="mobile-nav-button-primary"
                                    block
                                    size="large"
                                    aria-label="Create a new account"
                                >
                                    Sign Up
                                </Button>
                                <Button
                                    onClick={() => navigateTo('/auth/login')}
                                    className="mobile-nav-button-secondary"
                                    block
                                    size="large"
                                    aria-label="Log in to your account"
                                >
                                    Log In
                                </Button>
                            </Space>
                        )}
                    </div>
                </nav>
            </Drawer>
        </>
    );
};

export default Navbar;