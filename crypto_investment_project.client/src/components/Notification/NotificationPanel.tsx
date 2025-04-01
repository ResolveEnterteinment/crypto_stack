import React, { useState, useEffect, useRef } from "react";
import { AnimatePresence, motion } from "framer-motion";
import { useNotifications } from "../../context/NotificationContext";
import NotificationBell from "./NotificationBell";
import NotificationItem from "./NotificationItem";

/**
 * Integrated notification panel with advanced animations
 */
const NotificationPanel: React.FC = () => {
    // Local state for panel visibility
    const [isOpen, setIsOpen] = useState(false);
    const [showConnectionDetails, setShowConnectionDetails] = useState(false);

    // Get notification data and functions from context
    const {
        notifications,
        unreadCount,
        isLoading,
        error,
        connectionStatus,
        lastConnectionError,
        refreshNotifications,
        markAsRead,
        markAllNotificationsAsRead,
        reconnect
    } = useNotifications();

    // Reference for detecting outside clicks
    const panelRef = useRef<HTMLDivElement>(null);

    // Toggle panel visibility
    const togglePanel = () => setIsOpen(prev => !prev);

    // Handle outside clicks to close panel
    useEffect(() => {
        const handleClickOutside = (event: MouseEvent) => {
            if (panelRef.current && !panelRef.current.contains(event.target as Node)) {
                setIsOpen(false);
            }
        };

        document.addEventListener("mousedown", handleClickOutside);
        return () => {
            document.removeEventListener("mousedown", handleClickOutside);
        };
    }, []);

    // Connection status indicator component
    const ConnectionStatusIndicator = () => (
        <div className="mb-2">
            <button
                type="button"
                onClick={() => setShowConnectionDetails(!showConnectionDetails)}
                className={`flex items-center text-xs ${connectionStatus === 'connected'
                        ? 'text-green-600'
                        : connectionStatus === 'connecting'
                            ? 'text-yellow-600'
                            : 'text-red-600'
                    }`}
            >
                <div className={`h-2 w-2 rounded-full mr-1 ${connectionStatus === 'connected'
                        ? 'bg-green-600 animate-pulse'
                        : connectionStatus === 'connecting'
                            ? 'bg-yellow-600 animate-pulse'
                            : 'bg-red-600'
                    }`}></div>
                {connectionStatus === 'connected'
                    ? 'Connected'
                    : connectionStatus === 'connecting'
                        ? 'Connecting...'
                        : 'Disconnected'}

                <svg
                    className={`w-3 h-3 ml-1 transition-transform ${showConnectionDetails ? 'rotate-180' : ''}`}
                    fill="none"
                    stroke="currentColor"
                    viewBox="0 0 24 24"
                    xmlns="http://www.w3.org/2000/svg"
                >
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M19 9l-7 7-7-7" />
                </svg>
            </button>

            {/* Connection error details with animation */}
            <AnimatePresence>
                {showConnectionDetails && (
                    <motion.div
                        className="mt-1 text-xs"
                        initial={{ opacity: 0, height: 0 }}
                        animate={{ opacity: 1, height: 'auto' }}
                        exit={{ opacity: 0, height: 0 }}
                        transition={{ duration: 0.2 }}
                    >
                        {connectionStatus !== 'connected' && lastConnectionError && (
                            <div className="bg-red-50 p-2 rounded text-red-700 mb-2">
                                <p><strong>Error:</strong> {lastConnectionError}</p>
                                <button
                                    onClick={reconnect}
                                    className="mt-1 bg-red-100 hover:bg-red-200 text-red-800 text-xs px-2 py-1 rounded transition-colors"
                                >
                                    Retry Connection
                                </button>
                            </div>
                        )}

                        {connectionStatus === 'connected' && (
                            <p className="text-green-600">
                                Real-time notifications are working properly.
                            </p>
                        )}
                    </motion.div>
                )}
            </AnimatePresence>
        </div>
    );

    // Empty state with animation
    const EmptyState = () => (
        <motion.div
            className="py-6 text-center text-gray-500"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            transition={{ delay: 0.2 }}
        >
            <svg className="h-10 w-10 mx-auto mb-2 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9"></path>
            </svg>
            <p>No notifications yet</p>
        </motion.div>
    );

    // Loading state with animation
    const LoadingState = () => (
        <div className="py-4 text-center text-gray-500">
            <svg className="animate-spin h-5 w-5 mx-auto mb-2" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
            </svg>
            <p>Loading notifications...</p>
        </div>
    );

    return (
        <div ref={panelRef} className="relative">
            {/* Notification Bell */}
            <NotificationBell
                unreadCount={unreadCount}
                isActive={isOpen}
                onClick={togglePanel}
            />

            {/* Notification Panel with animation */}
            <AnimatePresence>
                {isOpen && (
                    <motion.div
                        className="absolute top-14 right-4 bg-white shadow-xl rounded-lg w-80 overflow-hidden z-50 border border-gray-200"
                        initial={{ opacity: 0, y: -10, scale: 0.95 }}
                        animate={{ opacity: 1, y: 0, scale: 1 }}
                        exit={{ opacity: 0, y: -10, scale: 0.95 }}
                        transition={{ duration: 0.2 }}
                    >
                        {/* Panel Header */}
                        <div className="p-4 border-b border-gray-100">
                            <div className="flex justify-between items-center mb-2">
                                <h3 className="text-lg font-semibold">Notifications</h3>
                                {unreadCount > 0 && (
                                    <button
                                        onClick={markAllNotificationsAsRead}
                                        className="text-xs text-blue-600 hover:text-blue-800 transition-colors"
                                    >
                                        Mark all as read
                                    </button>
                                )}
                            </div>

                            <ConnectionStatusIndicator />

                            {/* Error message with animation */}
                            <AnimatePresence>
                                {error && (
                                    <motion.div
                                        className="bg-red-100 text-red-700 p-2 rounded mb-2 text-xs"
                                        initial={{ opacity: 0, height: 0 }}
                                        animate={{ opacity: 1, height: 'auto' }}
                                        exit={{ opacity: 0, height: 0 }}
                                    >
                                        {error}
                                    </motion.div>
                                )}
                            </AnimatePresence>
                        </div>

                        {/* Notification List with max height and scrolling */}
                        <div className="overflow-y-auto" style={{ maxHeight: 'calc(24rem - 130px)' }}>
                            <motion.div
                                className="divide-y divide-gray-200"
                                initial="hidden"
                                animate="visible"
                                variants={{
                                    visible: {
                                        transition: {
                                            staggerChildren: 0.05
                                        }
                                    }
                                }}
                            >
                                {isLoading ? (
                                    <LoadingState />
                                ) : notifications.length === 0 ? (
                                    <EmptyState />
                                ) : (
                                    notifications.map((notification) => (
                                        <motion.div
                                            key={notification.id}
                                            variants={{
                                                hidden: { opacity: 0, y: 20 },
                                                visible: { opacity: 1, y: 0 }
                                            }}
                                            transition={{ duration: 0.3 }}
                                        >
                                            <NotificationItem
                                                data={notification}
                                                handleRead={markAsRead}
                                            />
                                        </motion.div>
                                    ))
                                )}
                            </motion.div>
                        </div>

                        {/* Panel Footer */}
                        <div className="p-3 border-t border-gray-100 bg-gray-50 text-xs text-gray-500 flex justify-between items-center">
                            <span>
                                {connectionStatus === 'connected'
                                    ? 'Live updates enabled'
                                    : 'Live updates unavailable'}
                            </span>

                            <button
                                onClick={refreshNotifications}
                                className="text-blue-600 hover:text-blue-800 flex items-center transition-colors"
                            >
                                <svg className="h-3.5 w-3.5 mr-1" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
                                </svg>
                                Refresh
                            </button>
                        </div>
                    </motion.div>
                )}
            </AnimatePresence>
        </div>
    );
};

export default NotificationPanel;