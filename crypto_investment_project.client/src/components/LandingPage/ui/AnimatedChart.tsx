// src/components/ui/AnimatedChart.tsx
import React, { useState, useEffect, useRef } from 'react';

interface ChartDataPoint {
    month: string;
    BTC: number;
    ETH: number;
    USDT: number;
    [key: string]: string | number;
}

interface AnimatedChartProps {
    data: ChartDataPoint[];
    height: number;
    categories: string[];
    colors: Record<string, string>;
    animationDuration?: number;
    className?: string;
}

const AnimatedChart: React.FC<AnimatedChartProps> = ({
    data,
    height,
    categories,
    colors,
    animationDuration = 1200,
    className = ''
}) => {
    const [animationProgress, setAnimationProgress] = useState(0);
    const [isVisible, setIsVisible] = useState(false);
    const chartRef = useRef<HTMLDivElement>(null);

    // Determine when chart comes into view for animation
    useEffect(() => {
        const observer = new IntersectionObserver(
            (entries) => {
                if (entries[0].isIntersecting) {
                    setIsVisible(true);
                    observer.disconnect();
                }
            },
            { threshold: 0.1 }
        );

        if (chartRef.current) {
            observer.observe(chartRef.current);
        }

        return () => observer.disconnect();
    }, []);

    // Control animation when visible
    useEffect(() => {
        if (!isVisible) return;

        let start: number | null = null;
        let animationFrameId: number;

        const animate = (timestamp: number) => {
            if (!start) start = timestamp;

            const elapsed = timestamp - start;
            const progress = Math.min(elapsed / animationDuration, 1);

            setAnimationProgress(progress);

            if (progress < 1) {
                animationFrameId = requestAnimationFrame(animate);
            }
        };

        animationFrameId = requestAnimationFrame(animate);

        return () => cancelAnimationFrame(animationFrameId);
    }, [isVisible, animationDuration]);

    // Get maximum value for scaling
    const maxValue = Math.max(...data.flatMap(entry =>
        categories.map(cat => Number(entry[cat]))
    ));

    // Calculate bar height with animation
    const getBarHeight = (value: number, categoryIndex: number) => {
        // Stagger animation based on category index
        const delay = categoryIndex * (animationDuration / (categories.length * 2));
        const adjustedProgress = Math.max(0, animationProgress - (delay / animationDuration));
        const cappedProgress = Math.min(1, adjustedProgress * (animationDuration / (animationDuration - delay)));

        // Ease-out function for smoother animation
        const easeOutProgress = 1 - Math.pow(1 - cappedProgress, 3);

        return (value / maxValue) * (height - 40) * easeOutProgress;
    };

    return (
        <div
            ref={chartRef}
            className={`animated-chart ${className}`}
            style={{ height: `${height}px` }}
        >
            <div className="flex h-full">
                {/* Y-axis labels */}
                <div className="flex flex-col justify-between text-xs text-gray-400 pr-2">
                    <span>{maxValue}</span>
                    <span>{Math.round(maxValue / 2)}</span>
                    <span>0</span>
                </div>

                {/* Chart content */}
                <div className="flex-1 flex items-end justify-between relative">
                    {/* Background grid lines */}
                    <div className="absolute inset-0 flex flex-col justify-between pointer-events-none">
                        <div className="border-t border-gray-700 opacity-30 h-0" />
                        <div className="border-t border-gray-700 opacity-30 h-0" />
                        <div className="border-t border-gray-700 opacity-30 h-0" />
                    </div>

                    {/* Bars */}
                    {data.map((entry, dataIndex) => (
                        <div key={entry.month} className="flex flex-col items-center">
                            <div className="flex h-full items-end space-x-1">
                                {categories.map((category, catIndex) => (
                                    <div
                                        key={`${entry.month}-${category}`}
                                        className="w-4 md:w-5 rounded-t transition-all duration-200"
                                        style={{
                                            height: `${getBarHeight(Number(entry[category]), catIndex)}px`,
                                            backgroundColor: colors[category],
                                            opacity: 0.85,
                                            transform: `translateY(${isVisible ? '0' : '10px'})`,
                                            transition: `height ${animationDuration}ms ease-out, transform 500ms ease-out ${catIndex * 100}ms`,
                                        }}
                                    />
                                ))}
                            </div>
                            <div className="text-xs mt-2">{entry.month}</div>
                        </div>
                    ))}
                </div>
            </div>

            {/* Legend */}
            <div className="flex justify-center mt-4 space-x-4">
                {categories.map(category => (
                    <div key={category} className="flex items-center">
                        <div
                            className="w-3 h-3 rounded-full mr-1"
                            style={{ backgroundColor: colors[category] }}
                        />
                        <span className="text-xs">{category}</span>
                    </div>
                ))}
            </div>
        </div>
    );
};

export default AnimatedChart;