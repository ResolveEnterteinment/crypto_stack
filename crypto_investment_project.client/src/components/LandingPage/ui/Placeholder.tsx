// src/components/ui/Placeholder.tsx
import React from 'react';

interface PlaceholderProps {
    width: number;
    height: number;
    text?: string;
    type?: 'crypto' | 'chart' | 'user' | 'generic';
    className?: string;
}

const Placeholder: React.FC<PlaceholderProps> = ({
    width,
    height,
    text,
    type = 'generic',
    className = ''
}) => {
    // Generate a deterministic color based on the text or type
    const getColor = (seed: string) => {
        let hash = 0;
        for (let i = 0; i < seed.length; i++) {
            hash = seed.charCodeAt(i) + ((hash << 5) - hash);
        }

        const hue = Math.abs(hash % 360);
        const saturation = 60 + Math.abs(hash % 20); // Between 60-80%
        const lightness = 65 + Math.abs((hash >> 4) % 10); // Between 65-75%

        return `hsl(${hue}, ${saturation}%, ${lightness}%)`;
    };

    // Specific placeholder patterns based on type
    const renderPlaceholderContent = () => {
        switch (type) {
            case 'crypto':
                return (
                    <g>
                        <circle cx={width / 2} cy={height / 2} r={Math.min(width, height) * 0.3}
                            fill={getColor(`crypto-${text}`)} />
                        <path d={`M${width / 2 - 15},${height / 2} l15,-15 l15,15 l-15,15 z`}
                            fill="white" fillOpacity="0.7" />
                        <path d={`M${width / 2 - 10},${height / 2 - 5} l10,-10 l10,10 l-10,10 z`}
                            fill="white" fillOpacity="0.9" />
                    </g>
                );

            case 'chart':
                return (
                    <g>
                        {/* Background */}
                        <rect width={width} height={height} fill={getColor(`chart-${text}`)} fillOpacity="0.2" />

                        {/* Grid lines */}
                        {Array.from({ length: 5 }).map((_, i) => (
                            <line key={`h-${i}`}
                                x1="0" y1={height * (i + 1) / 6}
                                x2={width} y2={height * (i + 1) / 6}
                                stroke="#FFF" strokeOpacity="0.3" strokeWidth="1" />
                        ))}

                        {Array.from({ length: 3 }).map((_, i) => (
                            <line key={`v-${i}`}
                                x1={width * (i + 1) / 4} y1="0"
                                x2={width * (i + 1) / 4} y2={height}
                                stroke="#FFF" strokeOpacity="0.3" strokeWidth="1" />
                        ))}

                        {/* Chart line */}
                        <path
                            d={`M0,${height * 0.7} 
                 C${width * 0.2},${height * 0.8} 
                  ${width * 0.3},${height * 0.4} 
                  ${width * 0.5},${height * 0.5} 
                  S${width * 0.7},${height * 0.3} 
                  ${width * 0.8},${height * 0.25} 
                  ${width},${height * 0.4}`}
                            fill="none"
                            stroke={getColor(`chart-line-${text}`)}
                            strokeWidth="3"
                            strokeLinecap="round"
                        />
                    </g>
                );

            case 'user':
                return (
                    <g>
                        <circle cx={width / 2} cy={height / 2} r={Math.min(width, height) * 0.4}
                            fill={getColor(`user-${text}`)} />
                        <circle cx={width / 2} cy={height * 0.4} r={Math.min(width, height) * 0.15}
                            fill="white" fillOpacity="0.8" />
                        <path d={`M${width / 2 - Math.min(width, height) * 0.2},${height * 0.6} 
                      a${Math.min(width, height) * 0.2},${Math.min(width, height) * 0.15} 0 0,0 
                      ${Math.min(width, height) * 0.4},0`}
                            fill="white" fillOpacity="0.8" />
                    </g>
                );

            case 'generic':
            default:
                return (
                    <g>
                        <rect width={width} height={height} fill={getColor(`generic-${text}`)} fillOpacity="0.3" />
                        <rect x={width * 0.1} y={height * 0.1} width={width * 0.8} height={height * 0.3}
                            fill="white" fillOpacity="0.4" rx="4" />
                        <rect x={width * 0.1} y={height * 0.5} width={width * 0.6} height={height * 0.08}
                            fill="white" fillOpacity="0.4" rx="2" />
                        <rect x={width * 0.1} y={height * 0.65} width={width * 0.8} height={height * 0.06}
                            fill="white" fillOpacity="0.3" rx="2" />
                        <rect x={width * 0.1} y={height * 0.75} width={width * 0.5} height={height * 0.06}
                            fill="white" fillOpacity="0.3" rx="2" />
                    </g>
                );
        }
    };

    return (
        <div className={`placeholder-component rounded overflow-hidden ${className}`} style={{ width, height }}>
            <svg width={width} height={height} viewBox={`0 0 ${width} ${height}`} xmlns="http://www.w3.org/2000/svg">
                {/* Base background */}
                <rect width={width} height={height} fill="#f0f0f0" />

                {/* Custom content based on type */}
                {renderPlaceholderContent()}

                {/* Optional text */}
                {text && (
                    <text
                        x="50%"
                        y="50%"
                        fontFamily="Arial, sans-serif"
                        fontSize={`${Math.min(width, height) * 0.1}px`}
                        textAnchor="middle"
                        dominantBaseline="middle"
                        fill="rgba(0,0,0,0.7)"
                    >
                        {text}
                    </text>
                )}
            </svg>
        </div>
    );
};

export default Placeholder;