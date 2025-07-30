import React, { useState, useEffect, useCallback, useRef } from 'react';
import { Typography } from 'antd';

const { Text } = Typography;

// Document configurations with proper aspect ratios
const DOCUMENT_CONFIGS = {
    passport: {
        aspectRatio: 1.45, // width / height = 1.45
        title: 'Passport',
        instruction: 'Place passport within the frame',
        cornerRadius: 8,
        borderColor: '#52c41a',
        capturingColor: '#faad14',
        icon: '🛂',
        maxWidthPercent: 80, // Maximum width as percentage of container
        maxHeightPercent: 70 // Maximum height as percentage of container
    },
    drivers_license: {
        aspectRatio: 1.59, // width / height = 1.59
        title: 'Driver\'s License',
        instruction: 'Align license horizontally within the frame',
        cornerRadius: 6,
        borderColor: '#1890ff',
        capturingColor: '#faad14',
        icon: '🚗',
        maxWidthPercent: 80,
        maxHeightPercent: 70
    },
    national_id: {
        aspectRatio: 1.58, // width / height = 1.58
        title: 'National ID',
        instruction: 'Position ID card within the guidelines',
        cornerRadius: 6,
        borderColor: '#722ed1',
        capturingColor: '#faad14',
        icon: '🆔',
        maxWidthPercent: 80,
        maxHeightPercent: 70
    }
};

interface DocumentOverlayProps {
    documentType: 'passport' | 'drivers_license' | 'national_id';
    isCapturing?: boolean;
    qualityScore?: number;
    showQualityIndicators?: boolean;
    showDebugInfo?: boolean;
    containerWidth?: number;
    containerHeight?: number;
}

