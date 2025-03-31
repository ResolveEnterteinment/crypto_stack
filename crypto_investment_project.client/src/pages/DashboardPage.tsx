import React, { useEffect, useState } from 'react';
import PortfolioChart from "../components/Dashboard/portfolio-chart";
import AssetBalanceCard from "../components/Dashboard/asset-balance-card";
import SubscriptionCard from "../components/Dashboard/subscription-card";

const DashboardPage = ({ user, logout, navigate }) => {
    // State management
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState(null);
    const [isHistoryModalOpen, setHistoryModalOpen] = useState(false);
    const [selectedSubscription, setSelectedSubscription] = useState(null);
    const [dashboardData, setDashboardData] = useState({
        balances: [],
        totalInvestments: 0,
        portfolioValue: 0
    });
    const [subscriptions, setSubscriptions] = useState([]);

    // Mock data for demonstration
    useEffect(() => {
        // Simulating API call
        setTimeout(() => {
            setDashboardData({
                balances: [
                    { ticker: 'BTC', assetName: 'Bitcoin', total: 0.5, available: 0.5, locked: 0 },
                    { ticker: 'ETH', assetName: 'Ethereum', total: 2.5, available: 2.5, locked: 0 },
                    { ticker: 'USDT', assetName: 'Tether', total: 500, available: 500, locked: 0 }
                ],
                totalInvestments: 10000,
                portfolioValue: 11500
            });

            setSubscriptions([
                {
                    id: '1',
                    createdAt: '2025-01-01T00:00:00Z',
                    userId: '123',
                    allocations: [
                        { id: 'a1', ticker: 'BTC', percentAmount: 50 },
                        { id: 'a2', ticker: 'ETH', percentAmount: 30 },
                        { id: 'a3', ticker: 'USDT', percentAmount: 20 }
                    ],
                    interval: 'MONTHLY',
                    amount: 500,
                    currency: 'USD',
                    nextDueDate: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000),
                    endDate: new Date(Date.now() + 365 * 24 * 60 * 60 * 1000),
                    totalInvestments: 5000,
                    isCancelled: false
                },
                {
                    id: '2',
                    createdAt: '2025-02-01T00:00:00Z',
                    userId: '123',
                    allocations: [
                        { id: 'b1', ticker: 'BTC', percentAmount: 100 }
                    ],
                    interval: 'WEEKLY',
                    amount: 100,
                    currency: 'USD',
                    nextDueDate: new Date(Date.now() + 3 * 24 * 60 * 60 * 1000),
                    endDate: new Date(Date.now() + 180 * 24 * 60 * 60 * 1000),
                    totalInvestments: 1000,
                    isCancelled: false
                }
            ]);

            setLoading(false);
        }, 1500);
    }, []);

    // Handler functions
    const handleLogout = () => {
        logout();
    };

    const handleEditSubscription = (id) => {
        // Implement edit functionality
        console.log(`Edit subscription ${id}`);
    };

    const handleCancelSubscription = async (id) => {
        // Implement cancel functionality
        console.log(`Cancel subscription ${id}`);

        // Update local state to simulate API call
        setSubscriptions(subscriptions.map(sub =>
            sub.id === id ? { ...sub, isCancelled: true } : sub
        ));
    };

    const handleViewHistory = (id) => {
        setSelectedSubscription(id);
        setHistoryModalOpen(true);
    };

    const showProfile = () => {
        console.log("Show profile");
    };

    const showSettings = () => {
        console.log("Show settings");
    };

    // Calculate profit/loss percentage
    const calculateProfitPercentage = () => {
        const { totalInvestments, portfolioValue } = dashboardData;
        if (!totalInvestments || totalInvestments === 0) return 0;

        return ((portfolioValue - totalInvestments) / totalInvestments) * 100;
    };

    const profitPercentage = calculateProfitPercentage();
    const isProfitable = profitPercentage >= 0;

    if (loading) {
        return (
            <div className="min-h-screen bg-gray-50 flex justify-center items-center">
                <div className="text-center">
                    <div className="w-16 h-16 border-4 border-blue-500 border-t-transparent rounded-full animate-spin mx-auto mb-4"></div>
                    <p className="text-gray-500">Loading your dashboard...</p>
                </div>
            </div>
        );
    }

    if (error) {
        return (
            <div className="min-h-screen bg-gray-50 flex justify-center items-center">
                <div className="bg-white p-8 rounded-lg shadow-md max-w-md w-full text-center">
                    <div className="text-red-500 text-5xl mb-4">
                        <svg className="w-16 h-16 mx-auto" fill="none" stroke="currentColor" viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"></path>
                        </svg>
                    </div>
                    <h2 className="text-2xl font-bold mb-4">Error Loading Dashboard</h2>
                    <p className="text-gray-600 mb-6">{error}</p>
                    <button
                        className="bg-blue-600 text-white py-2 px-4 rounded-md hover:bg-blue-700 transition-colors"
                    >
                        Retry
                    </button>
                </div>
            </div>
        );
    }

    return (
        <div className="min-h-screen bg-gray-50 py-8 px-4 lg:px-10">
            {/* Header with welcome message */}
            <div className="mb-8">
                <h1 className="text-3xl font-bold mb-2">Welcome back, {user?.username || 'Investor'}</h1>
                <p className="text-gray-500">Here's the latest update on your investments</p>
            </div>

            {/* Summary Cards */}
            <div className="grid grid-cols-1 md:grid-cols-3 gap-6 mb-8">
                {/* Total Investment Card */}
                <div className="bg-white shadow rounded-lg p-6">
                    <div className="flex justify-between items-start">
                        <div>
                            <p className="text-gray-500 mb-1">Total Investment</p>
                            <h2 className="text-3xl font-bold">${dashboardData.totalInvestments.toFixed(2)}</h2>
                        </div>
                        <div className="bg-blue-100 p-3 rounded-full">
                            <svg className="w-6 h-6 text-blue-600" fill="none" stroke="currentColor" viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 8c-1.657 0-3 .895-3 2s1.343 2 3 2 3 .895 3 2-1.343 2-3 2m0-8c1.11 0 2.08.402 2.599 1M12 8V7m0 1v8m0 0v1m0-1c-1.11 0-2.08-.402-2.599-1M21 12a9 9 0 11-18 0 9 9 0 0118 0z"></path>
                            </svg>
                        </div>
                    </div>
                </div>

                {/* Portfolio Value Card */}
                <div className="bg-white shadow rounded-lg p-6">
                    <div className="flex justify-between items-start">
                        <div>
                            <p className="text-gray-500 mb-1">Portfolio Value</p>
                            <h2 className="text-3xl font-bold">${dashboardData.portfolioValue.toFixed(2)}</h2>
                        </div>
                        <div className="bg-green-100 p-3 rounded-full">
                            <svg className="w-6 h-6 text-green-600" fill="none" stroke="currentColor" viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M13 7h8m0 0v8m0-8l-8 8-4-4-6 6"></path>
                            </svg>
                        </div>
                    </div>
                </div>

                {/* Profit/Loss Card */}
                <div className="bg-white shadow rounded-lg p-6">
                    <div className="flex justify-between items-start">
                        <div>
                            <p className="text-gray-500 mb-1">Profit/Loss</p>
                            <h2 className={`text-3xl font-bold ${isProfitable ? 'text-green-600' : 'text-red-600'}`}>
                                {isProfitable ? '+' : ''}{profitPercentage.toFixed(2)}%
                            </h2>
                        </div>
                        <div className={`${isProfitable ? 'bg-green-100' : 'bg-red-100'} p-3 rounded-full`}>
                            <svg
                                className={`w-6 h-6 ${isProfitable ? 'text-green-600' : 'text-red-600'}`}
                                fill="none"
                                stroke="currentColor"
                                viewBox="0 0 24 24"
                                xmlns="http://www.w3.org/2000/svg"
                            >
                                <path
                                    strokeLinecap="round"
                                    strokeLinejoin="round"
                                    strokeWidth="2"
                                    d={isProfitable
                                        ? "M13 7h8m0 0v8m0-8l-8 8-4-4-6 6"
                                        : "M13 17h8m0 0V9m0 8l-8-8-4 4-6-6"
                                    }
                                ></path>
                            </svg>
                        </div>
                    </div>
                    <p className="text-gray-500 text-sm mt-2">
                        {isProfitable ? 'Profit' : 'Loss'} of ${Math.abs(dashboardData.portfolioValue - dashboardData.totalInvestments).toFixed(2)}
                    </p>
                </div>
            </div>

            {/* Chart & Assets Section */}
            <div className="grid grid-cols-1 lg:grid-cols-3 gap-6 mb-8">
                {/* Chart */}
                <div className="bg-white shadow rounded-lg p-6 lg:col-span-2">
                    <h2 className="text-xl font-semibold mb-4">Investment vs Portfolio Value</h2>
                    <PortfolioChart
                        investmentData={dashboardData.totalInvestments}
                        portfolioData={dashboardData.portfolioValue}
                    />
                </div>

                {/* Asset Balance */}
                <div className="lg:col-span-1">
                    <AssetBalanceCard balances={dashboardData.balances} />
                </div>
            </div>

            {/* Subscriptions Section */}
            <div className="mb-8">
                <div className="flex justify-between items-center mb-6">
                    <h2 className="text-2xl font-bold">Your Subscriptions</h2>
                    <button className="bg-blue-600 text-white py-2 px-4 rounded-md hover:bg-blue-700 transition-colors text-sm font-medium">
                        New Subscription
                    </button>
                </div>

                {subscriptions.length > 0 ? (
                    <div className="grid grid-cols-1 lg:grid-cols-2 xl:grid-cols-3 gap-6">
                        {subscriptions.map((subscription) => (
                            <SubscriptionCard
                                key={subscription.id}
                                subscription={subscription}
                                onEdit={handleEditSubscription}
                                onCancel={handleCancelSubscription}
                                onViewHistory={handleViewHistory}
                            />
                        ))}
                    </div>
                ) : (
                    <div className="bg-white shadow-md rounded-lg p-8 text-center">
                        <svg className="w-16 h-16 text-gray-400 mx-auto mb-4" fill="none" stroke="currentColor" viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 9v3m0 0v3m0-3h3m-3 0H9m12 0a9 9 0 11-18 0 9 9 0 0118 0z"></path>
                        </svg>
                        <h3 className="text-xl font-medium mb-2">No subscriptions yet</h3>
                        <p className="text-gray-500 mb-6">Start your investment journey by creating your first subscription</p>
                        <button className="bg-blue-600 text-white py-2 px-6 rounded-md hover:bg-blue-700 transition-colors font-medium">
                            Create Subscription
                        </button>
                    </div>
                )}
            </div>

            {/* Transaction History Modal */}
            {isHistoryModalOpen && (
                <div className="fixed inset-0 bg-black bg-opacity-50 z-50 flex justify-center items-center">
                    <div className="bg-white p-6 rounded-xl shadow-xl max-w-2xl w-full mx-4 max-h-[80vh] flex flex-col">
                        <div className="flex justify-between items-center mb-6">
                            <h3 className="text-xl font-bold">Transaction History</h3>
                            <button
                                onClick={() => setHistoryModalOpen(false)}
                                className="text-gray-500 hover:text-gray-700"
                            >
                                <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 18L18 6M6 6l12 12"></path>
                                </svg>
                            </button>
                        </div>

                        <div className="overflow-y-auto flex-grow">
                            {/* This would be populated with actual transaction data */}
                            <div className="space-y-4">
                                {[1, 2, 3, 4, 5].map((item) => (
                                    <div key={item} className="border-b pb-4 last:border-0">
                                        <div className="flex justify-between items-center">
                                            <div>
                                                <p className="font-medium">BTC Purchase</p>
                                                <p className="text-sm text-gray-500">March {item}, 2025</p>
                                            </div>
                                            <div className="text-right">
                                                <p className="font-medium">+0.0034 BTC</p>
                                                <p className="text-sm text-gray-500">$500.00</p>
                                            </div>
                                        </div>
                                    </div>
                                ))}
                            </div>
                        </div>

                        <div className="mt-6">
                            <button
                                className="w-full bg-blue-600 text-white py-3 rounded-md hover:bg-blue-700 font-medium"
                                onClick={() => setHistoryModalOpen(false)}
                            >
                                Close
                            </button>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
};

export default DashboardPage;