import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import AdminLogsPanel from "../components/Admin/AdminLogsPanel";
import FlowEngineAdminPanel from "../components/Admin/Flow/FlowEngineAdminPanel";
import KycAdminDashboard from "../components/Admin/KycAdminDashboard";
import WithdrawalManagementPanel from "../components/Admin/WithdrawalManagement";
import Navbar from '../components/Navbar';
import TreasuryDashboard from '../components/Treasury/TreasuryDashboard';
import { useAuth } from '../context/AuthContext';

/**
 * AdminPage Component
 * Main admin interface with tabs for different administrative functions
 */
const AdminPageContent: React.FC = () => {
    const navigate = useNavigate();
    const { user } = useAuth();
    const [activeTab, setActiveTab] = useState('logs');

    // Ensure user has admin privileges
    if (user?.roles?.includes('Admin')) {
        return (
                <div className="min-h-screen bg-gray-50 flex justify-center items-center p-4">
                    <div className="bg-white rounded-lg shadow-lg p-8 max-w-md w-full text-center">
                        <div className="text-red-500 text-6xl mb-4">
                            <svg className="w-24 h-24 mx-auto" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
                            </svg>
                        </div>
                        <h1 className="text-2xl font-bold text-gray-800 mb-4">Access Denied</h1>
                        <p className="text-gray-600 mb-6">
                            You don't have permission to access this page. Please contact an administrator if you believe this is an error.
                        </p>
                        <button
                            onClick={() => navigate('/dashboard')}
                            className="w-full bg-blue-600 text-white py-3 rounded-lg hover:bg-blue-700 transition-colors"
                        >
                            Return to Dashboard
                        </button>
                    </div>
                </div>
        );
    }

    return (
            <div className="min-h-screen bg-gray-50 pt-20 pb-12 px-4">
                <div className="max-w-6xl mx-auto">
                    <header className="mb-8">
                        <h1 className="text-3xl font-bold text-gray-900">Admin Dashboard</h1>
                        <p className="mt-2 text-lg text-gray-600">
                            Manage and monitor system components
                        </p>
                    </header>

                    {/* Admin navigation tabs */}
                    <div className="border-b border-gray-200 mb-8">
                        <nav className="flex -mb-px">
                            <button
                                onClick={() => setActiveTab('logs')}
                                className={`mr-8 py-4 px-1 border-b-2 font-medium text-sm ${activeTab === 'logs'
                                    ? 'border-blue-500 text-blue-600'
                                    : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                                    }`}
                            >
                                System Logs
                            </button>
                            <button
                                onClick={() => setActiveTab('users')}
                                className={`mr-8 py-4 px-1 border-b-2 font-medium text-sm ${activeTab === 'users'
                                    ? 'border-blue-500 text-blue-600'
                                    : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                                    }`}
                            >
                                User Management
                        </button>
                            <button
                                onClick={() => setActiveTab('flows')}
                                className={`mr-8 py-4 px-1 border-b-2 font-medium text-sm ${activeTab === 'payments'
                                    ? 'border-blue-500 text-blue-600'
                                    : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                                    }`}
                            >
                                Flows
                            </button>
                            <button
                                onClick={() => setActiveTab('kyc')}
                                className={`mr-8 py-4 px-1 border-b-2 font-medium text-sm ${activeTab === 'kyc-dashboard'
                                    ? 'border-blue-500 text-blue-600'
                                    : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                                    }`}
                            >
                                KYC
                            </button>
                            <button
                            onClick={() => setActiveTab('treasury')}
                                className={`mr-8 py-4 px-1 border-b-2 font-medium text-sm ${activeTab === 'kyc-dashboard'
                                    ? 'border-blue-500 text-blue-600'
                                    : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                                    }`}
                            >
                                Treasury
                            </button>
                            <button
                                onClick={() => setActiveTab('withdrawals')}
                                className={`mr-8 py-4 px-1 border-b-2 font-medium text-sm ${activeTab === 'withdrawal'
                                    ? 'border-blue-500 text-blue-600'
                                    : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                                    }`}
                            >
                                Withdrawals
                            </button>
                            <button
                                onClick={() => setActiveTab('settings')}
                                className={`mr-8 py-4 px-1 border-b-2 font-medium text-sm ${activeTab === 'settings'
                                    ? 'border-blue-500 text-blue-600'
                                    : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
                                    }`}
                            >
                                System Settings
                            </button>
                        </nav>
                    </div>

                    {/* Tab content */}
                    <div>
                        {activeTab === 'logs' && <AdminLogsPanel />}
                        {activeTab === 'users' && (
                            <div className="bg-white shadow rounded-lg p-6">
                                <h2 className="text-xl font-semibold mb-4">User Management</h2>
                                <p className="text-gray-500">User management functionality coming soon...</p>
                            </div>
                        )}
                        {activeTab === 'flows' && <FlowEngineAdminPanel />}
                        {activeTab === 'kyc' && <KycAdminDashboard />}
                        {activeTab === 'treasury' && <TreasuryDashboard />}
                        {activeTab === 'withdrawals' && <WithdrawalManagementPanel />}
                        {activeTab === 'settings' && (
                            <div className="bg-white shadow rounded-lg p-6">
                                <h2 className="text-xl font-semibold mb-4">System Settings</h2>
                                <p className="text-gray-500">System settings functionality coming soon...</p>
                            </div>
                        )}
                    </div>
                </div>
            </div>
    );
};

const AdminPage: React.FC = () => {
    return (
        <>
            <Navbar />
            <AdminPageContent />
        </>
    );
};

export default AdminPage;