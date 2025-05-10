import { useState, useEffect } from 'react';
import { PaymentData } from "../../types/payment"
import {
    AlertTriangle,
    RefreshCw,
    Search,
    ChevronDown,
    Filter,
    Download
} from 'lucide-react';

interface PaginatedResponse<T> {
    items: T[];
    totalCount: number;
}

interface ApiResponse<T> {
    data: T;
}

// Admin dashboard component for managing failed payments
const AdminPaymentDashboard = () => {
    const [isLoading, setIsLoading] = useState<boolean>(false);
    const [payments, setPayments] = useState<PaymentData[]>([]);
    const [error, setError] = useState<string | null>(null);
    const [filter, setFilter] = useState<string>('FAILED'); // Default to showing failed payments
    const [searchQuery, setSearchQuery] = useState<string>('');
    const [page, setPage] = useState<number>(1);
    const [totalPages, setTotalPages] = useState<number>(1);
    const [selectedPayment, setSelectedPayment] = useState<PaymentData | null>(null);
    const [showRetryModal, setShowRetryModal] = useState<boolean>(false);

    const pageSize = 10;

    useEffect(() => {
        fetchPayments();
    }, [filter, page]);

    const fetchPayments = async () => {
        setIsLoading(true);
        try {
            const response = await fetch(`/api/admin/payments?status=${filter}&page=${page}&pageSize=${pageSize}&search=${searchQuery}`);
            if (!response.ok) {
                throw new Error('Failed to fetch payments');
            }
            const data: ApiResponse<PaginatedResponse<PaymentData>> = await response.json();
            setPayments(data.data.items || []);
            setTotalPages(Math.ceil(data.data.totalCount / pageSize));
            setError(null);
        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : 'An unknown error occurred';
            setError(errorMessage);
            console.error('Error fetching payments:', err);
        } finally {
            setIsLoading(false);
        }
    };

    const handleRetryPayment = async (paymentId: string) => {
        setIsLoading(true);
        try {
            const response = await fetch(`/api/payment-methods/retry/${paymentId}`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                }
            });

            if (!response.ok) {
                const errorData = await response.json();
                throw new Error(errorData.message || 'Failed to retry payment');
            }

            // Refresh payments
            fetchPayments();
            setShowRetryModal(false);

            // Show success notification
            alert('Payment retry initiated successfully');
        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : 'An unknown error occurred';
            setError(errorMessage);
            console.error('Error retrying payment:', err);
        } finally {
            setIsLoading(false);
        }
    };

    const handleSearch = () => {
        setPage(1); // Reset to first page
        fetchPayments();
    };

    const handleExportCsv = () => {
        // Logic to export payment data to CSV
        const headers = ['ID', 'User ID', 'Subscription ID', 'Amount', 'Currency', 'Status', 'Created At', 'Failed Reason'];

        const csvData = [
            headers.join(','),
            ...payments.map(payment => [
                payment.id,
                payment.userId,
                payment.subscriptionId,
                payment.totalAmount,
                payment.currency,
                payment.status,
                new Date(payment.createdAt).toISOString(),
                payment.failureReason || ''
            ].join(','))
        ].join('\n');

        const blob = new Blob([csvData], { type: 'text/csv;charset=utf-8;' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.setAttribute('download', `payments-${filter}-${new Date().toISOString()}.csv`);
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
    };

    const formatDate = (dateString: string) => {
        const date = new Date(dateString);
        return date.toLocaleDateString() + ' ' + date.toLocaleTimeString();
    };

    const getStatusBadge = (status: string) => {
        const statusColorMap: { [key: string]: string } = {
            'FILLED': 'bg-green-100 text-green-800',
            'PENDING': 'bg-yellow-100 text-yellow-800',
            'FAILED': 'bg-red-100 text-red-800',
            'QUEUED': 'bg-blue-100 text-blue-800'
        };

        const colorClass = statusColorMap[status] || 'bg-gray-100 text-gray-800';

        return (
            <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${colorClass}`}>
                {status}
            </span>
        );
    };

    // Retry payment modal
    const RetryModal = ({
        payment,
        onClose,
        onRetry
    }: {
        payment: PaymentData | null;
        onClose: () => void;
        onRetry: (paymentId: string) => void;
    }) => {
        if (!payment) return null;

        return (
            <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center p-4 z-50">
                <div className="bg-white rounded-lg max-w-md w-full p-6">
                    <h3 className="text-lg font-medium text-gray-900 mb-4">Retry Payment</h3>
                    <p className="text-sm text-gray-500 mb-4">
                        Are you sure you want to retry the following payment?
                    </p>

                    <div className="bg-gray-50 p-4 rounded mb-4">
                        <p><strong>Payment ID:</strong> {payment.id}</p>
                        <p><strong>Subscription:</strong> {payment.subscriptionId}</p>
                        <p><strong>Amount:</strong> {payment.currency} {payment.totalAmount}</p>
                        <p><strong>Failed Reason:</strong> {payment.failureReason || 'N/A'}</p>
                        <p><strong>Attempt Count:</strong> {payment.attemptCount || 0}</p>
                    </div>

                    <div className="flex justify-end space-x-3">
                        <button
                            type="button"
                            onClick={onClose}
                            className="px-4 py-2 border border-gray-300 rounded-md text-sm font-medium text-gray-700 hover:bg-gray-50"
                        >
                            Cancel
                        </button>
                        <button
                            type="button"
                            onClick={() => onRetry(payment.id)}
                            className="px-4 py-2 bg-indigo-600 border border-transparent rounded-md text-sm font-medium text-white hover:bg-indigo-700"
                        >
                            Retry Payment
                        </button>
                    </div>
                </div>
            </div>
        );
    };

    return (
        <div className="bg-white shadow overflow-hidden rounded-lg">
            <div className="px-4 py-5 sm:px-6 border-b border-gray-200">
                <div className="flex flex-col sm:flex-row sm:justify-between sm:items-center space-y-4 sm:space-y-0">
                    <h3 className="text-lg leading-6 font-medium text-gray-900">
                        Payment Management
                    </h3>

                    <div className="flex space-x-3">
                        <button
                            type="button"
                            onClick={fetchPayments}
                            className="inline-flex items-center px-3 py-2 border border-gray-300 shadow-sm text-sm leading-4 font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
                        >
                            <RefreshCw className="h-4 w-4 mr-2" />
                            Refresh
                        </button>

                        <button
                            type="button"
                            onClick={handleExportCsv}
                            className="inline-flex items-center px-3 py-2 border border-gray-300 shadow-sm text-sm leading-4 font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
                        >
                            <Download className="h-4 w-4 mr-2" />
                            Export CSV
                        </button>
                    </div>
                </div>
            </div>

            <div className="px-4 py-4 sm:px-6 border-b border-gray-200 bg-gray-50">
                <div className="flex flex-col sm:flex-row sm:items-center space-y-4 sm:space-y-0 sm:space-x-4">
                    <div className="relative flex-grow">
                        <div className="flex w-full">
                            <div className="relative flex-grow">
                                <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                                    <Search className="h-5 w-5 text-gray-400" />
                                </div>
                                <input
                                    type="text"
                                    className="block w-full pl-10 pr-3 py-2 border border-gray-300 rounded-md leading-5 bg-white shadow-sm focus:outline-none focus:ring-indigo-500 focus:border-indigo-500 sm:text-sm"
                                    placeholder="Search by user ID, subscription ID..."
                                    value={searchQuery}
                                    onChange={(e) => setSearchQuery(e.target.value)}
                                    onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
                                />
                            </div>
                            <button
                                type="button"
                                onClick={handleSearch}
                                className="ml-3 inline-flex items-center px-4 py-2 border border-gray-300 text-sm font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50"
                            >
                                Search
                            </button>
                        </div>
                    </div>

                    <div className="flex-shrink-0">
                        <div className="relative inline-block text-left">
                            <div>
                                <button
                                    type="button"
                                    className="inline-flex justify-center w-full rounded-md border border-gray-300 shadow-sm px-4 py-2 bg-white text-sm font-medium text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-indigo-500"
                                    id="filter-menu-button"
                                    aria-expanded="true"
                                    aria-haspopup="true"
                                    onClick={() => {
                                        const dropdown = document.getElementById('filter-dropdown');
                                        if (dropdown) {
                                            dropdown.classList.toggle('hidden');
                                        }
                                    }}
                                >
                                    <Filter className="h-4 w-4 mr-2" />
                                    {filter || 'All'}
                                    <ChevronDown className="h-4 w-4 ml-2" />
                                </button>
                            </div>

                            <div
                                id="filter-dropdown"
                                className="hidden origin-top-right absolute right-0 mt-2 w-56 rounded-md shadow-lg bg-white ring-1 ring-black ring-opacity-5 divide-y divide-gray-100 focus:outline-none z-10"
                                role="menu"
                                aria-orientation="vertical"
                                aria-labelledby="filter-menu-button"
                                tabIndex={-1}
                            >
                                <div className="py-1" role="none">
                                    {['ALL', 'FAILED', 'PENDING', 'FILLED'].map((status) => (
                                        <button
                                            key={status}
                                            className={`block px-4 py-2 text-sm w-full text-left ${filter === status ? 'bg-gray-100 text-gray-900' : 'text-gray-700'} hover:bg-gray-100`}
                                            role="menuitem"
                                            tabIndex={-1}
                                            onClick={() => {
                                                setFilter(status);
                                                const dropdown = document.getElementById('filter-dropdown');
                                                if (dropdown) {
                                                    dropdown.classList.add('hidden');
                                                }
                                            }}
                                        >
                                            {status}
                                        </button>
                                    ))}
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </div>

            {error && (
                <div className="px-4 py-3 bg-red-50 border-b border-red-200 text-red-700">
                    <div className="flex">
                        <div className="flex-shrink-0">
                            <AlertTriangle className="h-5 w-5 text-red-400" />
                        </div>
                        <div className="ml-3">
                            <p className="text-sm font-medium text-red-800">{error}</p>
                        </div>
                    </div>
                </div>
            )}

            <div className="overflow-x-auto">
                <table className="min-w-full divide-y divide-gray-200">
                    <thead className="bg-gray-50">
                        <tr>
                            <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Payment ID
                            </th>
                            <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Subscription
                            </th>
                            <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Amount
                            </th>
                            <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Status
                            </th>
                            <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Date
                            </th>
                            <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Retry Info
                            </th>
                            <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                Actions
                            </th>
                        </tr>
                    </thead>
                    <tbody className="bg-white divide-y divide-gray-200">
                        {isLoading ? (
                            <tr>
                                <td colSpan={7} className="px-6 py-4 text-center">
                                    <div className="flex justify-center">
                                        <div className="animate-spin rounded-full h-6 w-6 border-b-2 border-indigo-500"></div>
                                    </div>
                                </td>
                            </tr>
                        ) : payments.length === 0 ? (
                            <tr>
                                <td colSpan={7} className="px-6 py-4 text-center text-sm text-gray-500">
                                    No payments found
                                </td>
                            </tr>
                        ) : (
                            payments.map((payment) => (
                                <tr key={payment.id} className="hover:bg-gray-50">
                                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">
                                        {payment.id}
                                    </td>
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                        {payment.subscriptionId}
                                    </td>
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                        {payment.currency} {payment.totalAmount}
                                    </td>
                                    <td className="px-6 py-4 whitespace-nowrap">
                                        {getStatusBadge(payment.status)}
                                    </td>
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                        {formatDate(payment.createdAt)}
                                    </td>
                                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                                        {payment.status === 'FAILED' && (
                                            <div>
                                                <div>Attempts: {payment.attemptCount || 0}</div>
                                                {payment.nextRetryAt && (
                                                    <div>Next retry: {formatDate(payment.nextRetryAt)}</div>
                                                )}
                                            </div>
                                        )}
                                    </td>
                                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium">
                                        {payment.status === 'FAILED' && (
                                            <button
                                                type="button"
                                                onClick={() => {
                                                    setSelectedPayment(payment);
                                                    setShowRetryModal(true);
                                                }}
                                                className="text-indigo-600 hover:text-indigo-900"
                                            >
                                                Retry
                                            </button>
                                        )}
                                    </td>
                                </tr>
                            ))
                        )}
                    </tbody>
                </table>
            </div>

            {totalPages > 1 && (
                <div className="px-4 py-3 flex items-center justify-between border-t border-gray-200 sm:px-6">
                    <div className="flex-1 flex justify-between items-center">
                        <button
                            onClick={() => setPage(Math.max(1, page - 1))}
                            disabled={page === 1}
                            className={`relative inline-flex items-center px-4 py-2 border border-gray-300 text-sm font-medium rounded-md text-gray-700 bg-white ${page === 1 ? 'opacity-50 cursor-not-allowed' : 'hover:bg-gray-50'}`}
                        >
                            Previous
                        </button>
                        <p className="text-sm text-gray-700">
                            Page <span className="font-medium">{page}</span> of <span className="font-medium">{totalPages}</span>
                        </p>
                        <button
                            onClick={() => setPage(Math.min(totalPages, page + 1))}
                            disabled={page === totalPages}
                            className={`relative inline-flex items-center px-4 py-2 border border-gray-300 text-sm font-medium rounded-md text-gray-700 bg-white ${page === totalPages ? 'opacity-50 cursor-not-allowed' : 'hover:bg-gray-50'}`}
                        >
                            Next
                        </button>
                    </div>
                </div>
            )}

            {showRetryModal && selectedPayment && (
                <RetryModal
                    payment={selectedPayment}
                    onClose={() => setShowRetryModal(false)}
                    onRetry={handleRetryPayment}
                />
            )}
        </div>
    );
};

export default AdminPaymentDashboard;