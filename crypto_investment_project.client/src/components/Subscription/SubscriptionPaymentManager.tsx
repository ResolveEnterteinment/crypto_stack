import { useState, useEffect } from 'react';
import { PaymentData } from "../../types/payment";
import { SubscriptionData } from '../../types/subscription';
import { AlertTriangle, Check, AlertCircle, Clock, CreditCard } from 'lucide-react';

interface ApiResponse<T> {
    data: T | null;
}

// Payment status component for displaying subscription payment status
const PaymentStatusCard = ({
    subscription,
    onUpdatePaymentMethod
}: {
    subscription: SubscriptionData | null;
    onUpdatePaymentMethod: (subscriptionId: string) => void;
}) => {
    const [isLoading, setIsLoading] = useState<boolean>(false);
    const [payments, setPayments] = useState<PaymentData[]>([]);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        if (subscription?.id) {
            fetchPaymentData();
        }
    }, [subscription]);

    const fetchPaymentData = async () => {
        setIsLoading(true);
        try {
            const response = await fetch(`/api/payments/subscription/${subscription?.id}`);
            if (!response.ok) {
                throw new Error('Failed to fetch payment data');
            }
            const data: ApiResponse<PaymentData[]> = await response.json();
            setPayments(data.data || []);
            setError(null);
        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : 'An unknown error occurred';
            setError(errorMessage);
            console.error('Error fetching payment data:', err);
        } finally {
            setIsLoading(false);
        }
    };

    const handleUpdatePaymentMethod = async () => {
        if (onUpdatePaymentMethod && subscription) {
            onUpdatePaymentMethod(subscription.id);
        }
    };

    const getStatusIcon = (status: string) => {
        switch (status) {
            case 'FILLED':
                return <Check className="h-5 w-5 text-green-500" />;
            case 'PENDING':
                return <Clock className="h-5 w-5 text-yellow-500" />;
            case 'FAILED':
                return <AlertTriangle className="h-5 w-5 text-red-500" />;
            default:
                return <AlertCircle className="h-5 w-5 text-gray-500" />;
        }
    };

    const getSubscriptionStatusBadge = () => {
        const status = subscription?.status || 'UNKNOWN';

        const statusColorMap: { [key: string]: string } = {
            'ACTIVE': 'bg-green-100 text-green-800',
            'PENDING': 'bg-yellow-100 text-yellow-800',
            'CANCELED': 'bg-gray-100 text-gray-800',
            'SUSPENDED': 'bg-red-100 text-red-800',
            'UNKNOWN': 'bg-gray-100 text-gray-800'
        };

        const colorClass = statusColorMap[status] || 'bg-gray-100 text-gray-800';

        return (
            <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${colorClass}`}>
                {status}
            </span>
        );
    };

    // Check if the subscription is suspended
    const isSuspended = subscription?.status === 'SUSPENDED';

    // Check if there are any failed payments
    const hasFailedPayments = payments.some(payment => payment.status === 'FAILED');

    return (
        <div className="bg-white shadow rounded-lg overflow-hidden">
            <div className="p-5 border-b border-gray-200">
                <div className="flex justify-between items-center">
                    <h3 className="text-lg font-medium text-gray-900">Subscription Payment Status</h3>
                    {getSubscriptionStatusBadge()}
                </div>
            </div>

            {(isSuspended || hasFailedPayments) && (
                <div className="p-4 bg-red-50 border-l-4 border-red-400">
                    <div className="flex">
                        <div className="flex-shrink-0">
                            <AlertTriangle className="h-5 w-5 text-red-400" />
                        </div>
                        <div className="ml-3">
                            <h3 className="text-sm font-medium text-red-800">
                                {isSuspended
                                    ? 'Your subscription is suspended due to payment issues.'
                                    : 'We had trouble processing your last payment.'}
                            </h3>
                            <div className="mt-2 text-sm text-red-700">
                                <p>
                                    Please update your payment method to continue your subscription.
                                </p>
                            </div>
                            <div className="mt-4">
                                <button
                                    type="button"
                                    onClick={handleUpdatePaymentMethod}
                                    className="inline-flex items-center px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-red-600 hover:bg-red-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-red-500"
                                >
                                    <CreditCard className="mr-2 h-4 w-4" />
                                    Update Payment Method
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            )}

            <div className="p-5">
                {isLoading ? (
                    <div className="text-center py-4">
                        <div className="animate-pulse flex justify-center">
                            <div className="h-4 w-32 bg-gray-200 rounded"></div>
                        </div>
                    </div>
                ) : error ? (
                    <div className="text-center text-red-500 py-4">{error}</div>
                ) : payments.length === 0 ? (
                    <div className="text-center text-gray-500 py-4">No payment records found</div>
                ) : (
                    <div className="flow-root">
                        <ul className="divide-y divide-gray-200">
                            {payments.map((payment) => (
                                <li key={payment.id} className="py-4">
                                    <div className="flex items-center space-x-4">
                                        <div className="flex-shrink-0">
                                            {getStatusIcon(payment.status)}
                                        </div>
                                        <div className="flex-1 min-w-0">
                                            <p className="text-sm font-medium text-gray-900 truncate">
                                                {new Date(payment.createdAt).toLocaleDateString()} - {payment.currency} {payment.totalAmount}
                                            </p>
                                            {payment.status === 'FAILED' && (
                                                <p className="text-sm text-red-500 truncate">
                                                    {payment.failureReason || 'Payment failed'}
                                                    {payment.nextRetryAt && (
                                                        <span className="ml-2">
                                                            Next retry: {new Date(payment.nextRetryAt).toLocaleDateString()}
                                                        </span>
                                                    )}
                                                </p>
                                            )}
                                        </div>
                                        <div className="inline-flex items-center text-sm text-gray-500">
                                            {payment.status}
                                        </div>
                                    </div>
                                </li>
                            ))}
                        </ul>
                    </div>
                )}
            </div>

            <div className="px-5 py-4 bg-gray-50 border-t border-gray-200">
                <div className="flex justify-between text-sm">
                    <button
                        type="button"
                        onClick={handleUpdatePaymentMethod}
                        className="inline-flex items-center text-indigo-600 hover:text-indigo-900"
                    >
                        <CreditCard className="mr-1 h-4 w-4" />
                        Update Payment Method
                    </button>
                    <button
                        type="button"
                        onClick={fetchPaymentData}
                        className="inline-flex items-center text-gray-600 hover:text-gray-900"
                    >
                        Refresh
                    </button>
                </div>
            </div>
        </div>
    );
};

// Main component that handles payment update flow
const SubscriptionPaymentManager = ({ subscription }: { subscription: SubscriptionData | null }) => {
    const [updateUrl, setUpdateUrl] = useState<string | null>(null);
    const [isLoading, setIsLoading] = useState<boolean>(false);
    const [error, setError] = useState<string | null>(null);

    const handleUpdatePaymentMethod = async (subscriptionId: string) => {
        setIsLoading(true);
        setError(null);

        try {
            const response = await fetch(`/api/payment-methods/update/${subscriptionId}`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                }
            });

            if (!response.ok) {
                throw new Error('Failed to create payment update session');
            }

            const data = await response.json();

            if (data.url) {
                setUpdateUrl(data.url);
                window.location.href = data.url; // Redirect to Stripe
            } else {
                throw new Error('No redirect URL returned');
            }
        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : 'An unknown error occurred';
            setError(errorMessage);
            console.error('Error creating payment update session:', err);
        } finally {
            setIsLoading(false);
        }
    };

    const handleReactivateSubscription = async (subscriptionId: string) => {
        setIsLoading(true);
        setError(null);

        try {
            const response = await fetch(`/api/subscription-management/reactivate/${subscriptionId}`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                }
            });

            if (!response.ok) {
                const errorData = await response.json();
                throw new Error(errorData.message || 'Failed to reactivate subscription');
            }

            // Refresh the page to update status
            window.location.reload();
        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : 'An unknown error occurred';
            setError(errorMessage);
            console.error('Error reactivating subscription:', err);
        } finally {
            setIsLoading(false);
        }
    };

    return (
        <div>
            {error && (
                <div className="mb-4 p-4 bg-red-50 border-l-4 border-red-400 text-red-700">
                    {error}
                </div>
            )}

            {isLoading ? (
                <div className="text-center py-4">
                    <div className="animate-pulse flex justify-center">
                        <div className="h-6 w-48 bg-gray-200 rounded"></div>
                    </div>
                </div>
            ) : (
                <PaymentStatusCard
                    subscription={subscription}
                    onUpdatePaymentMethod={handleUpdatePaymentMethod}
                />
            )}

            {subscription?.status === 'SUSPENDED' && (
                <div className="mt-4 px-4 py-3 bg-yellow-50 rounded-lg">
                    <div className="flex">
                        <div className="flex-shrink-0">
                            <AlertCircle className="h-5 w-5 text-yellow-400" />
                        </div>
                        <div className="ml-3">
                            <h3 className="text-sm font-medium text-yellow-800">
                                Subscription Suspended
                            </h3>
                            <div className="mt-2 text-sm text-yellow-700">
                                <p>
                                    After updating your payment method, you can reactivate your subscription.
                                </p>
                            </div>
                            <div className="mt-4">
                                <button
                                    type="button"
                                    onClick={() => handleReactivateSubscription(subscription.id)}
                                    disabled={isLoading}
                                    className="inline-flex items-center px-4 py-2 border border-transparent rounded-md shadow-sm text-sm font-medium text-white bg-yellow-600 hover:bg-yellow-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-yellow-500 disabled:opacity-50"
                                >
                                    Reactivate Subscription
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
};

export default SubscriptionPaymentManager;