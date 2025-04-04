// src/components/LandingPage/CryptoTicker.tsx
import React, { useState, useEffect } from 'react';

interface CryptoCoin {
    name: string;
    ticker: string;
    price: number;
    change: string;
    isPositive: boolean;
    icon?: React.ReactNode;
}

interface CryptoTickerProps {
    coins: CryptoCoin[];
    autoScroll?: boolean;
    scrollSpeed?: number;
    className?: string;
    dark?: boolean;
}

const CryptoTicker: React.FC<CryptoTickerProps> = ({
    coins,
    autoScroll = true,
    scrollSpeed = 20,
    className = '',
    dark = true
}) => {
    const [visibleCoins, setVisibleCoins] = useState<CryptoCoin[]>(coins);
    const [isHovered, setIsHovered] = useState(false);

    // Icons for popular coins
    const getCoinIcon = (ticker: string) => {
        switch (ticker.toUpperCase()) {
            case 'BTC':
                return (
                    <svg viewBox="0 0 24 24" width="1em" height="1em" fill="currentColor" className="text-orange-500">
                        <path d="M12 0C5.376 0 0 5.376 0 12s5.376 12 12 12 12-5.376 12-12S18.624 0 12 0zm2.724 16.26c-.324.84-1.152 1.26-2.46 1.26h-4.8v-1.62h.36v-6.9h-.36V7.38h4.98c1.272 0 2.088.42 2.448 1.248.156.372.228.78.228 1.224 0 1.368-.66 2.148-1.98 2.328v.036c1.56.192 2.304.996 2.304 2.436 0 .588-.108 1.116-.324 1.608h-.036.036zM10.164 9h2.436c.72 0 1.08-.36 1.08-1.08 0-.72-.36-1.08-1.08-1.08h-2.436v2.16zm0 4.5h2.556c.828 0 1.24-.384 1.24-1.152 0-.768-.412-1.152-1.24-1.152h-2.556V13.5z" />
                    </svg>
                );
            case 'ETH':
                return (
                    <svg viewBox="0 0 24 24" width="1em" height="1em" fill="currentColor" className="text-purple-500">
                        <path d="M11.944 17.97L4.58 13.62 11.943 24l7.37-10.38-7.372 4.35h.003zM12.056 0L4.69 12.223l7.365 4.354 7.365-4.35L12.056 0z" />
                    </svg>
                );
            case 'SOL':
                return (
                    <svg viewBox="0 0 24 24" width="1em" height="1em" fill="currentColor" className="text-green-500">
                        <path d="M17.731 10.686l-2.34 2.34-9.201-9.201c-.308-.308-.765-.392-1.158-.217-.393.17-.648.558-.648.985v18.015c0 .431.259.82.648.99.393.175.85.09 1.158-.218l9.201-9.202 2.34 2.34c.308.308.807.308 1.115 0l1.721-1.721c.308-.308.308-.807 0-1.115l-2.34-2.34 2.34-2.34c.308-.308.308-.807 0-1.115l-1.721-1.721c-.308-.308-.807-.308-1.115 0zM4.007 16.829v-9.657l6.339 4.828-6.339 4.829z" />
                    </svg>
                );
            case 'DOGE':
                return (
                    <svg viewBox="0 0 24 24" width="1em" height="1em" fill="currentColor" className="text-yellow-500">
                        <path d="M6 12c0 2.22.892 4.177 2.31 5.293l1.718-1.717-.001.001A4.1 4.1 0 0 1 8 12a4.1 4.1 0 0 1 2.026-3.578l-1.715-1.716A7.11 7.11 0 0 0 6 12zm12 0c0-2.22-.892-4.177-2.31-5.293l-1.718 1.717A4.1 4.1 0 0 1 16 12a4.1 4.1 0 0 1-2.026 3.578l1.715 1.716A7.11 7.11 0 0 0 18 12zm-6-6a6 6 0 1 0 0 12 6 6 0 0 0 0-12z" />
                    </svg>
                );
            case 'ADA':
                return (
                    <svg viewBox="0 0 24 24" width="1em" height="1em" fill="currentColor" className="text-blue-500">
                        <path d="M12 0C5.373 0 0 5.373 0 12s5.373 12 12 12 12-5.373 12-12S18.627 0 12 0zm-.041 4.008c.78-.783 2.048-.783 2.828 0 .783.78.783 2.048 0 2.828-.78.783-2.048.783-2.828 0-.783-.78-.783-2.048 0-2.828zm6.255 6.296c.781.781.781 2.049 0 2.829-.78.78-2.048.78-2.828 0-.78-.78-.78-2.048 0-2.829.78-.78 2.047-.78 2.828 0zm-9.682 0c.781.781.781 2.049 0 2.829-.78.78-2.048.78-2.828 0-.78-.78-.78-2.048 0-2.829.78-.78 2.047-.78 2.828 0zM12.041 19.992c-.78.783-2.048.783-2.828 0-.783-.78-.783-2.048 0-2.828.78-.783 2.048-.783 2.828 0 .783.78.783 2.048 0 2.828z" />
                    </svg>
                );
            default:
                return (
                    <div className={`rounded-full flex items-center justify-center bg-gray-700 text-xs font-bold ${dark ? 'text-white' : 'text-gray-900'}`} style={{ width: '1em', height: '1em' }}>
                        {ticker.charAt(0)}
                    </div>
                );
        }
    };

    useEffect(() => {
        // Function to rotate coins for scrolling effect
        const rotateCoins = () => {
            if (isHovered || !autoScroll) return;

            setVisibleCoins(prev => {
                const newCoins = [...prev];
                const firstCoin = newCoins.shift();
                if (firstCoin) newCoins.push(firstCoin);
                return newCoins;
            });
        };

        // Set up interval for auto-scrolling
        const intervalId = setInterval(rotateCoins, 1000 * (60 / scrollSpeed));

        return () => clearInterval(intervalId);
    }, [autoScroll, isHovered, scrollSpeed]);

    // Update visible coins when the input coins change
    useEffect(() => {
        setVisibleCoins(coins);
    }, [coins]);

    return (
        <div
            className={`crypto-ticker overflow-hidden ${dark ? 'bg-gray-900 text-white' : 'bg-gray-100 text-gray-900'} rounded-lg ${className}`}
            onMouseEnter={() => setIsHovered(true)}
            onMouseLeave={() => setIsHovered(false)}
        >
            <div className="flex justify-between items-center px-3 py-2 border-b border-gray-700">
                <div className="flex items-center">
                    <svg className="w-4 h-4 mr-2 text-green-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M13 7h8m0 0v8m0-8l-8 8-4-4-6 6" />
                    </svg>
                    <span className="text-sm font-medium">Crypto Market</span>
                </div>
                <span className="text-xs text-gray-400">Live Prices</span>
            </div>

            <div className="ticker-container p-2">
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-2">
                    {visibleCoins.slice(0, 6).map((coin, index) => (
                        <div
                            key={`${coin.ticker}-${index}`}
                            className={`flex justify-between items-center p-2 rounded-lg ${dark ? 'bg-gray-800 hover:bg-gray-700' : 'bg-white hover:bg-gray-50'} transition-colors`}
                            style={{
                                animation: `fadeIn 0.5s ease-out forwards ${index * 0.1}s`,
                                opacity: 0
                            }}
                        >
                            <div className="flex items-center">
                                <div className="flex-shrink-0 w-8 h-8 mr-3 rounded-full bg-gradient-to-br from-gray-700 to-gray-900 flex items-center justify-center">
                                    {coin.icon || getCoinIcon(coin.ticker)}
                                </div>
                                <div>
                                    <div className="font-medium">{coin.ticker}</div>
                                    <div className="text-xs text-gray-400">{coin.name}</div>
                                </div>
                            </div>
                            <div className="text-right">
                                <div className="font-medium">${coin.price.toLocaleString()}</div>
                                <div className={`text-xs ${coin.isPositive ? 'text-green-400' : 'text-red-400'}`}>
                                    {coin.change}
                                </div>
                            </div>
                        </div>
                    ))}
                </div>
            </div>

            <style jsx>{`
        @keyframes fadeIn {
          from { 
            opacity: 0;
            transform: translateY(10px);
          }
          to { 
            opacity: 1;
            transform: translateY(0);
          }
        }
      `}</style>
        </div>
    );
};

export default CryptoTicker;