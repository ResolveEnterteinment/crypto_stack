// src/pages/PaymentCancelPage.tsx
import React, { useEffect, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import * as paymentService from '../services/payment';
import { PaymentRequestData } from '../services/payment';
import { LoadingOutlined, RedoOutlined } from '@ant-design/icons';
import { formatApiError } from '../utils/apiErrorHandler';

const PaymentCancelPage: React.FC = () => {
    const navigate = useNavigate();
    const { user } = useAuth();
    const [searchParams] = useSearchParams();
    const [retrying, setRetrying] = useState(false);

    // Get subscription ID from URL parameters
    const paymentData: PaymentRequestData | null = JSON.parse(decodeURIComponent(searchParams.get('payment_data') ?? ""));

    useEffect(() => {
        // Check if user is authenticated
        if (!user || !user.id) {
            navigate('/auth');
        }
    }, [user, navigate]);

    // Handler functions
    const handleRetry = async () => {
        // If we have the subscription ID, navigate back to the edit page
        try {
            setRetrying(true);
            var checkoutUrl = await paymentService.initiatePayment(paymentData!);
            // Redirect to checkout
            window.location.href = checkoutUrl;
        } catch (error: any) {
            console.error('Payment retry error:', error);
            // Format user-friendly error message
            const errorMessage = formatApiError(error);
            throw new Error(`Payment retry failed: ${errorMessage}`);
        } finally {
            setRetrying(false);
        }
    };

    const handleBackToDashboard = () => {
        navigate('/dashboard');
    };

    return (
        <>
            <div className="min-h-screen bg-gray-50 flex justify-center items-center p-4">
                <div className="bg-white rounded-lg shadow-lg p-8 max-w-md w-full">
                    <div className="text-center mb-6">
                        <div className="mx-auto bg-red-100 p-3 rounded-full w-16 h-16 flex items-center justify-center mb-4">
                            <svg
                                className="w-8 h-8 text-red-600"
                                fill="none"
                                stroke="currentColor"
                                viewBox="0 0 24 24"
                            >
                                <path
                                    strokeLinecap="round"
                                    strokeLinejoin="round"
                                    strokeWidth="2"
                                    d="M6 18L18 6M6 6l12 12"
                                />
                            </svg>
                        </div>
                        <h1 className="text-2xl font-bold text-gray-800">Payment Cancelled</h1>
                        <p className="text-gray-600 mt-2">
                            Your investment subscription has not been activated because the payment was cancelled.
                        </p>
                    </div>

                    <div className="bg-yellow-50 border-l-4 border-yellow-400 p-4 mb-6">
                        <div className="flex">
                            <div className="flex-shrink-0">
                                <svg
                                    className="h-5 w-5 text-yellow-400"
                                    viewBox="0 0 20 20"
                                    fill="currentColor"
                                >
                                    <path
                                        fillRule="evenodd"
                                        d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z"
                                        clipRule="evenodd"
                                    />
                                </svg>
                            </div>
                            <div className="ml-3">
                                <p className="text-sm text-yellow-700">
                                    No charges have been made to your payment method.
                                </p>
                            </div>
                        </div>
                    </div>

                    <div className="text-center space-y-3">
                        <button
                            onClick={handleRetry}
                            className="w-full bg-blue-600 text-white py-3 rounded-lg hover:bg-blue-700 transition-colors"
                        >
                            {retrying ? (
                                <>
                                    <LoadingOutlined /> Trying again...
                                </>) : (
                                <>
                                    <RedoOutlined className="mr-1" />
                                    Try Again
                                </>
                            )}
                        </button>
                        <button
                            onClick={handleBackToDashboard}
                            className="w-full bg-gray-200 text-gray-800 py-3 rounded-lg hover:bg-gray-300 transition-colors"
                        >
                            Back to Dashboard
                        </button>
                    </div>
                </div>
            </div>
        </>
    );
};

export default PaymentCancelPage;
