import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import Button from './Button';

const HeroSection = () => {
    const navigate = useNavigate();
    const [isVisible, setIsVisible] = useState(false);
    const [animatedValue, setAnimatedValue] = useState(0);

    // Charts data for visualization
    const chartData = [
        { month: 'Jan', BTC: 38, ETH: 27, USDT: 23 },
        { month: 'Feb', BTC: 42, ETH: 30, USDT: 24 },
        { month: 'Mar', BTC: 40, ETH: 35, USDT: 25 },
        { month: 'Apr', BTC: 45, ETH: 32, USDT: 26 },
        { month: 'May', BTC: 48, ETH: 36, USDT: 27 },
        { month: 'Jun', BTC: 52, ETH: 41, USDT: 28 },
        { month: 'Jul', BTC: 58, ETH: 46, USDT: 29 },
    ];

    // Crypto price data for animation
    const priceTickers = [
        { name: 'Bitcoin', ticker: 'BTC', price: 56842.31, change: '+2.4%', isPositive: true },
        { name: 'Ethereum', ticker: 'ETH', price: 3482.15, change: '+1.8%', isPositive: true },
        { name: 'Solana', ticker: 'SOL', price: 124.87, change: '+5.3%', isPositive: true },
        { name: 'Cardano', ticker: 'ADA', price: 0.58, change: '-0.7%', isPositive: false },
        { name: 'Avalanche', ticker: 'AVAX', price: 36.24, change: '+3.1%', isPositive: true },
    ];

    // Animation for numerical values
    useEffect(() => {
        setIsVisible(true);

        const interval = setInterval(() => {
            setAnimatedValue(prev => {
                if (prev < 100) return prev + 1;
                clearInterval(interval);
                return 100;
            });
        }, 20);

        return () => clearInterval(interval);
    }, []);

    // Get started button handler
    const handleGetStarted = () => {
        navigate('/auth/register');
    };

    // Animate chart bars with delay
    const getBarHeight = (index, value) => {
        if (!isVisible) return 0;
        const delay = index * 100; // ms delay per bar
        const animationDuration = 1000; // total animation duration
        const elapsed = Math.max(0, animatedValue * 10 - delay);
        const progress = Math.min(1, elapsed / animationDuration);

        return value * progress;
    };

    return (
        <div className="relative overflow-hidden bg-gradient-to-br from-blue-900 via-indigo-900 to-purple-900 text-white">
            {/* Background elements */}
            <div className="absolute inset-0 opacity-10">
                <div className="absolute top-0 right-0 w-1/2 h-1/2">
                    <svg width="100%" height="100%" viewBox="0 0 400 400" xmlns="http://www.w3.org/2000/svg">
                        <circle cx="200" cy="200" r="150" stroke="white" strokeWidth="2" fill="none" className="animate-pulse-slow" />
                        <circle cx="200" cy="200" r="100" stroke="white" strokeWidth="2" fill="none" className="animate-pulse-slow" style={{ animationDelay: '1s' }} />
                        <circle cx="200" cy="200" r="50" stroke="white" strokeWidth="2" fill="none" className="animate-pulse-slow" style={{ animationDelay: '2s' }} />
                    </svg>
                </div>

                <div className="absolute bottom-0 left-0 w-1/2 h-1/2">
                    <svg width="100%" height="100%" viewBox="0 0 400 400" xmlns="http://www.w3.org/2000/svg">
                        <polygon points="0,400 400,400 0,0" fill="white" fillOpacity="0.05" className="animate-pulse-slow" />
                        <polygon points="100,400 400,400 100,100" fill="white" fillOpacity="0.05" className="animate-pulse-slow" style={{ animationDelay: '1.5s' }} />
                    </svg>
                </div>

                {/* Grid pattern */}
                <div className="absolute inset-0"
                    style={{
                        backgroundImage: 'radial-gradient(circle, rgba(255,255,255,0.1) 1px, transparent 1px)',
                        backgroundSize: '30px 30px'
                    }}>
                </div>

                {/* Animated gradient lines */}
                <div className="absolute h-px w-full top-1/4 bg-gradient-to-r from-transparent via-blue-300 to-transparent animate-pulse"></div>
                <div className="absolute h-px w-full top-2/4 bg-gradient-to-r from-transparent via-purple-300 to-transparent animate-pulse" style={{ animationDelay: '1s' }}></div>
                <div className="absolute h-px w-full top-3/4 bg-gradient-to-r from-transparent via-indigo-300 to-transparent animate-pulse" style={{ animationDelay: '2s' }}></div>
            </div>

            {/* Content area */}
            <div className="container mx-auto px-4 py-24 md:py-32 relative z-10">
                <div className="flex flex-col md:flex-row items-center md:justify-between">
                    <div className="md:w-1/2 mb-12 md:mb-0 animate-slide-in-left">
                        <h1 className="text-4xl md:text-5xl lg:text-6xl font-bold mb-6 leading-tight">
                            <span className="block">Smart</span>
                            <span className="text-gradient text-gradient-blue">Crypto Investing</span>
                            <span className="block">on Autopilot</span>
                        </h1>
                        <p className="text-xl md:text-2xl text-gray-300 mb-8">
                            Automate your crypto investments with scheduled, diversified buys.
                            Build your portfolio effortlessly.
                        </p>
                        <div className="flex flex-col sm:flex-row gap-4">
                            <Button
                                variant="gradient"
                                size="lg"
                                onClick={handleGetStarted}
                                className="animate-pulse-glow"
                                icon={
                                    <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M13 10V3L4 14h7v7l9-11h-7z" />
                                    </svg>
                                }
                            >
                                Get Started
                            </Button>
                            <Button
                                variant="outline"
                                size="lg"
                                onClick={() => navigate('/learn')}
                                className="text-white border-white hover:bg-white hover:bg-opacity-10"
                            >
                                Learn More
                            </Button>
                        </div>

                        {/* Trust indicators */}
                        <div className="mt-12 flex flex-col sm:flex-row sm:items-center text-gray-300 gap-4">
                            <div className="flex items-center">
                                <svg className="w-5 h-5 mr-2 text-green-400" fill="currentColor" viewBox="0 0 20 20">
                                    <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clipRule="evenodd"></path>
                                </svg>
                                <span>Military-grade encryption</span>
                            </div>

                            <div className="flex items-center">
                                <svg className="w-5 h-5 mr-2 text-green-400" fill="currentColor" viewBox="0 0 20 20">
                                    <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clipRule="evenodd"></path>
                                </svg>
                                <span>Regulated & Compliant</span>
                            </div>

                            <div className="flex items-center">
                                <svg className="w-5 h-5 mr-2 text-green-400" fill="currentColor" viewBox="0 0 20 20">
                                    <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clipRule="evenodd"></path>
                                </svg>
                                <span>24/7 customer support</span>
                            </div>
                        </div>
                    </div>

                    {/* Hero image / Chart visualization */}
                    <div className="md:w-1/2 animate-fade-in" style={{ animationDelay: '0.5s' }}>
                        <div className="bg-gray-900 bg-opacity-60 backdrop-filter backdrop-blur-xl p-6 rounded-xl border border-gray-700 shadow-2xl transform hover:scale-[1.02] transition-transform">
                            <div className="flex justify-between items-center mb-6">
                                <h3 className="text-xl font-semibold">Portfolio Performance</h3>
                                <div className="flex space-x-3">
                                    <div className="flex items-center">
                                        <div className="w-3 h-3 bg-blue-500 rounded-full mr-2"></div>
                                        <span className="text-sm">BTC</span>
                                    </div>
                                    <div className="flex items-center">
                                        <div className="w-3 h-3 bg-purple-500 rounded-full mr-2"></div>
                                        <span className="text-sm">ETH</span>
                                    </div>
                                    <div className="flex items-center">
                                        <div className="w-3 h-3 bg-green-500 rounded-full mr-2"></div>
                                        <span className="text-sm">USDT</span>
                                    </div>
                                </div>
                            </div>

                            {/* Chart visualization */}
                            <div className="h-64 relative">
                                <div className="absolute inset-0 flex items-end">
                                    {chartData.map((entry, index) => (
                                        <div key={entry.month} className="flex-1 flex flex-col items-center" style={{ height: '100%' }}>
                                            <div className="relative w-full h-full flex flex-col justify-end items-center">
                                                {/* USDT bar */}
                                                <div
                                                    className="w-5 bg-green-500 mb-1 rounded-t opacity-80 transition-all duration-1000"
                                                    style={{ height: `${getBarHeight(index * 3, entry.USDT * 2)}px` }}
                                                ></div>

                                                {/* ETH bar */}
                                                <div
                                                    className="w-5 bg-purple-500 mb-1 rounded-t opacity-80 transition-all duration-1000"
                                                    style={{ height: `${getBarHeight(index * 3 + 1, entry.ETH * 2)}px` }}
                                                ></div>

                                                {/* BTC bar */}
                                                <div
                                                    className="w-5 bg-blue-500 rounded-t opacity-80 transition-all duration-1000"
                                                    style={{ height: `${getBarHeight(index * 3 + 2, entry.BTC * 2)}px` }}
                                                ></div>
                                            </div>
                                            <div className="text-xs mt-2">{entry.month}</div>
                                        </div>
                                    ))}
                                </div>
                            </div>

                            {/* Key metrics */}
                            <div className="flex justify-between mt-6 pt-6 border-t border-gray-700">
                                <div>
                                    <p className="text-gray-400 text-sm">Total Value</p>
                                    <p className="text-xl font-semibold">${(12456.78 * animatedValue / 100).toFixed(2)}</p>
                                </div>
                                <div>
                                    <p className="text-gray-400 text-sm">7d Change</p>
                                    <p className="text-xl font-semibold text-green-500">+{(15.4 * animatedValue / 100).toFixed(1)}%</p>
                                </div>
                                <div>
                                    <p className="text-gray-400 text-sm">Automated Buys</p>
                                    <p className="text-xl font-semibold">{Math.floor(24 * animatedValue / 100)}</p>
                                </div>
                            </div>

                            {/* Live crypto ticker */}
                            <div className="mt-6 pt-4 border-t border-gray-700">
                                <p className="text-sm text-gray-400 mb-2">Live Market Prices</p>
                                <div className="space-y-2">
                                    {priceTickers.map((coin, index) => (
                                        <div
                                            key={coin.ticker}
                                            className="flex justify-between items-center p-2 rounded-lg bg-gray-800 bg-opacity-50 animate-slide-in-right"
                                            style={{ animationDelay: `${index * 150}ms` }}
                                        >
                                            <div className="flex items-center">
                                                <div className="w-8 h-8 rounded-full bg-gradient-to-br from-gray-700 to-gray-900 flex items-center justify-center mr-3">
                                                    <span className="text-xs font-bold">{coin.ticker}</span>
                                                </div>
                                                <span>{coin.name}</span>
                                            </div>
                                            <div className="text-right">
                                                <p className="font-medium">${coin.price.toLocaleString()}</p>
                                                <p className={`text-xs ${coin.isPositive ? 'text-green-400' : 'text-red-400'}`}>
                                                    {coin.change}
                                                </p>
                                            </div>
                                        </div>
                                    ))}
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>

            {/* Slanted edge for visual interest */}
            <div className="h-16 bg-gray-50 transform -skew-y-3 origin-top-right"></div>
        </div>
    );
};

export default HeroSection;