const DocumentOverlay: React.FC<DocumentOverlayProps> = ({
    documentType,
    isCapturing = false,
    qualityScore = 85,
    showQualityIndicators = true,
    showDebugInfo = true,
    containerWidth,
    containerHeight
}) => {
    const [overlayDimensions, setOverlayDimensions] = useState({ width: 0, height: 0 });
    const [isMobile, setIsMobile] = useState(false);
    const overlayRef = useRef<HTMLDivElement>(null);
    const resizeTimeoutRef = useRef<NodeJS.Timeout | null>(null);

    const config = DOCUMENT_CONFIGS[documentType];
    const borderColor = isCapturing ? config.capturingColor : config.borderColor;

    // Debounced resize calculation for better performance with strict container constraints
    const calculateDimensions = useCallback(() => {
        if (resizeTimeoutRef.current) {
            clearTimeout(resizeTimeoutRef.current);
        }

        resizeTimeoutRef.current = setTimeout(() => {
            let parentWidth = containerWidth;
            let parentHeight = containerHeight;

            // If no container dimensions provided, don't render
            if (!parentWidth || !parentHeight) {
                setOverlayDimensions({ width: 0, height: 0 });
                return;
            }

            // Detect mobile devices and adjust accordingly
            const isMobileDevice = window.innerWidth < 768 || /Android|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent);
            setIsMobile(isMobileDevice);

            // Adjust percentages for mobile devices but ensure they never exceed container
            let adjustedMaxWidthPercent = config.maxWidthPercent;
            let adjustedMaxHeightPercent = config.maxHeightPercent;

            if (isMobileDevice) {
                // Increase frame size on mobile for better usability but cap at 85%
                adjustedMaxWidthPercent = Math.min(85, config.maxWidthPercent + 10);
                adjustedMaxHeightPercent = Math.min(70, config.maxHeightPercent + 10);
            }

            // Calculate frame dimensions while maintaining aspect ratio and staying within container
            const maxWidth = Math.min(parentWidth, (parentWidth * adjustedMaxWidthPercent) / 100);
            const maxHeight = Math.min(parentHeight, (parentHeight * adjustedMaxHeightPercent) / 100);

            // Calculate dimensions based on aspect ratio constraints
            let frameWidth, frameHeight;

            // Try fitting by width first
            frameWidth = maxWidth;
            frameHeight = frameWidth / config.aspectRatio;

            // If height exceeds maximum, fit by height instead
            if (frameHeight > maxHeight) {
                frameHeight = maxHeight;
                frameWidth = frameHeight * config.aspectRatio;
            }

            // Ensure minimum dimensions for usability
            const minWidth = 100;
            const minHeight = minWidth / config.aspectRatio;

            frameWidth = Math.max(minWidth, frameWidth);
            frameHeight = Math.max(minHeight, frameHeight);

            // Strict constraint: never exceed container dimensions with padding
            const padding = 20; // Reserve 20px padding on all sides
            const maxAllowedWidth = parentWidth - padding;
            const maxAllowedHeight = parentHeight - padding;

            if (frameWidth > maxAllowedWidth) {
                frameWidth = maxAllowedWidth;
                frameHeight = frameWidth / config.aspectRatio;
            }

            if (frameHeight > maxAllowedHeight) {
                frameHeight = maxAllowedHeight;
                frameWidth = frameHeight * config.aspectRatio;
            }

            // Final safety check
            frameWidth = Math.min(frameWidth, maxAllowedWidth);
            frameHeight = Math.min(frameHeight, maxAllowedHeight);

            setOverlayDimensions({
                width: Math.round(frameWidth),
                height: Math.round(frameHeight)
            });
        }, 100); // 100ms debounce
    }, [containerWidth, containerHeight, config.aspectRatio, config.maxWidthPercent, config.maxHeightPercent]);

    // Calculate dimensions when container dimensions change
    useEffect(() => {
        calculateDimensions();
    }, [calculateDimensions]);

    // Quality indicator colors based on score
    const getQualityColor = (score: number) => {
        if (score >= 80) return '#52c41a';
        if (score >= 60) return '#faad14';
        return '#ff4d4f';
    };

    // Responsive corner size based on overlay dimensions
    const getCornerSize = () => {
        if (isMobile || overlayDimensions.width < 400) return 16;
        return 20;
    };

    // Responsive font sizes
    const getFontSizes = () => {
        const baseSize = isMobile || overlayDimensions.width < 400 ? 0.75 : 1;
        return {
            header: Math.round(12 * baseSize),
            instruction: Math.round(14 * baseSize),
            debug: Math.round(9 * baseSize),
            quality: Math.round(10 * baseSize),
            indicator: Math.round(8 * baseSize)
        };
    };

    // Responsive spacing
    const getSpacing = () => {
        const factor = isMobile || overlayDimensions.width < 400 ? 0.75 : 1;
        return {
            headerTop: Math.round(-45 * factor),
            instructionBottom: Math.round(-50 * factor),
            qualityTop: Math.round(-20 * factor),
            indicatorsTop: Math.round(-25 * factor)
        };
    };

    // Pulsing animation for capturing state
    const pulseKeyframes = `
        @keyframes pulse {
            0% { 
                box-shadow: 0 0 ${isMobile ? '8px' : '12px'} ${borderColor}40;
                transform: scale(1);
            }
            50% { 
                box-shadow: 0 0 ${isMobile ? '12px' : '20px'} ${borderColor}60;
                transform: scale(${isMobile ? '1.005' : '1.01'});
            }
            100% { 
                box-shadow: 0 0 ${isMobile ? '8px' : '12px'} ${borderColor}40;
                transform: scale(1);
            }
        }
        
        @keyframes shimmer {
            0% { border-color: ${borderColor}; }
            50% { border-color: ${borderColor}80; }
            100% { border-color: ${borderColor}; }
        }

        @keyframes breathe {
            0%, 100% { opacity: 1; }
            50% { opacity: 0.7; }
        }
    `;

    const fontSizes = getFontSizes();
    const cornerSize = getCornerSize();
    const spacing = getSpacing();

    // Don't render if dimensions aren't calculated yet or container is too small
    if (overlayDimensions.width === 0 || overlayDimensions.height === 0 ||
        !containerWidth || !containerHeight ||
        containerWidth < 200 || containerHeight < 150) {
        return null;
    }

    return (
        <>
            <style>{pulseKeyframes}</style>
            <div
                ref={overlayRef}
                style={{
                    position: 'absolute',
                    top: '50%',
                    left: '50%',
                    transform: 'translate(-50%, -50%)',
                    width: overlayDimensions.width,
                    height: overlayDimensions.height,
                    pointerEvents: 'none',
                    zIndex: 10,
                    transition: 'all 0.3s ease-out',
                    // Ensure overlay never exceeds container
                    maxWidth: `${containerWidth - 10}px`,
                    maxHeight: `${containerHeight - 10}px`
                }}
            >
                {/* Main document frame with proper aspect ratio */}
                <div style={{
                    position: 'relative',
                    width: '100%',
                    height: '100%',
                    border: `${isMobile ? '2px' : '3px'} ${isCapturing ? 'solid' : 'dashed'} ${borderColor}`,
                    borderRadius: config.cornerRadius,
                    backgroundColor: isCapturing
                        ? `${config.capturingColor}15`
                        : `${config.borderColor}08`,
                    transition: 'all 0.3s ease',
                    animation: isCapturing ? 'pulse 1.5s infinite' : 'shimmer 3s ease-in-out infinite',
                    boxSizing: 'border-box'
                }}>

                    {/* Enhanced corner guides with responsive sizing */}
                    {['tl', 'tr', 'bl', 'br'].map((position) => {
                        const positions = {
                            tl: { top: -2, left: -2 },
                            tr: { top: -2, right: -2 },
                            bl: { bottom: -2, left: -2 },
                            br: { bottom: -2, right: -2 }
                        };

                        const borders = {
                            tl: { borderRight: 'none', borderBottom: 'none' },
                            tr: { borderLeft: 'none', borderBottom: 'none' },
                            bl: { borderRight: 'none', borderTop: 'none' },
                            br: { borderLeft: 'none', borderTop: 'none' }
                        };

                        return (
                            <div
                                key={position}
                                style={{
                                    position: 'absolute',
                                    ...positions[position as keyof typeof positions],
                                    width: cornerSize,
                                    height: cornerSize,
                                    border: `${isMobile ? '2px' : '3px'} solid ${borderColor}`,
                                    borderRadius: 2,
                                    ...borders[position as keyof typeof borders],
                                    transition: 'all 0.3s ease',
                                    animation: 'breathe 2s ease-in-out infinite'
                                }}
                            />
                        );
                    })}

                    {/* Center crosshair for precise alignment */}
                    <div style={{
                        position: 'absolute',
                        top: '50%',
                        left: '50%',
                        transform: 'translate(-50%, -50%)',
                        width: 1,
                        height: '35%',
                        backgroundColor: borderColor,
                        opacity: 0.3,
                        transition: 'opacity 0.3s ease'
                    }} />
                    <div style={{
                        position: 'absolute',
                        top: '50%',
                        left: '50%',
                        transform: 'translate(-50%, -50%)',
                        width: '35%',
                        height: 1,
                        backgroundColor: borderColor,
                        opacity: 0.3,
                        transition: 'opacity 0.3s ease'
                    }} />

                    {/* Document type header with responsive sizing */}
                    <div style={{
                        position: 'absolute',
                        top: spacing.headerTop,
                        left: '50%',
                        transform: 'translateX(-50%)',
                        backgroundColor: borderColor,
                        color: 'white',
                        padding: isMobile ? '3px 8px' : '4px 12px',
                        borderRadius: isMobile ? 3 : 4,
                        fontSize: `${fontSizes.header}px`,
                        fontWeight: 'bold',
                        whiteSpace: 'nowrap',
                        boxShadow: '0 1px 4px rgba(0,0,0,0.15)',
                        display: 'flex',
                        alignItems: 'center',
                        gap: isMobile ? 3 : 4,
                        transition: 'all 0.3s ease',
                        maxWidth: `${overlayDimensions.width + 20}px`,
                        overflow: 'hidden'
                    }}>
                        <span style={{ fontSize: isMobile ? '10px' : '12px' }}>{config.icon}</span>
                        <span style={{
                            overflow: 'hidden',
                            textOverflow: 'ellipsis',
                            fontSize: isMobile && overlayDimensions.width < 300 ? '10px' : 'inherit'
                        }}>
                            {isCapturing ? '📸 Processing...' : `Place ${config.title} within the frame.`}
                        </span>
                    </div>

                    {/* Quality score indicator with responsive positioning */}
                    {showQualityIndicators && (
                        <div style={{
                            position: 'absolute',
                            top: spacing.qualityTop,
                            right: -5,
                            backgroundColor: getQualityColor(qualityScore),
                            color: 'white',
                            padding: isMobile ? '1px 5px' : '2px 6px',
                            borderRadius: isMobile ? 6 : 8,
                            fontSize: `${fontSizes.quality}px`,
                            fontWeight: 'bold',
                            transition: 'all 0.3s ease'
                        }}>
                            {qualityScore}%
                        </div>
                    )}
                </div>

                {/* Enhanced quality indicators with responsive layout - hide on very small overlays */}
                {showQualityIndicators && overlayDimensions.width > 280 && (
                    <div style={{
                        position: 'absolute',
                        top: spacing.indicatorsTop,
                        left: 0,
                        display: 'flex',
                        gap: isMobile ? 3 : 4,
                        alignItems: 'center',
                        flexWrap: 'nowrap'
                    }}>
                        {[
                            { label: 'Light', score: Math.min(100, qualityScore + 5) },
                            { label: 'Focus', score: qualityScore },
                            { label: 'Angle', score: Math.min(100, qualityScore + 2) }
                        ].map((indicator, index) => (
                            <div key={index} style={{
                                display: 'flex',
                                flexDirection: 'column',
                                alignItems: 'center',
                                gap: 1
                            }}>
                                <div style={{
                                    width: isMobile ? 6 : 8,
                                    height: isMobile ? 6 : 8,
                                    borderRadius: '50%',
                                    backgroundColor: getQualityColor(indicator.score),
                                    border: `1px solid white`,
                                    boxShadow: '0 1px 2px rgba(0,0,0,0.2)',
                                    transition: 'all 0.3s ease'
                                }} />
                                <Text style={{
                                    fontSize: `${fontSizes.indicator}px`,
                                    color: getQualityColor(indicator.score),
                                    fontWeight: 'bold'
                                }}>
                                    {indicator.label}
                                </Text>
                            </div>
                        ))}
                    </div>
                )}
            </div>
        </>
    );
};

export default DocumentOverlay;