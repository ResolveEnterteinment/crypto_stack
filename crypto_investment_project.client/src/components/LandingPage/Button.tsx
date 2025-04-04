import React, { useState } from 'react';

/**
 * Animated Button Component with hover effects
 * 
 * @param {Object} props - Component props
 * @param {string} props.variant - Button style variant ('primary', 'secondary', 'gradient', 'outline', 'ghost')
 * @param {string} props.size - Button size ('sm', 'md', 'lg')
 * @param {boolean} props.isFullWidth - Whether button should take full width
 * @param {Function} props.onClick - Click handler function
 * @param {string} props.href - Optional URL if button should act as a link
 * @param {boolean} props.isDisabled - Whether button is disabled
 * @param {boolean} props.isLoading - Whether button is in loading state
 * @param {string} props.icon - Optional icon to display
 * @param {string} props.iconPosition - Position of icon ('left' or 'right')
 * @param {React.ReactNode} props.children - Button content
 */
const Button = ({
    variant = 'primary',
    size = 'md',
    isFullWidth = false,
    onClick,
    href,
    isDisabled = false,
    isLoading = false,
    icon,
    iconPosition = 'left',
    children,
    className = '',
    ...props
}) => {
    const [isHovered, setIsHovered] = useState(false);

    // Determine base classes for the button
    const baseClasses = 'relative inline-flex items-center justify-center font-medium transition-all focus:outline-none focus:ring-2 focus:ring-offset-2 overflow-hidden';

    // Size classes
    const sizeClasses = {
        sm: 'text-sm px-4 py-2 rounded-md',
        md: 'text-base px-6 py-3 rounded-lg',
        lg: 'text-lg px-8 py-4 rounded-lg',
    };

    // Variant classes
    const variantClasses = {
        primary: 'bg-blue-600 hover:bg-blue-700 text-white focus:ring-blue-500',
        secondary: 'bg-indigo-600 hover:bg-indigo-700 text-white focus:ring-indigo-500',
        gradient: 'text-white focus:ring-purple-500 bg-gradient-to-r from-blue-600 to-indigo-600 hover:from-blue-700 hover:to-indigo-700',
        outline: 'bg-transparent border-2 border-current text-blue-600 hover:bg-blue-50 focus:ring-blue-500',
        ghost: 'bg-transparent hover:bg-gray-100 text-gray-800 focus:ring-gray-500',
        danger: 'bg-red-600 hover:bg-red-700 text-white focus:ring-red-500',
        success: 'bg-green-600 hover:bg-green-700 text-white focus:ring-green-500',
    };

    // Handle width
    const widthClass = isFullWidth ? 'w-full' : '';

    // Handle disabled state
    const disabledClasses = isDisabled
        ? 'opacity-60 cursor-not-allowed pointer-events-none'
        : 'transform hover:-translate-y-1 hover:shadow-lg';

    // Combine all classes
    const buttonClasses = `
    ${baseClasses} 
    ${sizeClasses[size] || sizeClasses.md} 
    ${variantClasses[variant] || variantClasses.primary} 
    ${widthClass} 
    ${disabledClasses}
    ${className}
  `;

    // Render loading spinner
    const renderSpinner = () => (
        <svg className="animate-spin -ml-1 mr-2 h-4 w-4" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
            <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
            <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
        </svg>
    );

    // Render icon
    const renderIcon = () => {
        if (!icon) return null;

        return (
            <span className={iconPosition === 'right' ? 'ml-2' : 'mr-2'}>
                {icon}
            </span>
        );
    };

    // Render button content
    const renderContent = () => (
        <>
            {isLoading && renderSpinner()}
            {icon && iconPosition === 'left' && renderIcon()}
            <span className="relative z-10">{children}</span>
            {icon && iconPosition === 'right' && renderIcon()}

            {/* Animated background hover effect for gradient buttons */}
            {variant === 'gradient' && (
                <span
                    className={`absolute inset-0 bg-gradient-to-r from-blue-700 to-indigo-700 transition-opacity duration-300 ${isHovered ? 'opacity-100' : 'opacity-0'}`}
                />
            )}

            {/* Animated border for outline buttons */}
            {variant === 'outline' && (
                <span
                    className={`absolute inset-0 border-2 border-blue-600 rounded-lg transition-all duration-300 ${isHovered ? 'opacity-100' : 'opacity-0'}`}
                    style={{
                        clipPath: isHovered
                            ? 'polygon(0 0, 100% 0, 100% 100%, 0 100%)'
                            : 'polygon(0 0, 0 0, 0 100%, 0 100%)'
                    }}
                />
            )}
        </>
    );

    // Handle mouse events for hover animations
    const mouseEvents = {
        onMouseEnter: () => setIsHovered(true),
        onMouseLeave: () => setIsHovered(false),
        onFocus: () => setIsHovered(true),
        onBlur: () => setIsHovered(false),
    };

    // Render as link if href is provided
    if (href && !isDisabled) {
        return (
            <a
                href={href}
                className={buttonClasses}
                {...mouseEvents}
                {...props}
            >
                {renderContent()}
            </a>
        );
    }

    // Otherwise render as button
    return (
        <button
            type="button"
            onClick={onClick}
            disabled={isDisabled || isLoading}
            className={buttonClasses}
            {...mouseEvents}
            {...props}
        >
            {renderContent()}
        </button>
    );
};

export default Button;