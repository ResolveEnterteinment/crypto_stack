import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';

// Import enhanced components
import Navbar from "../components/LandingPage/Navbar";
import HeroSection from "../components/LandingPage/HeroSection";
import Placeholder from "../components/LandingPage/ui/Placeholder";
import TestimonialCarousel from "../components/LandingPage/TestimonialCarousel";
import FaqAccordion from "../components/LandingPage/FaqAccordion";
import CtaSection from "../components/LandingPage/CtaSection";

// Components for our landing page
const LandingPage = () => {
    const navigate = useNavigate();
    const { isAuthenticated } = useAuth();
    const [isLoading, setIsLoading] = useState(true);

    // Animation state
    const [animateChart, setAnimateChart] = useState(false);
    const [animateFeatures, setAnimateFeatures] = useState(false);

    // Sample investment performance data
    const performanceData = [
        { name: 'Jan', BTC: 32, ETH: 37, USDT: 25 },
        { name: 'Feb', BTC: 40, ETH: 35, USDT: 26 },
        { name: 'Mar', BTC: 45, ETH: 30, USDT: 27 },
        { name: 'Apr', BTC: 50, ETH: 45, USDT: 28 },
        { name: 'May', BTC: 35, ETH: 40, USDT: 28 },
        { name: 'Jun', BTC: 55, ETH: 50, USDT: 29 },
        { name: 'Jul', BTC: 75, ETH: 65, USDT: 30 },
    ];

    // Animated counter hook
    const useCounter = (end, duration = 2000) => {
        const [count, setCount] = useState(0);

        useEffect(() => {
            let startTime;
            let animationFrame;

            const step = (timestamp) => {
                if (!startTime) startTime = timestamp;
                const progress = Math.min((timestamp - startTime) / duration, 1);
                setCount(Math.floor(progress * end));

                if (progress < 1) {
                    animationFrame = requestAnimationFrame(step);
                }
            };

            animationFrame = requestAnimationFrame(step);
            return () => cancelAnimationFrame(animationFrame);
        }, [end, duration]);

        return count;
    };

    // Counters for stats section
    const userCount = useCounter(15000);
    const investedAmount = useCounter(25000000);
    const cryptoCount = useCounter(32);

    // Set up animations
    useEffect(() => {
        setIsLoading(false);

        // Trigger animations after page load
        const chartTimer = setTimeout(() => setAnimateChart(true), 500);
        const featuresTimer = setTimeout(() => setAnimateFeatures(true), 800);

        return () => {
            clearTimeout(chartTimer);
            clearTimeout(featuresTimer);
        };
    }, []);

    // Handle CTA button click
    const handleGetStarted = () => {
        if (isAuthenticated) {
            navigate('/dashboard');
        } else {
            navigate('/auth');
        }
    };

    // Calculate gradient stops for stats panel
    const gradientStops = [
        { color: 'from-blue-600', percent: '0%' },
        { color: 'via-indigo-600', percent: '50%' },
        { color: 'to-purple-600', percent: '100%' },
    ];

    if (isLoading) {
        return (
            <div className="min-h-screen flex items-center justify-center bg-gray-50">
                <div className="w-16 h-16 border-4 border-blue-500 border-t-transparent rounded-full animate-spin"></div>
            </div>
        );
    }

    return (
        <div className="bg-gray-50 min-h-screen">
            <Navbar />
            {/* Hero Section */}
            <HeroSection />

            {/* Stats Section */}
            <div className="py-16 bg-gray-50">
                <div className="container mx-auto px-4">
                    <div className="flex flex-wrap -mx-4">
                        <div className="w-full md:w-1/3 px-4 mb-8 md:mb-0">
                            <div className="h-full p-8 bg-gradient-to-br from-blue-600 via-indigo-600 to-purple-600 rounded-xl text-white shadow-lg transform transition-all hover:scale-105">
                                <h3 className="text-6xl font-bold mb-2">{userCount.toLocaleString()}+</h3>
                                <p className="text-blue-100 text-xl">Active Investors</p>
                            </div>
                        </div>
                        <div className="w-full md:w-1/3 px-4 mb-8 md:mb-0">
                            <div className="h-full p-8 bg-gradient-to-br from-purple-600 via-pink-600 to-red-600 rounded-xl text-white shadow-lg transform transition-all hover:scale-105">
                                <h3 className="text-6xl font-bold mb-2">${(investedAmount / 1000000).toFixed(1)}M+</h3>
                                <p className="text-purple-100 text-xl">Total Invested</p>
                            </div>
                        </div>
                        <div className="w-full md:w-1/3 px-4">
                            <div className="h-full p-8 bg-gradient-to-br from-green-600 via-teal-600 to-cyan-600 rounded-xl text-white shadow-lg transform transition-all hover:scale-105">
                                <h3 className="text-6xl font-bold mb-2">{cryptoCount}+</h3>
                                <p className="text-green-100 text-xl">Cryptocurrencies</p>
                            </div>
                        </div>
                    </div>
                </div>
            </div>

            {/* Features Section */}
            <div className="py-20 bg-white">
                <div className="container mx-auto px-4">
                    <div className="text-center mb-16">
                        <h2 className="text-4xl font-bold mb-4">Why Choose Our Platform</h2>
                        <p className="text-xl text-gray-600 max-w-3xl mx-auto">
                            Investing in crypto shouldn't be complicated. Our platform makes it simple, secure, and effective.
                        </p>
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-3 gap-8">
                        {[
                            {
                                icon: (
                                    <svg className="w-12 h-12 text-blue-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z" />
                                    </svg>
                                ),
                                title: "Automated Investments",
                                description: "Set up recurring investments on your schedule. Daily, weekly, or monthly—you decide.",
                                delay: 0
                            },
                            {
                                icon: (
                                    <svg className="w-12 h-12 text-indigo-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2m-3 7h3m-3 4h3m-6-4h.01M9 16h.01" />
                                    </svg>
                                ),
                                title: "Diversified Portfolio",
                                description: "Spread your investment across multiple cryptocurrencies to balance risk and reward.",
                                delay: 100
                            },
                            {
                                icon: (
                                    <svg className="w-12 h-12 text-purple-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
                                    </svg>
                                ),
                                title: "Bank-Level Security",
                                description: "Your investments are protected with military-grade encryption and strict security protocols.",
                                delay: 200
                            },
                            {
                                icon: (
                                    <svg className="w-12 h-12 text-green-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M17 9V7a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2m2 4h10a2 2 0 002-2v-6a2 2 0 00-2-2H9a2 2 0 00-2 2v6a2 2 0 002 2zm7-5a2 2 0 11-4 0 2 2 0 014 0z" />
                                    </svg>
                                ),
                                title: "Low Fees",
                                description: "Competitive pricing with transparent fee structure—no hidden costs or surprises.",
                                delay: 300
                            },
                            {
                                icon: (
                                    <svg className="w-12 h-12 text-red-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z" />
                                    </svg>
                                ),
                                title: "Regulated & Compliant",
                                description: "We follow all regulatory requirements to ensure your investments are legitimate and protected.",
                                delay: 400
                            },
                            {
                                icon: (
                                    <svg className="w-12 h-12 text-yellow-600" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M13 10V3L4 14h7v7l9-11h-7z" />
                                    </svg>
                                ),
                                title: "Instant Processing",
                                description: "Your orders are executed immediately with optimized trade routing for the best prices.",
                                delay: 500
                            },
                        ].map((feature, index) => (
                            <div
                                key={index}
                                className={`bg-gray-50 p-8 rounded-xl shadow-md transition-all transform ${animateFeatures ? 'translate-y-0 opacity-100' : 'translate-y-8 opacity-0'}`}
                                style={{ transitionDelay: `${feature.delay}ms` }}
                            >
                                <div className="mb-4">{feature.icon}</div>
                                <h3 className="text-xl font-bold mb-3">{feature.title}</h3>
                                <p className="text-gray-600">{feature.description}</p>
                            </div>
                        ))}
                    </div>
                </div>
            </div>

            {/* How It Works Section */}
            <div className="py-20 bg-gray-100">
                <div className="container mx-auto px-4">
                    <div className="text-center mb-16">
                        <h2 className="text-4xl font-bold mb-4">How It Works</h2>
                        <p className="text-xl text-gray-600 max-w-3xl mx-auto">
                            Get started in three simple steps
                        </p>
                    </div>

                    <div className="flex flex-col md:flex-row md:justify-between items-center md:items-start">
                        {[
                            {
                                number: "01",
                                title: "Create Your Account",
                                description: "Sign up in less than 2 minutes with just your email and basic information.",
                                image: "/api/placeholder/300/200"
                            },
                            {
                                number: "02",
                                title: "Set Investment Plan",
                                description: "Choose your investment amount, frequency, and the cryptocurrencies you want to buy.",
                                image: "/api/placeholder/300/200"
                            },
                            {
                                number: "03",
                                title: "Watch Your Portfolio Grow",
                                description: "We'll automatically execute your investment plan and you can track performance in real-time.",
                                image: "/api/placeholder/300/200"
                            }
                        ].map((step, index) => (
                            <div key={index} className="w-full md:w-1/3 mb-12 md:mb-0 px-4">
                                <div className="relative">
                                    <div className="rounded-xl overflow-hidden mb-6 shadow-lg transform transition-all hover:scale-105 hover:shadow-xl">
                                        <Placeholder
                                            width={300}
                                            height={200}
                                            text={step.title}
                                            type="generic"
                                            className="w-full h-48 object-cover"
                                        />
                                    </div>
                                    <div className="absolute -top-4 -left-4 w-16 h-16 rounded-full bg-blue-600 text-white text-2xl font-bold flex items-center justify-center shadow-lg">
                                        {step.number}
                                    </div>
                                </div>
                                <h3 className="text-2xl font-bold mb-3">{step.title}</h3>
                                <p className="text-gray-600">{step.description}</p>
                            </div>
                        ))}
                    </div>
                </div>
            </div>

            {/* Testimonials Section */}
            <div className="py-20 bg-white">
                <div className="container mx-auto px-4">
                    <div className="text-center mb-16">
                        <h2 className="text-4xl font-bold mb-4">What Our Users Say</h2>
                        <p className="text-xl text-gray-600 max-w-3xl mx-auto">
                            Join thousands of satisfied investors already growing their crypto portfolios
                        </p>
                    </div>

                    <TestimonialCarousel
                        testimonials={[
                            {
                                quote: "I've tried many platforms, but this one makes investing in crypto truly effortless. The automated buys have consistently built my portfolio even during market dips.",
                                name: "Michael T.",
                                title: "Software Engineer",
                                image: "/api/placeholder/64/64",
                                rating: 5
                            },
                            {
                                quote: "As someone new to crypto, I was intimidated by the complexity. This platform made it simple to start small and grow my investments over time without constant monitoring.",
                                name: "Sarah L.",
                                title: "Marketing Director",
                                image: "/api/placeholder/64/64",
                                rating: 5
                            },
                            {
                                quote: "The diversification options are excellent. I've been able to spread my risk across multiple cryptocurrencies while still focusing on the ones I believe in long-term.",
                                name: "David R.",
                                title: "Financial Analyst",
                                image: "/api/placeholder/64/64",
                                rating: 4
                            },
                            {
                                quote: "Customer support is fantastic. When I had questions about my subscription, they responded quickly and resolved everything in minutes.",
                                name: "Emily K.",
                                title: "Small Business Owner",
                                image: "/api/placeholder/64/64",
                                rating: 5
                            }
                        ]}
                        className="max-w-3xl mx-auto"
                    />
                </div>
            </div>

            {/* FAQ Section */}
            <div className="py-20 bg-gray-50">
                <div className="container mx-auto px-4">
                    <div className="text-center mb-16">
                        <h2 className="text-4xl font-bold mb-4">Frequently Asked Questions</h2>
                        <p className="text-xl text-gray-600 max-w-3xl mx-auto">
                            Got questions? We've got answers.
                        </p>
                    </div>

                    <div className="max-w-4xl mx-auto">
                        <FaqAccordion
                            items={[
                                {
                                    question: "How do I get started?",
                                    answer: "Creating an account takes less than 2 minutes. Just click the Get Started button, enter your email, create a password, and you're ready to set up your first investment plan.",
                                    category: "Getting Started"
                                },
                                {
                                    question: "What are the fees?",
                                    answer: "We charge a simple 1% platform fee on each transaction, plus standard payment processing fees (2.9% + $0.30). There are no hidden charges or subscription fees.",
                                    category: "Pricing"
                                },
                                {
                                    question: "How secure is my investment?",
                                    answer: "We use bank-level encryption and security measures to protect your data and investments. We never store your credit card information and all transactions are processed through secure, regulated channels.",
                                    category: "Security"
                                },
                                {
                                    question: "Can I withdraw my funds anytime?",
                                    answer: "Yes, you have full control over your portfolio. You can withdraw your crypto or sell it back to fiat currency at any time without penalties.",
                                    category: "Account Management"
                                },
                                {
                                    question: "Which cryptocurrencies can I invest in?",
                                    answer: "We support over 30 cryptocurrencies including Bitcoin, Ethereum, Solana, Cardano, and many more. We regularly add new cryptocurrencies based on market stability and user demand.",
                                    category: "Investments"
                                },
                                {
                                    question: "How do recurring investments work?",
                                    answer: "You set up a schedule (daily, weekly, or monthly), choose your amount and which cryptocurrencies to buy, and we'll automatically execute the purchases according to your plan.",
                                    category: "Getting Started"
                                },
                                {
                                    question: "What happens if a transaction fails?",
                                    answer: "If a transaction fails for any reason, we'll notify you immediately and retry at the next opportunity. Your funds will remain in your account until the transaction completes successfully.",
                                    category: "Account Management"
                                }
                            ]}
                            showCategories={true}
                        />
                    </div>

                    <div className="text-center mt-12">
                        <p className="text-gray-600 mb-6">Still have questions?</p>
                        <button
                            onClick={() => navigate('/contact')}
                            className="px-8 py-3 bg-blue-600 hover:bg-blue-700 text-white rounded-lg font-medium transition-all"
                        >
                            Contact Support
                        </button>
                    </div>
                </div>
            </div>

            {/* CTA Section */}
            <CtaSection
                title="Ready to Start Your Crypto Journey?"
                description="Join thousands of investors who are already building their crypto portfolio the smart way."
                primaryButtonText="Create Free Account"
                secondaryButtonText="View Pricing"
                primaryButtonLink="/auth/register"
                secondaryButtonLink="/pricing"
                backgroundStyle="particles"
                showDemo={true}
            />

            {/* Footer */}
            {/* Add proper aria-labels to the social media links */}
            <a href="#" aria-label="Facebook" className="text-gray-400 hover:text-white transition-colors">
                <svg className="w-6 h-6" fill="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                    <path fillRule="evenodd" d="M22 12c0-5.523-4.477-10-10-10S2 6.477 2 12c0 4.991 3.657 9.128 8.438 9.878v-6.987h-2.54V12h2.54V9.797c0-2.506 1.492-3.89 3.777-3.89 1.094 0 2.238.195 2.238.195v2.46h-1.26c-1.243 0-1.63.771-1.63 1.562V12h2.773l-.443 2.89h-2.33v6.988C18.343 21.128 22 16.991 22 12z" clipRule="evenodd" />
                </svg>
            </a>
            
            {/* Global CSS for animations */}
            <style tsx global>{`
                @keyframes growUp {
                  from { transform: scaleY(0); }
                  to { transform: scaleY(1); }
                }
        
                @keyframes fadeInUp {
                  from { 
                    opacity: 0;
                    transform: translateY(20px);
                  }
                  to { 
                    opacity: 1;
                    transform: translateY(0);
                  }
                }
        
                @keyframes pulse {
                  0% { transform: scale(1); }
                  50% { transform: scale(1.05); }
                  100% { transform: scale(1); }
                }
        
                @keyframes float {
                  0% { transform: translateY(0px); }
                  50% { transform: translateY(-10px); }
                  100% { transform: translateY(0px); }
                }
        
                .animate-fadeInUp {
                  animation: fadeInUp 0.5s ease-out forwards;
                }
        
                .animate-pulse-slow {
                  animation: pulse 3s ease-in-out infinite;
                }
        
                .animate-float {
                  animation: float 6s ease-in-out infinite;
                }
        `}</style>

        </div>
    );
}
export default LandingPage;