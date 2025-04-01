import React from 'react';
import { AnimatePresence, motion } from 'framer-motion';

interface NotificationBellProps {
    unreadCount: number;
    isActive: boolean;
    onClick: () => void;
}

/**
 * An enhanced notification bell component with animations
 */
const NotificationBell: React.FC<NotificationBellProps> = ({
    unreadCount,
    isActive,
    onClick
}) => {
    return (
        <button
            type="button"
            onClick={onClick}
            className={`relative rounded-full p-1.5 transition-colors focus:outline-none focus:ring-2 focus:ring-white focus:ring-offset-2 focus:ring-offset-gray-800 ${isActive
                    ? 'bg-blue-600 text-white'
                    : 'bg-gray-800 text-gray-400 hover:text-white'
                }`}
            aria-label={`${unreadCount > 0 ? `${unreadCount} unread notifications` : 'Notifications'}`}
        >
            {/* Bell Icon with subtle animation */}
            <motion.svg
                className="h-6 w-6"
                fill="none"
                stroke="currentColor"
                viewBox="0 0 24 24"
                initial={{ rotate: 0 }}
                animate={{
                    rotate: unreadCount > 0 ? [0, -5, 5, -5, 5, 0] : 0
                }}
                transition={{
                    duration: 0.5,
                    repeat: unreadCount > 0 ? 1 : 0,
                    repeatDelay: 5
                }}
            >
                <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    strokeWidth={2}
                    d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9"
                />
            </motion.svg>

            {/* Notification Badge */}
            <AnimatePresence>
                {unreadCount > 0 && (
                    <motion.span
                        className="absolute top-0 right-0 inline-flex items-center justify-center px-2 py-1 text-xs font-bold leading-none transform translate-x-1/2 -translate-y-1/2 rounded-full bg-red-500 text-white"
                        initial={{ scale: 0.5, opacity: 0 }}
                        animate={{ scale: 1, opacity: 1 }}
                        exit={{ scale: 0.5, opacity: 0 }}
                        transition={{ type: 'spring', stiffness: 500, damping: 25 }}
                    >
                        {unreadCount > 99 ? '99+' : unreadCount}
                    </motion.span>
                )}
            </AnimatePresence>
        </button>
    );
};

export default NotificationBell;