﻿import React, { useEffect, useState } from 'react';
import { useAuth } from "../context/AuthContext";
import { useNavigate } from "react-router-dom";
import Navbar from "../components/Navbar"
import { getSubscriptions, ISubscription } from "../services/subscription";
import { getBalances, getTotalInvestments, getPortfolioValue, IBalance } from "../services/balance";

const DashboardPage: React.FC = () => {
    const [isModalOpen, setModalOpen] = useState(false);
    const { user, logout } = useAuth();
    const [balances, setBalances] = useState<IBalance[] | null>(null); // Changed from empty array to null
    const [totalInvestments, setTotalInvestments] = useState<number | null>(null); // Changed from 0 to null
    const [portfolioValue, setPortfolioValue] = useState<number | null>(null); // Changed from 0 to null
    const [subscriptions, setSubscriptions] = useState<ISubscription[] | null>(null); // Changed from empty array to null
    const navigate = useNavigate();

    useEffect(() => {
        if (!user || !user.id) return;
        fetchBalances(user.id);
        fetchTotalInvestments(user.id);
        fetchPortfolioValue(user.id);
        fetchSubscriptions(user.id);
    }, [user]);

    const fetchBalances = async (id: string) => {
        if (!id) return;
        const data = await getBalances(id);
        setBalances(data);
    };

    const fetchPortfolioValue = async (id: string) => {
        if (!id) return;
        const data = await getPortfolioValue(id);
        setPortfolioValue(data);
    };

    const fetchTotalInvestments = async (id: string) => {
        if (!id) return;
        const data = await getTotalInvestments(id);
        setTotalInvestments(data);
    };

    const fetchSubscriptions = async (id: string) => {
        if (!id) return;
        const data = await getSubscriptions(id);
        setSubscriptions(data);
    };

    const handleLogout = () => {
        logout();
        navigate("/auth");
    };

    const showProfile = () => {
        alert("Showing user profile...");
    };

    const showSettings = () => {
        alert("Showing user settings...");
    };

    return (
        <>
            <Navbar showProfile={showProfile} showSettings={showSettings} logout={handleLogout} />
            <div className="min-h-screen bg-gray-100 py-8 px-4 lg:px-10">
                <div className="flex justify-between items-center mb-6">
                    <h1 className="text-3xl font-bold">Welcome, {user?.username}</h1>
                </div>
                {/* Total Balances Section */}
                <div className="grid grid-cols-1 md:grid-cols-3 gap-6 mb-8">
                    <div className="bg-white shadow rounded-lg p-4">
                        <h2 className="text-xl font-semibold">Total USD Investment</h2>
                        <p className="text-2xl font-bold mt-2">
                            {totalInvestments !== null ? `${totalInvestments.valueOf()} USD` : "Loading..."}
                        </p>
                    </div>
                    <div className="bg-white shadow rounded-lg p-4">
                        <h2 className="text-xl font-semibold">Asset Holdings</h2>
                        {balances && balances.length > 0 ? (
                            balances.map((balance) => (
                                <p key={balance.assetId} className="font-bold mt-2">
                                    {balance.ticker}: <span className="font-normal">{balance.total}</span>
                                </p>
                            ))
                        ) : (
                            <p className="font-bold mt-2">Loading...</p>
                        )}
                    </div>
                    <div className="bg-white shadow rounded-lg p-4">
                        <h2 className="text-xl font-semibold">Portfolio Value</h2>
                        <p className="text-2xl font-bold mt-2">
                            {portfolioValue !== null ? `${portfolioValue} USD` : "Loading..."}
                        </p>
                    </div>
                </div>
                {/* Subscriptions Section */}
                <div className="mb-8">
                    <h2 className="text-2xl font-bold mb-4">Your Subscriptions</h2>
                    <div className="grid grid-cols-1 lg:grid-cols-2 xl:grid-cols-3 gap-6">
                        {subscriptions && subscriptions.length > 0 ? (
                            subscriptions.map((sub, index) => (
                                <div key={sub.id} className="bg-white shadow-lg rounded-xl p-6 relative">
                                    <div className="mb-4">
                                        <h3 className="text-xl font-semibold">Subscription #{index + 1}</h3>
                                        <p className="text-gray-600">Id: {sub.id}</p>
                                        <p className="text-gray-600">
                                            Started: {new Date(Date.parse(sub.createdAt)).toLocaleDateString()}
                                        </p>
                                    </div>
                                    <div className="mb-4">
                                        <p className="text-sm font-semibold">
                                            Interval: <span className="font-normal">{sub.interval}</span>
                                        </p>
                                        <p className="text-sm font-semibold">
                                            Amount: <span className="font-normal">{sub.amount} {sub.currency}</span>
                                        </p>
                                        <p className="text-sm font-semibold">
                                            Next Due Date: <span className="font-normal">
                                                {new Date(sub.nextDueDate).toLocaleDateString()}
                                            </span>
                                        </p>
                                        <p className="text-sm font-semibold">
                                            Total Investments: <span className="font-normal">
                                                {sub.totalInvestments} {sub.currency}
                                            </span>
                                        </p>
                                        <p className="text-sm font-semibold">
                                            Status: <span className="text-green-500 font-normal">
                                                {sub.isCancelled ? "Cancelled" : "Active"}
                                            </span>
                                        </p>
                                    </div>
                                    <div className="mb-4">
                                        <h4 className="font-semibold">Allocations:</h4>
                                        <ul className="list-disc list-inside text-sm">
                                            {sub.allocations?.map((alloc) => (
                                                <li key={alloc.id}>
                                                    {alloc.ticker}: {alloc.percentAmount}%
                                                </li>
                                            ))}
                                        </ul>
                                    </div>
                                    <div className="flex gap-2 absolute bottom-4 right-4">
                                        <button className="bg-blue-500 text-white px-3 py-1 rounded-md text-sm hover:bg-blue-600">
                                            Edit
                                        </button>
                                        <button className="bg-red-500 text-white px-3 py-1 rounded-md text-sm hover:bg-red-600">
                                            Cancel
                                        </button>
                                        <button
                                            onClick={() => setModalOpen(true)}
                                            className="bg-gray-500 text-white px-3 py-1 rounded-md text-sm hover:bg-gray-600"
                                        >
                                            History
                                        </button>
                                    </div>
                                </div>
                            ))
                        ) : (
                            <p>Loading subscriptions...</p>
                        )}
                    </div>
                </div>
                {/* Chart Placeholder */}
                <div className="bg-white shadow rounded-lg p-4">
                    <h2 className="text-2xl font-bold mb-4">Investment vs Portfolio (USD)</h2>
                    <div className="h-64 flex items-center justify-center text-gray-400">
                        [Chart Placeholder]
                    </div>
                </div>
                {/* Transactions Modal */}
                {isModalOpen && (
                    <div className="fixed inset-0 bg-black bg-opacity-50 flex justify-center items-center">
                        <div className="bg-white p-6 rounded-xl shadow-lg max-w-xl w-full">
                            <h3 className="text-xl font-bold mb-4">Transaction History</h3>
                            <ul className="mb-4 max-h-72 overflow-auto">
                                <li className="border-b py-2">Jan 10, 2025 - BTC Buy - $500</li>
                                <li className="border-b py-2">Feb 10, 2025 - ETH Buy - $300</li>
                            </ul>
                            <button
                                className="bg-blue-600 text-white py-2 px-4 rounded-md hover:bg-blue-700"
                                onClick={() => setModalOpen(false)}
                            >
                                Close
                            </button>
                        </div>
                    </div>
                )}
            </div>
        </>
    );
};

export default DashboardPage;