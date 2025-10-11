import React, { useState, useEffect } from 'react';
import { Subscription } from '../../types/subscription';
import SubscriptionProgressOverlay from './SubscriptionProgressOverlay';
import CompactProgressOverlay from './CompactProgressOverlay';

interface ResponsiveProgressOverlayProps {
    subscription: Subscription;
    show: boolean;
    /**
     * Breakpoint in pixels to switch between full and compact view
     * Default: 768px (tablet breakpoint)
     */
    breakpoint?: number;
}

/**
 * Responsive wrapper that automatically switches between full and compact
 * progress overlays based on screen size
 */
const ResponsiveProgressOverlay: React.FC<ResponsiveProgressOverlayProps> = ({
    subscription,
    show,
    breakpoint = 768
}) => {
    const [isCompact, setIsCompact] = useState(false);

    useEffect(() => {
        // Check initial screen size
        const checkScreenSize = () => {
            setIsCompact(window.innerWidth < breakpoint);
        };

        // Check on mount
        checkScreenSize();

        // Add resize listener
        window.addEventListener('resize', checkScreenSize);

        // Cleanup
        return () => window.removeEventListener('resize', checkScreenSize);
    }, [breakpoint]);

    // Render appropriate overlay based on screen size
    if (isCompact) {
        return (
            <CompactProgressOverlay
                subscription={subscription}
                show={show}
            />
        );
    }

    return (
        <SubscriptionProgressOverlay
            subscription={subscription}
            show={show}
        />
    );
};

export default ResponsiveProgressOverlay;
