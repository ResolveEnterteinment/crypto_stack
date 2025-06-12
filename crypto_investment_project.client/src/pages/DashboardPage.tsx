// src/pages/DashboardPage.tsx
import React, { useEffect, useState } from 'react';
import { useAuth } from "../context/AuthContext";
import { useNavigate } from "react-router-dom";
import Navbar from "../components/Navbar";
import PortfolioChart from "../components/Dashboard/portfolio-chart";
import AssetBalanceCard from "../components/Dashboard/asset-balance-card";
import SubscriptionCard from "../components/Dashboard/subscription-card";
import { getDashboardData } from "../services/dashboard";
import { getSubscriptions, getTransactions, updateSubscription } from "../services/subscription";
import { Subscription } from "../types/subscription";
import ITransaction from "../interfaces/ITransaction";
import ApiTestPanel from '../components/DevTools/ApiTestPanel';
import { Dashboard } from '../types/dashboardTypes';

const DashboardPage: React.FC = () => {
    // Get authenticated user and navigation
    const { user, logout } = useAuth();
    const navigate = useNavigate();

    // State management
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [isHistoryModalOpen, setHistoryModalOpen] = useState(false);
    const [currentSubscriptionId, setCurrentSubscriptionId] = useState<string | null>(null);
    const [dashboardData, setDashboardData] = useState<Dashboard | null>(null);
    const [subscriptions, setSubscriptions] = useState<Subscription[]>([]);
    const [transactions, setTransactions] = useState<ITransaction[]>([]);
    const [transactionsLoading, setTransactionsLoading] = useState(false);

    // Fetch data when component mounts
    useEffect(() => {
        if (!user || !user.id) {
            // Clear state explicitly upon logout
            setSubscriptions([]);
            setDashboardData(null);
            navigate('/auth');
            return;
        }

        setLoading(true);
        setError(null);

        // Clear previous user's data immediately on user change
        setSubscriptions([]);
        setDashboardData(null);

        Promise.all([
            fetchDashboardData(),
            fetchSubscriptions()
        ])
            .then(() => setLoading(false))
            .catch(err => {
                console.error('Error loading dashboard data:', err);
                setError('Failed to load dashboard data. Please try again.');
                setLoading(false);
            });
    }, [user]);


    // Fetch dashboard data
    const fetchDashboardData = async () => {
        try {
            if (!user?.id) return;
            const data = await getDashboardData(user.id);
            setDashboardData(data);
        } catch (err) {
            console.error('Error fetching dashboard data:', err);
            throw err;
        }
    };

    // Fetch subscriptions
    const fetchSubscriptions = async () => {
        try {
            console.log("User ID before fetching subscriptions:", user?.id);
            if (!user?.id) throw new Error("User ID is missing.");
            const trimmedUserId = user.id.trim(); // Trim whitespaces
            const data = await getSubscriptions(trimmedUserId);
            setSubscriptions(data);
        } catch (err) {
            console.error('Error fetching subscriptions:', err);
            throw err;
        }
    };

    // Fetch subscription transactions
    const fetchTransactionHistory = async (subscriptionId: string) => {
        if (!subscriptionId) {
            console.error('Cannot fetch transaction history: Subscription ID is undefined');
            return;
        }

        try {
            setTransactionsLoading(true);
            const data = await getTransactions(subscriptionId);
            setTransactions(data);
        } catch (err) {
            console.error('Error fetching transactions:', err);
            // Show a user-friendly error in the modal instead of failing completely
            setTransactions([]);
        } finally {
            setTransactionsLoading(false);
        }
    };

    // Handler functions
    const handleLogout = () => {
        logout();
        navigate('/auth');
    };

    const handleEditSubscription = (id: string) => {
        // Navigate to subscription edit page
        navigate(`/subscription/edit/${id}`);
    };

    const handleCancelSubscription = async (id: string) => {
        try {
            await updateSubscription(id, { isCancelled: true });

            // Update local state to reflect changes
            setSubscriptions(subscriptions.map(sub =>
                sub.id === id ? { ...sub, isCancelled: true } : sub
            ));
        } catch (err) {
            console.error('Error cancelling subscription:', err);
            alert('Failed to cancel subscription. Please try again.');
        }
    };

    const handleViewHistory = async (id: string) => {
        if (!id) {
            console.error("Cannot view history: Subscription ID is missing");
            return;
        }

        setCurrentSubscriptionId(id);
        setHistoryModalOpen(true);
        await fetchTransactionHistory(id);
    };

    const showProfile = () => {
        navigate('/profile');
    };

    const showSettings = () => {
        navigate('/settings');
    };

    // Calculate profit/loss percentage
    const calculateProfitPercentage = () => {
        if (!dashboardData) return 0;

        const { totalInvestments, portfolioValue } = dashboardData;
        if (!totalInvestments || totalInvestments === 0) return 0;

        return ((portfolioValue - totalInvestments) / totalInvestments) * 100;
    };

    const profitPercentage = calculateProfitPercentage();
    const isProfitable = profitPercentage >= 0;

    // Find current subscription details for the modal
    const currentSubscription = subscriptions.find(sub => sub.id === currentSubscriptionId);

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
                        onClick={() => window.location.reload()}
                        className="bg-blue-600 text-white py-2 px-4 rounded-md hover:bg-blue-700 transition-colors"
                    >
                        Retry
                    </button>
                </div>
            </div>
        );
    }

    return (
        <>
            <Navbar/>
            <div className="min-h-screen bg-gray-50 py-8 px-4 lg:px-10">
                {/* Header with welcome message */}
                <div className="mb-8">
                    <h1 className="text-3xl font-bold mb-2">Welcome back, {user?.username || 'Investor'}</h1>,
                    <p className="text-gray-500">Here's the latest update on your investments</p>
                </div>

                {/* Summary Cards */}
                <div className="grid grid-cols-1 md:grid-cols-3 gap-6 mb-8">
                    {/* Total Investment Card */}
                    <div className="bg-white shadow rounded-lg p-6">
                        <div className="flex justify-between items-start">
                            <div>
                                <p className="text-gray-500 mb-1">Total Investment</p>
                                <h2 className="text-3xl font-bold">${dashboardData?.totalInvestments.toFixed(2) || '0.00'}</h2>
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
                                <h2 className="text-3xl font-bold">${dashboardData?.portfolioValue.toFixed(2) || '0.00'}</h2>
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
                            {isProfitable ? 'Profit' : 'Loss'} of ${dashboardData ?
                                Math.abs(dashboardData.portfolioValue - dashboardData.totalInvestments).toFixed(2) : '0.00'}
                        </p>
                    </div>
                </div>

                {/* Chart & Assets Section */}
                <div className="grid grid-cols-1 lg:grid-cols-3 gap-6 mb-8">
                    {/* Chart */}
                    <div className="bg-white shadow rounded-lg p-6 lg:col-span-2">
                        <h2 className="text-xl font-semibold mb-4">Investment vs Portfolio Value</h2>
                        <PortfolioChart
                            investmentData={dashboardData?.totalInvestments || 0}
                            portfolioData={dashboardData?.portfolioValue || 0}
                        />
                    </div>

                    {/* Asset Balance */}
                    <div className="lg:col-span-1">
                        <AssetBalanceCard assetHoldings={dashboardData?.assetHoldings!} />
                    </div>
                </div>

                {/* Subscriptions Section */}
                <div className="mb-8">
                    <div className="flex justify-between items-center mb-6">
                        <h2 className="text-2xl font-bold">Your Subscriptions</h2>
                        <button
                            onClick={() => navigate('/subscription/new')}
                            className="bg-blue-600 text-white py-2 px-4 rounded-md hover:bg-blue-700 transition-colors text-sm font-medium"
                        >
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
                            <button
                                onClick={() => navigate('/subscription/new')}
                                className="bg-blue-600 text-white py-2 px-6 rounded-md hover:bg-blue-700 transition-colors font-medium"
                            >
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
                                <h3 className="text-xl font-bold">
                                    {currentSubscription ? (
                                        <span>Transaction History: ${currentSubscription.amount} {currentSubscription.interval.toLowerCase()} plan</span>
                                    ) : (
                                        <span>Transaction History</span>
                                    )}
                                </h3>
                                <button
                                    onClick={() => {
                                        setHistoryModalOpen(false);
                                        setTransactions([]);
                                        setCurrentSubscriptionId(null); // Reset the ID when closing
                                    }}
                                    className="text-gray-500 hover:text-gray-700"
                                >
                                    <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 18L18 6M6 6l12 12"></path>
                                    </svg>
                                </button>
                            </div>

                            <div className="overflow-y-auto flex-grow">
                                {transactionsLoading ? (
                                    <div className="flex justify-center items-center h-40">
                                        <div className="w-10 h-10 border-4 border-blue-500 border-t-transparent rounded-full animate-spin"></div>
                                    </div>
                                ) : transactions.length > 0 ? (
                                    <div className="space-y-4">
                                        {transactions.map((item, index) => (
                                            <div key={index} className="border-b pb-4 last:border-0">
                                                <div className="flex justify-between items-center">
                                                    <div>
                                                        <p className="font-medium">{item.assetName} {item.action}</p>
                                                        <p className="text-sm text-gray-500">
                                                            {new Date(item.createdAt).toLocaleDateString('en-US', {
                                                                year: 'numeric',
                                                                month: 'short',
                                                                day: 'numeric',
                                                                hour: '2-digit',
                                                                minute: '2-digit'
                                                            })}
                                                        </p>
                                                    </div>
                                                    <div className="text-right">
                                                        <p className="font-medium">+{item.quantity.toFixed(6)} {item.assetTicker}</p>
                                                        <p className="text-sm text-gray-500">{item.quoteCurrency} {item.quoteQuantity.toFixed(2)}</p>
                                                    </div>
                                                </div>
                                            </div>
                                        ))}
                                    </div>
                                ) : (
                                    <div className="py-6 text-center text-gray-500">
                                        <svg className="h-10 w-10 mx-auto mb-2 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"></path>
                                        </svg>
                                        <p>No transaction history found for this subscription</p>
                                    </div>
                                )}
                            </div>

                            <div className="mt-6">
                                <button
                                    className="w-full bg-blue-600 text-white py-3 rounded-md hover:bg-blue-700 font-medium"
                                    onClick={() => {
                                        setHistoryModalOpen(false);
                                        setTransactions([]);
                                        setCurrentSubscriptionId(null); // Reset the ID when closing
                                    }}
                                >
                                    Close
                                </button>
                            </div>
                        </div>
                    </div>
                )}
            </div>
            <ApiTestPanel />
        </>
    );
};

export default DashboardPage;