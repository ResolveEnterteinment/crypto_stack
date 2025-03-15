import { Disclosure, DisclosureButton, Menu, MenuButton, MenuItem, MenuItems } from '@headlessui/react';
import { Bars3Icon, BellIcon, XMarkIcon } from '@heroicons/react/24/outline';
import React, { useState, useEffect, useRef } from "react";
import { useAuth } from "../context/AuthContext";
import { getNotifications } from "../services/notification";
import Notifications from "./Notifications";

const navigation = [
    { name: 'Dashboard', href: '#', current: true },
    { name: 'Team', href: '#', current: false },
    { name: 'Projects', href: '#', current: false },
    { name: 'Calendar', href: '#', current: false },
];

function classNames(...classes: string[]) {
    return classes.filter(Boolean).join(' ');
}

const Navbar: React.FC<{ showProfile: () => void; showSettings: () => void; logout: () => void }> = ({ showProfile, showSettings, logout }) => {
    const [showNotifications, setShowNotifications] = useState(false);
    const [unreadCount, setUnreadCount] = useState(0);
    const { user } = useAuth();
    const notificationRef = useRef<HTMLDivElement>(null); // ✅ Reference for outside click detection

    useEffect(() => {
        if (!user || !user.id) return;

        const fetchUnreadCount = async () => {
            const notifications = await getNotifications(user.id);
            setUnreadCount(notifications.filter((n: { isRead: any; }) => !n.isRead).length);
        };

        fetchUnreadCount();
    }, [user]);

    // ✅ Close notifications when clicking outside
    useEffect(() => {
        const handleClickOutside = (event: MouseEvent) => {
            if (notificationRef.current && !notificationRef.current.contains(event.target as Node)) {
                setShowNotifications(false);
            }
        };

        document.addEventListener("mousedown", handleClickOutside);
        return () => {
            document.removeEventListener("mousedown", handleClickOutside);
        };
    }, []);

    const getUserInitials = () => {
        if (!user || !user.username) return "?";
        return user.username
            .split(" ")
            .map((name: string) => name.charAt(0))
            .join("")
            .toUpperCase();
    };

    return (
        <Disclosure as="nav" className="bg-gray-800">
            <div className="mx-auto max-w-7xl px-2 sm:px-6 lg:px-8">
                <div className="relative flex h-16 items-center justify-between">
                    {/* Mobile Menu */}
                    <div className="absolute inset-y-0 left-0 flex items-center sm:hidden">
                        <DisclosureButton className="group relative inline-flex items-center justify-center rounded-md p-2 text-gray-400 hover:bg-gray-700 hover:text-white focus:ring-2 focus:ring-white focus:outline-none">
                            <span className="sr-only">Open main menu</span>
                            <Bars3Icon aria-hidden="true" className="block size-6 group-data-open:hidden" />
                            <XMarkIcon aria-hidden="true" className="hidden size-6 group-data-open:block" />
                        </DisclosureButton>
                    </div>

                    {/* Logo & Navigation */}
                    <div className="flex flex-1 items-center justify-center sm:items-stretch sm:justify-start">
                        <div className="flex shrink-0 items-center">
                            <img
                                alt="Company Logo"
                                src="https://tailwindcss.com/plus-assets/img/logos/mark.svg?color=indigo&shade=500"
                                className="h-8 w-auto"
                            />
                        </div>
                        <div className="hidden sm:ml-6 sm:block">
                            <div className="flex space-x-4">
                                {navigation.map((item) => (
                                    <a
                                        key={item.name}
                                        href={item.href}
                                        aria-current={item.current ? "page" : undefined}
                                        className={classNames(
                                            item.current ? "bg-gray-900 text-white" : "text-gray-300 hover:bg-gray-700 hover:text-white",
                                            "rounded-md px-3 py-2 text-sm font-medium"
                                        )}
                                    >
                                        {item.name}
                                    </a>
                                ))}
                            </div>
                        </div>
                    </div>

                    {/* Notification Bell & User Menu */}
                    <div className="absolute inset-y-0 right-0 flex items-center pr-2 sm:static sm:inset-auto sm:ml-6 sm:pr-0">
                        {/* Notifications Panel */}
                        <div ref={notificationRef} className="relative">
                            <button
                                type="button"
                                onClick={() => setShowNotifications(!showNotifications)}
                                className="relative rounded-full bg-gray-800 p-1 text-gray-400 hover:text-white focus:ring-2 focus:ring-white focus:ring-offset-2 focus:ring-offset-gray-800 focus:outline-none"
                            >
                                <span className="sr-only">View notifications</span>
                                <BellIcon aria-hidden="true" className="size-6" />
                                {unreadCount > 0 && (
                                    <span className="absolute top-0 right-0 flex h-3 w-3 items-center justify-center rounded-full bg-red-500 text-xs font-bold text-white">
                                        {unreadCount}
                                    </span>
                                )}
                            </button>

                            {showNotifications && <Notifications onUpdateUnread={setUnreadCount} />}
                        </div>

                        {/* Profile Dropdown */}
                        <Menu as="div" className="relative ml-3">
                            <div>
                                <MenuButton className="relative flex rounded-full bg-gray-800 text-sm focus:ring-2 focus:ring-white focus:ring-offset-2 focus:ring-offset-gray-800 focus:outline-none">
                                    <span className="sr-only">Open user menu</span>
                                    <span className="inline-flex size-11 items-center justify-center overflow-hidden rounded-full bg-gray-500 text-2xl font-semibold text-gray-300">
                                        {getUserInitials()}
                                    </span>
                                </MenuButton>
                            </div>
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
                                            onClick={logout}
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
        </Disclosure>
    );
};

export default Navbar;
