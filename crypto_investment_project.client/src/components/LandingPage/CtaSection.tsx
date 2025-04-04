// src/components/LandingPage/CtaSection.tsx
import React, { useEffect, useState, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import Button from './Button';

interface CtaSectionProps {
    title?: string;
    description?: string;
    primaryButtonText?: string;
    secondaryButtonText?: string;
    primaryButtonLink?: string;
    secondaryButtonLink?: string;
    backgroundStyle?: 'gradient' | 'image' | 'pattern' | 'particles';
    theme?: 'light' | 'dark';
    showDemo?: boolean;
    className?: string;
    onPrimaryClick?: () => void;
    onSecondaryClick?: () => void;
}

const CtaSection: React.FC<CtaSectionProps> = ({
    title = "Ready to Start Your Crypto Journey?",
    description = "Join thousands of investors who are already building their crypto portfolio the smart way.",
    primaryButtonText = "Get Started",
    secondaryButtonText = "Learn More",
    primaryButtonLink = "/auth/register",
    secondaryButtonLink = "/learn",
    backgroundStyle = "gradient",
    theme = "dark",
    showDemo = true,
    className = "",
    onPrimaryClick,
    onSecondaryClick,
}) => {
    const navigate = useNavigate();
    const containerRef = useRef<HTMLDivElement>(null);
    const [isVisible, setIsVisible] = useState(false);
    const [mousePosition, setMousePosition] = useState({ x: 0, y: 0 });
    const particlesRef = useRef<HTMLCanvasElement>(null);

    // Check when section is visible
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

        if (containerRef.current) {
            observer.observe(containerRef.current);
        }

        return () => observer.disconnect();
    }, []);

    // Handle mouse movement for parallax effect
    useEffect(() => {
        const handleMouseMove = (e: MouseEvent) => {
            if (!containerRef.current) return;

            const { left, top, width, height } = containerRef.current.getBoundingClientRect();
            const x = ((e.clientX - left) / width) - 0.5;
            const y = ((e.clientY - top) / height) - 0.5;

            setMousePosition({ x, y });
        };

        window.addEventListener('mousemove', handleMouseMove);
        return () => window.removeEventListener('mousemove', handleMouseMove);
    }, []);

    // Particles animation
    useEffect(() => {
        if (backgroundStyle !== 'particles' || !particlesRef.current) return;

        const canvas = particlesRef.current;
        const ctx = canvas.getContext('2d');
        if (!ctx) return;

        // Set canvas size
        const setCanvasSize = () => {
            const rect = canvas.parentElement?.getBoundingClientRect() || { width: 0, height: 0 };
            canvas.width = rect.width;
            canvas.height = rect.height;
        };

        setCanvasSize();
        window.addEventListener('resize', setCanvasSize);

        // Particle system
        const particles: Array<{
            x: number;
            y: number;
            size: number;
            color: string;
            speed: number;
            vx: number;
            vy: number;
        }> = [];

        const colors = theme === 'dark'
            ? ['rgba(59, 130, 246, 0.6)', 'rgba(99, 102, 241, 0.6)', 'rgba(139, 92, 246, 0.6)']
            : ['rgba(59, 130, 246, 0.4)', 'rgba(99, 102, 241, 0.4)', 'rgba(139, 92, 246, 0.4)'];

        // Create particles
        for (let i = 0; i < 50; i++) {
            particles.push({
                x: Math.random() * canvas.width,
                y: Math.random() * canvas.height,
                size: Math.random() * 2 + 1,
                color: colors[Math.floor(Math.random() * colors.length)],
                speed: Math.random() * 0.2 + 0.1,
                vx: Math.random() * 0.2 - 0.1,
                vy: Math.random() * 0.2 - 0.1
            });
        }

        const animate = () => {
            if (!ctx) return;

            ctx.clearRect(0, 0, canvas.width, canvas.height);

            // Update and draw particles
            particles.forEach(particle => {
                // Update position
                particle.x += particle.vx;
                particle.y += particle.vy;

                // Bounce off edges
                if (particle.x < 0 || particle.x > canvas.width) {
                    particle.vx *= -1;
                }
                if (particle.y < 0 || particle.y > canvas.height) {
                    particle.vy *= -1;
                }

                // Draw particle
                ctx.beginPath();
                ctx.arc(particle.x, particle.y, particle.size, 0, Math.PI * 2);
                ctx.fillStyle = particle.color;
                ctx.fill();

                // Connect nearby particles with lines
                particles.forEach(otherParticle => {
                    const dx = particle.x - otherParticle.x;
                    const dy = particle.y - otherParticle.y;
                    const distance = Math.sqrt(dx * dx + dy * dy);

                    if (distance < 100) {
                        ctx.beginPath();
                        ctx.strokeStyle = particle.color;
                        ctx.lineWidth = 0.2;
                        ctx.moveTo(particle.x, particle.y);
                        ctx.lineTo(otherParticle.x, otherParticle.y);
                        ctx.stroke();
                    }
                });
            });

            requestAnimationFrame(animate);
        };

        const animationId = requestAnimationFrame(animate);

        return () => {
            cancelAnimationFrame(animationId);
            window.removeEventListener('resize', setCanvasSize);
        };
    }, [backgroundStyle, isVisible, theme]);

    // Button click handlers
    const handlePrimaryClick = () => {
        if (onPrimaryClick) {
            onPrimaryClick();
        } else {
            navigate(primaryButtonLink);
        }
    };

    const handleSecondaryClick = () => {
        if (onSecondaryClick) {
            onSecondaryClick();
        } else {
            navigate(secondaryButtonLink);
        }
    };

    // Background styles
    const getBackgroundStyle = () => {
        switch (backgroundStyle) {
            case 'gradient':
                return theme === 'dark'
                    ? 'bg-gradient-to-r from-blue-700 via-indigo-700 to-purple-700'
                    : 'bg-gradient-to-r from-blue-100 via-indigo-100 to-purple-100';
            case 'image':
                return 'bg-cover bg-center';
            case 'pattern':
                return theme === 'dark'
                    ? 'bg-gray-900'
                    : 'bg-gray-50';
            case 'particles':
                return theme === 'dark'
                    ? 'bg-gray-900'
                    : 'bg-white';
            default:
                return 'bg-gray-100';
        }
    };

    return (
        <div
            ref={containerRef}
            className={`cta-section relative overflow-hidden py-16 md:py-24 ${getBackgroundStyle()} ${theme === 'dark' ? 'text-white' : 'text-gray-900'
                } ${className}`}
        >
            {/* Background particles */}
            {backgroundStyle === 'particles' && (
                <canvas
                    ref={particlesRef}
                    className="absolute inset-0 w-full h-full"
                />
            )}

            {/* Pattern background */}
            {backgroundStyle === 'pattern' && (
                <div className="absolute inset-0 opacity-10">
                    <div className="absolute inset-0"
                        style={{
                            backgroundImage: theme === 'dark'
                                ? 'radial-gradient(circle, rgba(255,255,255,0.1) 1px, transparent 1px)'
                                : 'radial-gradient(circle, rgba(0,0,0,0.1) 1px, transparent 1px)',
                            backgroundSize: '20px 20px'
                        }}>
                    </div>

                    {/* Animated gradient lines */}
                    <div className="absolute h-px w-full top-1/4 bg-gradient-to-r from-transparent via-blue-300 to-transparent animate-pulse"></div>
                    <div className="absolute h-px w-full top-2/4 bg-gradient-to-r from-transparent via-purple-300 to-transparent animate-pulse" style={{ animationDelay: '1s' }}></div>
                    <div className="absolute h-px w-full top-3/4 bg-gradient-to-r from-transparent via-indigo-300 to-transparent animate-pulse" style={{ animationDelay: '2s' }}></div>
                </div>
            )}

            <div className="container mx-auto px-4 relative z-10">
                <div className="max-w-4xl mx-auto text-center">
                    {/* Heading with animation */}
                    <h2
                        className={`text-4xl md:text-5xl font-bold mb-6 transition-transform transform ${isVisible ? 'translate-y-0 opacity-100' : 'translate-y-10 opacity-0'
                            }`}
                        style={{
                            transitionDuration: '700ms',
                            transitionDelay: '100ms'
                        }}
                    >
                        {title}
                    </h2>

                    {/* Description with animation */}
                    <p
                        className={`text-xl ${theme === 'dark' ? 'text-blue-100' : 'text-gray-600'
                            } mb-10 max-w-2xl mx-auto transition-transform transform ${isVisible ? 'translate-y-0 opacity-100' : 'translate-y-10 opacity-0'
                            }`}
                        style={{
                            transitionDuration: '700ms',
                            transitionDelay: '300ms'
                        }}
                    >
                        {description}
                    </p>

                    {/* Action buttons with animation */}
                    <div
                        className={`flex flex-col sm:flex-row justify-center gap-4 mb-12 transition-transform transform ${isVisible ? 'translate-y-0 opacity-100' : 'translate-y-10 opacity-0'
                            }`}
                        style={{
                            transitionDuration: '700ms',
                            transitionDelay: '500ms'
                        }}
                    >
                        <Button
                            variant={theme === 'dark' ? 'primary' : 'gradient'}
                            size="lg"
                            onClick={handlePrimaryClick}
                            className="animate-pulse-glow"
                            icon={
                                <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M13 10V3L4 14h7v7l9-11h-7z" />
                                </svg>
                            }
                        >
                            {primaryButtonText}
                        </Button>
                        <Button
                            variant={theme === 'dark' ? 'outline' : 'secondary'}
                            size="lg"
                            onClick={handleSecondaryClick}
                            className={theme === 'dark' ? 'text-white border-white hover:bg-white hover:bg-opacity-10' : ''}
                        >
                            {secondaryButtonText}
                        </Button>
                    </div>

                    {/* Animated demo section */}
                    {showDemo && (
                        <div
                            className={`relative aspect-video max-w-5xl mx-auto transition-transform transform ${isVisible ? 'translate-y-0 opacity-100' : 'translate-y-20 opacity-0'
                                }`}
                            style={{
                                transitionDuration: '900ms',
                                transitionDelay: '700ms',
                                transform: `perspective(1000px) rotateX(${mousePosition.y * 5}deg) rotateY(${mousePosition.x * -5}deg)`
                            }}
                        >
                            {/* Dashboard mockup */}
                            <div
                                className="bg-gray-900 rounded-t-xl shadow-2xl border border-gray-700 w-full h-full"
                                style={{
                                    boxShadow: '0 20px 25px -5px rgba(0, 0, 0, 0.3), 0 10px 10px -5px rgba(0, 0, 0, 0.04)',
                                }}
                            >
                                {/* Browser-like top bar */}
                                <div className="bg-gray-800 rounded-t-xl px-4 py-3 flex items-center border-b border-gray-700">
                                    <div className="flex space-x-2">
                                        <div className="w-3 h-3 bg-red-500 rounded-full"></div>
                                        <div className="w-3 h-3 bg-yellow-500 rounded-full"></div>
                                        <div className="w-3 h-3 bg-green-500 rounded-full"></div>
                                    </div>
                                    <div className="mx-auto bg-gray-700 rounded-full px-4 py-1 text-xs text-gray-300 max-w-sm truncate">
                                        secure.cryptoinvestments.app/dashboard
                                    </div>
                                </div>

                                {/* Dashboard content */}
                                <div className="p-6">
                                    <div className="flex justify-between items-center mb-6">
                                        <div>
                                            <h3 className="text-xl font-bold text-white">My Portfolio</h3>
                                            <p className="text-green-400 text-sm">+12.8% this month</p>
                                        </div>
                                        <div className="flex space-x-2">
                                            <button className="bg-blue-600 px-3 py-1 rounded text-sm">Buy</button>
                                            <button className="bg-gray-700 px-3 py-1 rounded text-sm">Sell</button>
                                        </div>
                                    </div>

                                    {/* Chart representation */}
                                    <div className="h-24 mb-6 flex items-end">
                                        <div className="flex-1 h-16 bg-gradient-to-t from-blue-400 to-blue-600 opacity-80 rounded-sm"></div>
                                        <div className="flex-1 h-20 bg-gradient-to-t from-blue-400 to-blue-600 opacity-80 rounded-sm"></div>
                                        <div className="flex-1 h-14 bg-gradient-to-t from-blue-400 to-blue-600 opacity-80 rounded-sm"></div>
                                        <div className="flex-1 h-18 bg-gradient-to-t from-blue-400 to-blue-600 opacity-80 rounded-sm"></div>
                                        <div className="flex-1 h-22 bg-gradient-to-t from-blue-400 to-blue-600 opacity-80 rounded-sm"></div>
                                        <div className="flex-1 h-16 bg-gradient-to-t from-blue-400 to-blue-600 opacity-80 rounded-sm"></div>
                                        <div className="flex-1 h-12 bg-gradient-to-t from-blue-400 to-blue-600 opacity-80 rounded-sm"></div>
                                    </div>

                                    {/* Assets list */}
                                    <div className="space-y-3">
                                        {/* Bitcoin row */}
                                        <div className="bg-gray-800 p-3 rounded-lg flex justify-between items-center">
                                            <div className="flex items-center">
                                                <div className="w-8 h-8 rounded-full bg-orange-500 flex items-center justify-center text-white font-bold mr-3">₿</div>
                                                <div>
                                                    <p className="font-medium text-white">Bitcoin</p>
                                                    <p className="text-xs text-gray-400">0.36 BTC</p>
                                                </div>
                                            </div>
                                            <div className="text-right">
                                                <p className="font-medium text-white">$14,328</p>
                                                <p className="text-xs text-green-400">+4.32%</p>
                                            </div>
                                        </div>

                                        {/* Ethereum row */}
                                        <div className="bg-gray-800 p-3 rounded-lg flex justify-between items-center">
                                            <div className="flex items-center">
                                                <div className="w-8 h-8 rounded-full bg-purple-500 flex items-center justify-center text-white font-bold mr-3">Ξ</div>
                                                <div>
                                                    <p className="font-medium text-white">Ethereum</p>
                                                    <p className="text-xs text-gray-400">2.5 ETH</p>
                                                </div>
                                            </div>
                                            <div className="text-right">
                                                <p className="font-medium text-white">$6,245</p>
                                                <p className="text-xs text-green-400">+7.82%</p>
                                            </div>
                                        </div>
                                    </div>
                                </div>
                            </div>

                            {/* Phone mockup */}
                            <div className="absolute -bottom-20 -right-8 md:block hidden w-64 h-96 bg-gray-900 rounded-3xl shadow-xl border-4 border-gray-800 transform rotate-6">
                                {/* Phone notch */}
                                <div className="absolute top-0 left-1/2 transform -translate-x-1/2 w-24 h-6 bg-gray-800 rounded-b-xl"></div>

                                {/* Phone content */}
                                <div className="mt-8 p-4">
                                    {/* Mobile header */}
                                    <div className="mb-4">
                                        <h4 className="text-white text-sm font-bold">Portfolio Value</h4>
                                        <p className="text-lg font-bold text-white">$21,345.67</p>
                                    </div>

                                    {/* Mini chart */}
                                    <div className="h-20 bg-gray-800 rounded-lg mb-4"></div>

                                    {/* Mobile coins list */}
                                    <div className="space-y-2">
                                        <div className="bg-gray-800 p-2 rounded flex justify-between items-center">
                                            <span className="text-xs text-white">BTC</span>
                                            <span className="text-xs text-white">$14,328</span>
                                        </div>
                                        <div className="bg-gray-800 p-2 rounded flex justify-between items-center">
                                            <span className="text-xs text-white">ETH</span>
                                            <span className="text-xs text-white">$6,245</span>
                                        </div>
                                        <div className="bg-gray-800 p-2 rounded flex justify-between items-center">
                                            <span className="text-xs text-white">SOL</span>
                                            <span className="text-xs text-white">$772</span>
                                        </div>
                                    </div>
                                </div>
                            </div>
                        </div>
                    )}
                </div>
            </div>
        </div>
    );
};

export default CtaSection;