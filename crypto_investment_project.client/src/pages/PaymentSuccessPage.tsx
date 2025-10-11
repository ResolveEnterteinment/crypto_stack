// src/pages/PaymentSuccessPage.tsx
import React, { useEffect, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import * as paymentService from '../services/payment';
import * as subscriptionService from '../services/subscription';
import { Subscription } from '../types/subscription';
import { PaymentStatusResponse } from '../services/payment';

const PaymentSuccessPage: React.FC = () => {
    const navigate = useNavigate();
    const { user } = useAuth();
    const [searchParams] = useSearchParams();

    const [loading, setLoading] = useState(true);
    const [payment, setPayment] = useState<PaymentStatusResponse | null>(null);
    const [subscription, setSubscription] = useState<Subscription | null>(null);

    // Get parameters from URL
    const subscriptionId = searchParams.get('subscription_id') ?? "";

    // Format date
    const formatDate = (dateString: string | Date): string => {
        return new Date(dateString).toLocaleDateString();
    };

    const fetchSubscription = async (subscriptionId: string) => {
        try {
            setLoading(true);

            var subscriptionData = await subscriptionService.getSubscription(subscriptionId);

            setSubscription(subscriptionData);
        } catch (error) {
            console.error('Error fetching subscription:', error);
        } finally {
            setLoading(false);
        }
    };

    const fetchPaymentDetails = async (subscriptionId: string) => {
        try {
            setLoading(true);

            var paymentData = await paymentService.getSubscriptionPaymentStatus(subscriptionId);

            if (paymentData)
            setPayment(paymentData);
        } catch (error) {
            console.error('Error verifying payment:', error);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        // Check if user is authenticated
        if (!user || !user.id) {
            navigate('/auth');
            return;
        }
        
        fetchSubscription(subscriptionId);
        fetchPaymentDetails(subscriptionId);
        
    }, [user, navigate, subscriptionId, searchParams]);

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
                        <h1 className="text-2xl font-bold text-gray-800">Payment {payment && payment.status == "paid" ? "Successful" : "Failed"}!</h1>
                        <p className="text-gray-600 mt-2">
                            Your investment subscription has been activated successfully.
                        </p>
                    </div>

                    {payment && subscription && (
                        <div className="border-t border-b border-gray-200 py-4 mb-6">
                            <div className="grid grid-cols-2 gap-4">
                                <div>
                                    <p className="text-sm text-gray-500">Amount</p>
                                    <p className="font-medium">${payment.totalAmount} {payment.currency.toUpperCase()}</p>
                                </div>
                                <div>
                                    <p className="text-sm text-gray-500">Status</p>
                                    <p className="font-medium text-green-600">{payment.status.toUpperCase()}</p>
                                </div>
                                <div>
                                    <p className="text-sm text-gray-500">Start Date</p>
                                    <p className="font-medium">{formatDate(subscription.createdAt)}</p>
                                </div>
                                <div>
                                    <p className="text-sm text-gray-500">End Date</p>
                                    <p className="font-medium">{subscription.endDate ? formatDate(subscription.endDate) : "Until Canceled"}</p>
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