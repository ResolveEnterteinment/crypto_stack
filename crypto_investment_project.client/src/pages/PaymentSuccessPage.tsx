// src/pages/PaymentSuccessPage.tsx
import React, { useEffect, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import Navbar from '../components/Navbar';

const PaymentSuccessPage: React.FC = () => {
    const navigate = useNavigate();
    const { user } = useAuth();
    const [searchParams] = useSearchParams();

    const [loading, setLoading] = useState(true);
    const [paymentDetails, setPaymentDetails] = useState<any>(null);

    // Get parameters from URL
    const sessionId = searchParams.get('session_id');
    const subscriptionId = searchParams.get('subscription_id');

    useEffect(() => {
        // Check if user is authenticated
        if (!user || !user.id) {
            navigate('/auth');
            return;
        }

        // Simulate fetching payment confirmation details
        // In a real application, you would make an API call to verify the payment
        const fetchPaymentDetails = async () => {
            try {
                // Simulate API call delay
                await new Promise(resolve => setTimeout(resolve, 1500));

                // Set mock payment details (in a real app, this would come from your backend)
                setPaymentDetails({
                    amount: searchParams.get('amount') || '100.00',
                    currency: searchParams.get('currency') || 'USD',
                    subscriptionId: subscriptionId,
                    date: new Date().toLocaleDateString(),
                    status: 'Successful'
                });

                setLoading(false);
            } catch (error) {
                console.error('Error verifying payment:', error);
                setLoading(false);
            }
        };

        fetchPaymentDetails();
    }, [user, navigate, sessionId, subscriptionId, searchParams]);

    // Handler functions
    const handleViewDashboard = () => {
        navigate('/dashboard');
    };

    const handleManageSubscription = () => {
        navigate('/subscriptions');
    };

    if (loading) {
        return (
            <>
                <Navbar
                    showProfile={() => navigate('/profile')}
                    showSettings={() => navigate('/settings')}
                    logout={() => { }}
                />
                <div className="min-h-screen bg-gray-50 flex justify-center items-center">
                    <div className="text-center">
                        <div className="w-16 h-16 border-4 border-green-500 border-t-transparent rounded-full animate-spin mx-auto mb-4"></div>
                        <p className="text-gray-500">Verifying your payment...</p>
                    </div>
                </div>
            </>
        );
    }

    return (
        <>
            <Navbar
                showProfile={() => navigate('/profile')}
                showSettings={() => navigate('/settings')}
                logout={() => { }}
            />
            <div className="min-h-screen bg-gray-50 flex justify-center items-center p-4">
                <div className="bg-white rounded-lg shadow-lg p-8 max-w-md w-full">
                    <div className="text-center mb-6">
                        <div className="mx-auto bg-green-100 p-3 rounded-full w-16 h-16 flex items-center justify-center mb-4">
                            <svg
                                className="w-8 h-8 text-green-600"
                                fill="none"
                                stroke="currentColor"
                                viewBox="0 0 24 24"
                            >
                                <path
                                    strokeLinecap="round"
                                    strokeLinejoin="round"
                                    strokeWidth="2"
                                    d="M5 13l4 4L19 7"
                                />
                            </svg>
                        </div>
                        <h1 className="text-2xl font-bold text-gray-800">Payment Successful!</h1>
                        <p className="text-gray-600 mt-2">
                            Your investment subscription has been activated successfully.
                        </p>
                    </div>

                    {paymentDetails && (
                        <div className="border-t border-b border-gray-200 py-4 mb-6">
                            <div className="grid grid-cols-2 gap-4">
                                <div>
                                    <p className="text-sm text-gray-500">Amount</p>
                                    <p className="font-medium">${paymentDetails.amount} {paymentDetails.currency}</p>
                                </div>
                                <div>
                                    <p className="text-sm text-gray-500">Date</p>
                                    <p className="font-medium">{paymentDetails.date}</p>
                                </div>
                                <div>
                                    <p className="text-sm text-gray-500">Status</p>
                                    <p className="font-medium text-green-600">{paymentDetails.status}</p>
                                </div>
                                <div>
                                    <p className="text-sm text-gray-500">Subscription ID</p>
                                    <p className="font-medium text-xs truncate">{paymentDetails.subscriptionId}</p>
                                </div>
                            </div>
                        </div>
                    )}

                    <div className="text-center space-y-3">
                        <button
                            onClick={handleViewDashboard}
                            className="w-full bg-blue-600 text-white py-3 rounded-lg hover:bg-blue-700 transition-colors"
                        >
                            View Dashboard
                        </button>
                        <button
                            onClick={handleManageSubscription}
                            className="w-full bg-gray-200 text-gray-800 py-3 rounded-lg hover:bg-gray-300 transition-colors"
                        >
                            Manage Subscription
                        </button>
                    </div>
                </div>
            </div>
        </>
    );
};

export default PaymentSuccessPage;