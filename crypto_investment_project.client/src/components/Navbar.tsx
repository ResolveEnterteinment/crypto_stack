import { Disclosure, DisclosureButton, DisclosurePanel, Menu, MenuButton, MenuItem, MenuItems } from '@headlessui/react';
import { Bars3Icon, XMarkIcon } from '@heroicons/react/24/outline';
import React, { useEffect, useState } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import NotificationPanel from "./Notification/NotificationPanel";

interface NavItem {
    name: string;
    href: string;
    exact?: boolean;
}

const navigation: NavItem[] = [
    { name: 'Dashboard', href: '/dashboard', exact: true },
    { name: 'Portfolio', href: '/portfolio' },
    { name: 'Subscriptions', href: '/subscriptions' },
    { name: 'Market', href: '/market' },
];

const Navbar: React.FC = () => {
    const location = useLocation();
    const [scrolled, setScrolled] = useState(false);

    const navigate = useNavigate();
    const { user, logout } = useAuth();

    // Navigation handlers
    const showProfile = () => navigate('/profile');
    const showSettings = () => navigate('/settings');
    const handleLogout = async () => {
        await logout();
        navigate('/auth');
    };

    // Handle scroll effect for navbar
    useEffect(() => {
        const handleScroll = () => {
            setScrolled(window.scrollY > 10);
        };

        window.addEventListener('scroll', handleScroll);

        return () => {
            window.removeEventListener('scroll', handleScroll);
        };
    }, []);

    // Check if a nav item is active
    const isActive = (navItem: NavItem) => {
        if (navItem.exact) {
            return location.pathname === navItem.href;
        }
        return location.pathname.startsWith(navItem.href);
    };

    // Get user initials for avatar
    const getUserInitials = () => {
        if (!user || !user.fullName) return "?";

        return user.fullName 
            .split(" ")
            .map((name: string) => name.charAt(0))
            .join("")
            .toUpperCase();
    };

    return (
        <Disclosure
            as="nav"
            className={`fixed w-full top-0 z-50 bg-gray-800 transition-shadow duration-300 ${scrolled ? 'shadow-lg' : ''
                }`}
        >
            {({ open }) => (
                <>
                    <div className="mx-auto px-2 sm:px-6 lg:px-8">
                        <div className="relative flex h-16 items-center justify-between">
                            {/* Mobile menu button */}
                            <div className="absolute inset-y-0 left-0 flex items-center sm:hidden">
                                <DisclosureButton className="relative inline-flex items-center justify-center rounded-md p-2 text-gray-400 hover:bg-gray-700 hover:text-white focus:ring-2 focus:ring-white focus:outline-none">
                                    <span className="sr-only">Open main menu</span>
                                    {open ? (
                                        <XMarkIcon className="block h-6 w-6" aria-hidden="true" />
                                    ) : (
                                        <Bars3Icon className="block h-6 w-6" aria-hidden="true" />
                                    )}
                                </DisclosureButton>
                            </div>

                            {/* Logo & Navigation */}
                            <div className="flex flex-1 items-center justify-center sm:items-stretch sm:justify-start">
                                <div className="flex shrink-0 items-center">
                                    {/* Company logo */}
                                    <Link to="/dashboard" className="flex items-center">
                                        <svg
                                            className="h-8 w-8 text-blue-500"
                                            fill="none"
                                            viewBox="0 0 24 24"
                                            stroke="currentColor"
                                        >
                                            <path
                                                strokeLinecap="round"
                                                strokeLinejoin="round"
                                                strokeWidth={2}
                                                d="M13 7h8m0 0v8m0-8l-8 8-4-4-6 6"
                                            />
                                        </svg>
                                        <span className="ml-2 font-bold text-xl text-white hidden md:block">CryptoVest</span>
                                    </Link>
                                </div>

                                {/* Desktop Navigation */}
                                <div className="hidden sm:ml-6 sm:block">
                                    <div className="flex space-x-4">
                                        {navigation.map((item) => (
                                            <Link
                                                key={item.name}
                                                to={item.href}
                                                className={`${isActive(item)
                                                        ? 'bg-gray-900 text-white'
                                                        : 'text-gray-300 hover:bg-gray-700 hover:text-white'
                                                    } rounded-md px-3 py-2 text-sm font-medium transition-colors`}
                                                aria-current={isActive(item) ? 'page' : undefined}
                                            >
                                                {item.name}
                                            </Link>
                                        ))}
                                    </div>
                                </div>
                            </div>

                            {/* Notification Bell & User Menu */}
                            <div className="absolute inset-y-0 right-0 flex items-center pr-2 sm:static sm:inset-auto sm:ml-6 sm:pr-0">
                                {/* Notifications Panel */}
                                <NotificationPanel />

                                {/* Profile Dropdown */}
                                <Menu as="div" className="relative ml-3">
                                    <MenuButton
                                        className="group relative flex rounded-full bg-gray-800 text-sm focus:ring-2 focus:ring-white focus:ring-offset-2 focus:ring-offset-gray-800 focus:outline-none"
                                        aria-label="User menu"
                                    >
                                        <span className="inline-flex h-10 w-10 items-center justify-center overflow-hidden rounded-full bg-gray-600 text-xl font-semibold text-gray-200 ring-2 ring-blue-500 group-hover:ring-blue-400 transition-all">
                                            {getUserInitials()}
                                        </span>
                                    </MenuButton>
                                    <MenuItems className="absolute right-0 z-10 mt-2 w-48 origin-top-right rounded-md bg-white py-1 shadow-lg ring-1 ring-black/5 transition">
                                        <MenuItem>
                                            {({ active }) => (
                                                <button
                                                    onClick={showProfile}
                                                    className={`block px-4 py-2 text-sm text-gray-700 w-full text-left ${active ? "bg-gray-100" : ""}`}
                                                >
                                                    Your Profile
                                                </button>
                                            )}
                                        </MenuItem>
                                        <MenuItem>
                                            {({ active }) => (
                                                <button
                                                    onClick={showSettings}
                                                    className={`block px-4 py-2 text-sm text-gray-700 w-full text-left ${active ? "bg-gray-100" : ""}`}
                                                >
                                                    Settings
                                                </button>
                                            )}
                                        </MenuItem>
                                        <MenuItem>
                                            {({ active }) => (
                                                <button
                                                    onClick={handleLogout}
                                                    className={`block px-4 py-2 text-sm text-gray-700 w-full text-left ${active ? "bg-gray-100" : ""}`}
                                                >
                                                    Sign out
                                                </button>
                                            )}
                                        </MenuItem>
                                    </MenuItems>
                                </Menu>
                            </div>
                        </div>
                    </div>

                    {/* Mobile menu dropdown */}
                    <DisclosurePanel className="sm:hidden">
                        <div className="space-y-1 px-2 pb-3 pt-2">
                            {navigation.map((item) => (
                                <Link
                                    key={item.name}
                                    to={item.href}
                                    className={`${isActive(item)
                                            ? 'bg-gray-900 text-white'
                                            : 'text-gray-300 hover:bg-gray-700 hover:text-white'
                                        } block rounded-md px-3 py-2 text-base font-medium`}
                                    aria-current={isActive(item) ? 'page' : undefined}
                                >
                                    {item.name}
                                </Link>
                            ))}
                        </div>
                    </DisclosurePanel>
                </>
            )}
        </Disclosure>
    );
};

export default Navbar;