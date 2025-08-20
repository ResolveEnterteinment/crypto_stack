import {
    Activity,
    AlertCircle,
    AlertTriangle,
    ArrowRight,
    BarChart3,
    CheckCircle,
    ChevronDown,
    ChevronRight,
    Clock,
    DollarSign,
    Download,
    FileText,
    Info,
    Loader2,
    RefreshCw,
    Search,
    TrendingUp,
    XCircle
} from 'lucide-react';
import React, { useEffect, useState } from 'react';
import paymentFlowService, {
    FailedPaymentDto,
    PaymentFlowDto,
    PaymentFlowMetricsDto,
    PaymentFlowQuery,
    PaymentFlowSummaryDto
} from '../../services/paymentFlowService';

const AdminPaymentTracker: React.FC = () => {
    // State management - using summary data for the list
    const [paymentSummaries, setPaymentSummaries] = useState<PaymentFlowSummaryDto[]>([]);
    const [metrics, setMetrics] = useState<PaymentFlowMetricsDto | null>(null);
    const [failedPayments, setFailedPayments] = useState<FailedPaymentDto[]>([]);
    const [loading, setLoading] = useState(false);
    const [searchQuery, setSearchQuery] = useState('');
    const [statusFilter, setStatusFilter] = useState<string>('ALL');
    const [dateRange, setDateRange] = useState({ start: '', end: '' });
    const [expandedRows, setExpandedRows] = useState<Set<string>>(new Set());
    const [page, setPage] = useState(1);
    const [pageSize] = useState(20);
    const [totalCount, setTotalCount] = useState(0);
    const [refreshing, setRefreshing] = useState(false);
    const [error, setError] = useState<string | null>(null);

    // Individual payment flow states
    const [loadingFlowDetails, setLoadingFlowDetails] = useState<Set<string>>(new Set());
    const [flowDetailsCache, setFlowDetailsCache] = useState<Map<string, PaymentFlowDto>>(new Map());
    const [retryingPayments, setRetryingPayments] = useState<Set<string>>(new Set());
    const [reconcilingPayments, setReconcilingPayments] = useState<Set<string>>(new Set());

    // Load initial data (summaries only, not full flows)
    useEffect(() => {
        loadData();
    }, [page, statusFilter, dateRange]);

    // Auto-refresh metrics every 30 seconds
    useEffect(() => {
        const interval = setInterval(() => {
            loadMetrics();
        }, 30000);

        return () => clearInterval(interval);
    }, []);

    const loadData = async () => {
        setLoading(true);
        setError(null);

        try {
            // Load all data in parallel
            await Promise.all([
                loadPaymentSummaries(), // Only load summaries, not full flows
                loadMetrics(),
                loadFailedPayments()
            ]);
        } catch (err) {
            setError(err instanceof Error ? err.message : 'Failed to load data');
        } finally {
            setLoading(false);
        }
    };

    // Load payment summaries (lightweight list data)
    const loadPaymentSummaries = async () => {
        const query: PaymentFlowQuery = {
            page,
            pageSize,
            status: statusFilter !== 'ALL' ? statusFilter : undefined,
            startDate: dateRange.start || undefined,
            endDate: dateRange.end || undefined,
            searchTerm: searchQuery || undefined,
        };

        const result = await paymentFlowService.getPaymentFlows(query);
        console.log("loadPaymentSummaries => result: ", result);
        setPaymentSummaries(result.items);
        setTotalCount(result.totalCount);
    };

    const loadMetrics = async () => {
        try {
            const metricsData = await paymentFlowService.getMetrics();
            setMetrics(metricsData);
        } catch (err) {
            console.error('Failed to load metrics:', err);
        }
    };

    const loadFailedPayments = async () => {
        try {
            const failed = await paymentFlowService.getFailedPayments(10);
            setFailedPayments(failed);
        } catch (err) {
            console.error('Failed to load failed payments:', err);
        }
    };

    // Load full payment flow details only when needed (when expanding a row)
    const loadPaymentFlowDetails = async (paymentId: string): Promise<PaymentFlowDto | null> => {
        // Check cache first
        if (flowDetailsCache.has(paymentId)) {
            return flowDetailsCache.get(paymentId)!;
        }

        // Add to loading set
        setLoadingFlowDetails(prev => new Set(prev).add(paymentId));

        try {
            const flow = await paymentFlowService.getPaymentFlow(paymentId);

            // Update cache
            const newCache = new Map(flowDetailsCache);
            newCache.set(paymentId, flow);
            setFlowDetailsCache(newCache);

            return flow;
        } catch (error) {
            console.error(`Failed to load flow details for ${paymentId}:`, error);
            setError(`Failed to load payment details: ${error instanceof Error ? error.message : 'Unknown error'}`);
            return null;
        } finally {
            // Remove from loading set
            setLoadingFlowDetails(prev => {
                const newSet = new Set(prev);
                newSet.delete(paymentId);
                return newSet;
            });
        }
    };

    const handleRefresh = async () => {
        setRefreshing(true);
        try {
            await loadData();
            // Clear cache on refresh to get fresh data
            setFlowDetailsCache(new Map());
        } finally {
            setTimeout(() => setRefreshing(false), 500);
        }
    };

    const handleSearch = () => {
        setPage(1); // Reset to first page
        loadPaymentSummaries();
    };

    const handleRetryPayment = async (paymentId: string) => {
        setRetryingPayments(prev => new Set(prev).add(paymentId));

        try {
            await paymentFlowService.retryPaymentProcessing(paymentId);

            // Refresh the summary list
            await loadPaymentSummaries();

            // Clear cache for this payment to force reload if expanded
            const newCache = new Map(flowDetailsCache);
            newCache.delete(paymentId);
            setFlowDetailsCache(newCache);

            // Show success message
            alert('Payment retry initiated successfully');
        } catch (err) {
            alert(`Failed to retry payment: ${err instanceof Error ? err.message : 'Unknown error'}`);
        } finally {
            setRetryingPayments(prev => {
                const newSet = new Set(prev);
                newSet.delete(paymentId);
                return newSet;
            });
        }
    };

    const handleReconcilePayment = async (paymentId: string) => {
        setReconcilingPayments(prev => new Set(prev).add(paymentId));

        try {
            const result = await paymentFlowService.reconcilePayment(paymentId);

            if (result.success) {
                // Refresh data
                await loadPaymentSummaries();

                // Clear cache
                const newCache = new Map(flowDetailsCache);
                newCache.delete(paymentId);
                setFlowDetailsCache(newCache);

                alert('Payment reconciled successfully');
            } else {
                alert(`Reconciliation failed: ${result.message || 'Manual investigation required'}`);
            }
        } catch (err) {
            alert(`Failed to reconcile payment: ${err instanceof Error ? err.message : 'Unknown error'}`);
        } finally {
            setReconcilingPayments(prev => {
                const newSet = new Set(prev);
                newSet.delete(paymentId);
                return newSet;
            });
        }
    };

    const handleExport = async () => {
        try {
            const blob = await paymentFlowService.exportReport({
                status: statusFilter !== 'ALL' ? statusFilter : undefined,
                startDate: dateRange.start || undefined,
                endDate: dateRange.end || undefined,
            });

            // Create download link
            const url = window.URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = `payment-flow-report-${new Date().toISOString()}.csv`;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            window.URL.revokeObjectURL(url);
        } catch (err) {
            alert(`Failed to export report: ${err instanceof Error ? err.message : 'Unknown error'}`);
        }
    };

    const toggleRowExpansion = async (paymentId: string) => {
        const newExpanded = new Set(expandedRows);

        if (newExpanded.has(paymentId)) {
            // Collapse row
            newExpanded.delete(paymentId);
            setExpandedRows(newExpanded);
        } else {
            // Expand row - load full details if not cached
            const flow = await loadPaymentFlowDetails(paymentId);
            if (flow) {
                newExpanded.add(paymentId);
                setExpandedRows(newExpanded);
            }
        }
    };

    const getStatusIcon = (status: string) => {
        switch (status.toUpperCase()) {
            case 'COMPLETED':
            case 'FILLED':
            case 'COMPLETE':
                return <CheckCircle className="w-4 h-4 text-green-500" />;
            case 'PROCESSING':
            case 'PENDING':
                return <Clock className="w-4 h-4 text-yellow-500" />;
            case 'FAILED':
                return <XCircle className="w-4 h-4 text-red-500" />;
            case 'PARTIAL':
                return <AlertTriangle className="w-4 h-4 text-orange-500" />;
            default:
                return <Info className="w-4 h-4 text-gray-500" />;
        }
    };

    const getStatusBadge = (status: string) => {
        const statusColors: Record<string, string> = {
            'COMPLETED': 'bg-green-100 text-green-800',
            'FILLED': 'bg-green-100 text-green-800',
            'COMPLETE': 'bg-green-100 text-green-800',
            'PROCESSING': 'bg-yellow-100 text-yellow-800',
            'PENDING': 'bg-gray-100 text-gray-800',
            'FAILED': 'bg-red-100 text-red-800',
            'PARTIAL': 'bg-orange-100 text-orange-800',
            'REFUNDED': 'bg-purple-100 text-purple-800'
        };

        return (
            <span className={`inline-flex items-center gap-1 px-2 py-1 rounded-full text-xs font-medium ${statusColors[status.toUpperCase()] || 'bg-gray-100 text-gray-800'}`}>
                {getStatusIcon(status)}
                {status}
            </span>
        );
    };

    const formatCurrency = (amount: number, currency: string = 'USD') => {
        return new Intl.NumberFormat('en-US', {
            style: 'currency',
            currency: currency,
        }).format(amount);
    };

    const formatDate = (dateString: string) => {
        return new Date(dateString).toLocaleString();
    };

    const totalPages = Math.ceil(totalCount / pageSize);

    return (
        <div className="min-h-screen bg-gray-50 p-6">
            <div className="max-w-7xl mx-auto">
                {/* Header */}
                <div className="mb-8">
                    <h1 className="text-3xl font-bold text-gray-900 mb-2">Payment Flow Tracker</h1>
                    <p className="text-gray-600">Monitor and track payment processing from initiation to exchange order fulfillment</p>
                </div>

                {/* Error Alert */}
                {error && (
                    <div className="mb-6 bg-red-50 border border-red-200 rounded-lg p-4">
                        <div className="flex items-center gap-2">
                            <AlertCircle className="w-5 h-5 text-red-600" />
                            <p className="text-red-800">{error}</p>
                            <button
                                onClick={() => setError(null)}
                                className="ml-auto text-red-600 hover:text-red-800"
                            >
                                ×
                            </button>
                        </div>
                    </div>
                )}

                {/* Metrics Cards */}
                {metrics && (
                    <div className="grid grid-cols-1 md:grid-cols-5 gap-4 mb-6">
                        <div className="bg-white rounded-lg shadow p-4">
                            <div className="flex items-center justify-between">
                                <div>
                                    <p className="text-sm text-gray-600">Today's Volume</p>
                                    <p className="text-2xl font-bold text-gray-900">
                                        {formatCurrency(metrics.todayVolume)}
                                    </p>
                                    <p className="text-xs text-gray-500 mt-1">
                                        {metrics.todayPaymentCount} payments
                                    </p>
                                </div>
                                <DollarSign className="w-8 h-8 text-green-500" />
                            </div>
                        </div>

                        <div className="bg-white rounded-lg shadow p-4">
                            <div className="flex items-center justify-between">
                                <div>
                                    <p className="text-sm text-gray-600">Success Rate</p>
                                    <p className="text-2xl font-bold text-green-600">
                                        {metrics.todaySuccessRate.toFixed(1)}%
                                    </p>
                                    <p className="text-xs text-gray-500 mt-1">Today</p>
                                </div>
                                <TrendingUp className="w-8 h-8 text-green-500" />
                            </div>
                        </div>

                        <div className="bg-white rounded-lg shadow p-4">
                            <div className="flex items-center justify-between">
                                <div>
                                    <p className="text-sm text-gray-600">Pending</p>
                                    <p className="text-2xl font-bold text-yellow-600">
                                        {metrics.pendingPayments}
                                    </p>
                                    <p className="text-xs text-gray-500 mt-1">
                                        {formatCurrency(metrics.pendingAmount)}
                                    </p>
                                </div>
                                <Clock className="w-8 h-8 text-yellow-500" />
                            </div>
                        </div>

                        <div className="bg-white rounded-lg shadow p-4">
                            <div className="flex items-center justify-between">
                                <div>
                                    <p className="text-sm text-gray-600">Failed (24h)</p>
                                    <p className="text-2xl font-bold text-red-600">
                                        {metrics.failedLast24Hours}
                                    </p>
                                    <p className="text-xs text-gray-500 mt-1">
                                        {formatCurrency(metrics.failedAmount)}
                                    </p>
                                </div>
                                <XCircle className="w-8 h-8 text-red-500" />
                            </div>
                        </div>

                        <div className="bg-white rounded-lg shadow p-4">
                            <div className="flex items-center justify-between">
                                <div>
                                    <p className="text-sm text-gray-600">Avg Process Time</p>
                                    <p className="text-2xl font-bold text-gray-900">
                                        {metrics.averageProcessingTimeSeconds.toFixed(1)}s
                                    </p>
                                    <p className="text-xs text-gray-500 mt-1">
                                        Median: {metrics.medianProcessingTimeSeconds.toFixed(1)}s
                                    </p>
                                </div>
                                <Activity className="w-8 h-8 text-blue-500" />
                            </div>
                        </div>
                    </div>
                )}

                {/* Failed Payments Alert */}
                {failedPayments.length > 0 && (
                    <div className="mb-6 bg-red-50 border border-red-200 rounded-lg p-4">
                        <div className="flex items-center justify-between mb-3">
                            <div className="flex items-center gap-2">
                                <AlertTriangle className="w-5 h-5 text-red-600" />
                                <h3 className="font-semibold text-red-900">Failed Payments Requiring Attention</h3>
                            </div>
                        </div>
                        <div className="space-y-2 max-h-40 overflow-y-auto">
                            {failedPayments.slice(0, 5).map((payment) => (
                                <div key={payment.paymentId} className="flex items-center justify-between bg-white p-2 rounded">
                                    <div className="flex-1">
                                        <span className="text-sm font-medium">{payment.paymentId}</span>
                                        <span className="text-sm text-gray-500 ml-2">
                                            {formatCurrency(payment.amount, payment.currency)}
                                        </span>
                                        <span className="text-xs text-red-600 ml-2">{payment.failureStage}</span>
                                    </div>
                                    <div className="flex items-center gap-2">
                                        <span className="text-xs text-gray-500">{payment.recommendedAction}</span>
                                        {payment.canRetry && (
                                            <button
                                                onClick={() => handleRetryPayment(payment.paymentId)}
                                                disabled={retryingPayments.has(payment.paymentId)}
                                                className="px-2 py-1 text-xs bg-red-600 text-white rounded hover:bg-red-700 disabled:opacity-50"
                                            >
                                                {retryingPayments.has(payment.paymentId) ? (
                                                    <Loader2 className="w-3 h-3 animate-spin" />
                                                ) : (
                                                    'Retry'
                                                )}
                                            </button>
                                        )}
                                    </div>
                                </div>
                            ))}
                        </div>
                    </div>
                )}

                {/* Filters and Search */}
                <div className="bg-white rounded-lg shadow p-4 mb-6">
                    <div className="flex flex-wrap gap-4 items-center">
                        <div className="flex-1 min-w-[300px]">
                            <div className="relative">
                                <Search className="absolute left-3 top-1/2 transform -translate-y-1/2 text-gray-400 w-5 h-5" />
                                <input
                                    type="text"
                                    placeholder="Search by Payment ID or Provider ID..."
                                    className="w-full pl-10 pr-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                                    value={searchQuery}
                                    onChange={(e) => setSearchQuery(e.target.value)}
                                    onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
                                />
                            </div>
                        </div>

                        <select
                            className="px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                            value={statusFilter}
                            onChange={(e) => {
                                setStatusFilter(e.target.value);
                                setPage(1); // Reset to first page
                            }}
                        >
                            <option value="ALL">All Status</option>
                            <option value="Pending">Pending</option>
                            <option value="Processing">Processing</option>
                            <option value="Completed">Completed</option>
                            <option value="Failed">Failed</option>
                            <option value="Refunded">Refunded</option>
                        </select>

                        <input
                            type="date"
                            className="px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                            value={dateRange.start}
                            onChange={(e) => setDateRange({ ...dateRange, start: e.target.value })}
                        />

                        <input
                            type="date"
                            className="px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500"
                            value={dateRange.end}
                            onChange={(e) => setDateRange({ ...dateRange, end: e.target.value })}
                        />

                        <button
                            onClick={handleRefresh}
                            className={`px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 flex items-center gap-2 ${refreshing ? 'opacity-50' : ''}`}
                            disabled={refreshing}
                        >
                            <RefreshCw className={`w-4 h-4 ${refreshing ? 'animate-spin' : ''}`} />
                            Refresh
                        </button>

                        <button
                            onClick={handleExport}
                            className="px-4 py-2 border border-gray-300 rounded-lg hover:bg-gray-50 flex items-center gap-2"
                        >
                            <Download className="w-4 h-4" />
                            Export
                        </button>
                    </div>
                </div>

                {/* Payment Summary Table */}
                <div className="bg-white rounded-lg shadow overflow-hidden">
                    {loading ? (
                        <div className="flex justify-center items-center h-64">
                            <Loader2 className="w-12 h-12 text-blue-600 animate-spin" />
                        </div>
                    ) : paymentSummaries.length === 0 ? (
                        <div className="text-center py-12">
                            <FileText className="w-12 h-12 text-gray-400 mx-auto mb-4" />
                            <p className="text-gray-500">No payment flows found</p>
                        </div>
                    ) : (
                        <div className="overflow-x-auto">
                            <table className="min-w-full divide-y divide-gray-200">
                                <thead className="bg-gray-50">
                                    <tr>
                                        <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                            Payment Details
                                        </th>
                                        <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                            Amount
                                        </th>
                                        <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                            Status
                                        </th>
                                        <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                            Allocations
                                        </th>
                                        <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                            Orders
                                        </th>
                                        <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                            Reconciliation
                                        </th>
                                        <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                            Actions
                                        </th>
                                    </tr>
                                </thead>
                                <tbody className="bg-white divide-y divide-gray-200">
                                    {paymentSummaries.map((summary) => {
                                        const isExpanded = expandedRows.has(summary.paymentId);
                                        const isLoadingDetails = loadingFlowDetails.has(summary.paymentId);
                                        const flowDetails = flowDetailsCache.get(summary.paymentId);

                                        return (
                                            <React.Fragment key={summary.paymentId}>
                                                <tr className="hover:bg-gray-50">
                                                    <td className="px-6 py-4">
                                                        <div>
                                                            <div className="text-sm font-medium text-gray-900">{summary.paymentId}</div>
                                                            <div className="text-xs text-gray-500">Provider: {summary.paymentProviderId}</div>
                                                            <div className="text-xs text-gray-500">{formatDate(summary.createdAt)}</div>
                                                        </div>
                                                    </td>
                                                    <td className="px-6 py-4">
                                                        <div className="text-sm font-medium text-gray-900">
                                                            {formatCurrency(summary.amount, summary.currency)}
                                                        </div>
                                                    </td>
                                                    <td className="px-6 py-4">
                                                        {getStatusBadge(summary.status)}
                                                    </td>
                                                    <td className="px-6 py-4">
                                                        <div className="flex items-center gap-2">
                                                            <div className="text-sm">
                                                                {summary.allocationsCompleted}/{summary.allocationCount}
                                                            </div>
                                                            <div className="w-20 bg-gray-200 rounded-full h-2">
                                                                <div
                                                                    className="bg-green-500 h-2 rounded-full"
                                                                    style={{
                                                                        width: `${summary.allocationCount > 0 ? (summary.allocationsCompleted / summary.allocationCount) * 100 : 0}%`
                                                                    }}
                                                                />
                                                            </div>
                                                        </div>
                                                    </td>
                                                    <td className="px-6 py-4">
                                                        <div className="flex items-center gap-2">
                                                            <div className="text-sm">
                                                                {summary.ordersCompleted}/{summary.orderCount}
                                                            </div>
                                                            <div className="w-20 bg-gray-200 rounded-full h-2">
                                                                <div
                                                                    className="bg-blue-500 h-2 rounded-full"
                                                                    style={{
                                                                        width: `${summary.orderCount > 0 ? (summary.ordersCompleted / summary.orderCount) * 100 : 0}%`
                                                                    }}
                                                                />
                                                            </div>
                                                            {summary.ordersFailed > 0 && (
                                                                <span className="text-xs text-red-600">
                                                                    ({summary.ordersFailed} failed)
                                                                </span>
                                                            )}
                                                        </div>
                                                    </td>
                                                    <td className="px-6 py-4">
                                                        {getStatusBadge(summary.reconciliationStatus)}
                                                    </td>
                                                    <td className="px-6 py-4">
                                                        <div className="flex items-center gap-2">
                                                            <button
                                                                onClick={() => toggleRowExpansion(summary.paymentId)}
                                                                className="text-blue-600 hover:text-blue-800 flex items-center gap-1"
                                                                disabled={isLoadingDetails}
                                                            >
                                                                {isLoadingDetails ? (
                                                                    <>
                                                                        <Loader2 className="w-4 h-4 animate-spin" />
                                                                        Loading...
                                                                    </>
                                                                ) : isExpanded ? (
                                                                    <>
                                                                        <ChevronDown className="w-4 h-4" />
                                                                        Hide
                                                                    </>
                                                                ) : (
                                                                    <>
                                                                        <ChevronRight className="w-4 h-4" />
                                                                        Details
                                                                    </>
                                                                )}
                                                            </button>

                                                            {/* Quick actions for failed payments */}
                                                            {summary.status === 'Failed' && (
                                                                <button
                                                                    onClick={() => handleRetryPayment(summary.paymentId)}
                                                                    disabled={retryingPayments.has(summary.paymentId)}
                                                                    className="text-red-600 hover:text-red-800"
                                                                    title="Retry Payment"
                                                                >
                                                                    {retryingPayments.has(summary.paymentId) ? (
                                                                        <Loader2 className="w-4 h-4 animate-spin" />
                                                                    ) : (
                                                                        <RefreshCw className="w-4 h-4" />
                                                                    )}
                                                                </button>
                                                            )}
                                                        </div>
                                                    </td>
                                                </tr>

                                                {/* Expanded Details Row - Only render if we have the flow details */}
                                                {isExpanded && flowDetails && (
                                                    <tr>
                                                        <td colSpan={7} className="px-6 py-4 bg-gray-50">
                                                            <div className="space-y-4">
                                                                {/* Payment Flow Visualization */}
                                                                <div className="bg-white rounded-lg p-4">
                                                                    <h3 className="font-semibold text-gray-900 mb-3">Payment Flow Visualization</h3>
                                                                    <div className="flex items-center justify-between overflow-x-auto">
                                                                        {/* Payment */}
                                                                        <div className="flex flex-col items-center min-w-[100px]">
                                                                            <div className={`w-24 h-24 rounded-lg flex flex-col items-center justify-center ${flowDetails.status === 'Completed' ? 'bg-green-100' :
                                                                                    flowDetails.status === 'Failed' ? 'bg-red-100' : 'bg-yellow-100'
                                                                                }`}>
                                                                                <DollarSign className="w-8 h-8 text-gray-700" />
                                                                                <span className="text-xs mt-1">Payment</span>
                                                                                <span className="text-xs font-bold">{formatCurrency(flowDetails.netAmount)}</span>
                                                                            </div>
                                                                            {getStatusBadge(flowDetails.status)}
                                                                        </div>

                                                                        <ArrowRight className="w-6 h-6 text-gray-400 flex-shrink-0" />

                                                                        {/* Allocations */}
                                                                        <div className="flex flex-col items-center min-w-[120px]">
                                                                            <div className="space-y-2">
                                                                                {flowDetails.allocations.map((allocation, idx) => (
                                                                                    <div key={idx} className={`w-28 h-12 rounded-lg flex flex-col items-center justify-center ${allocation.status === 'COMPLETED' ? 'bg-green-100' :
                                                                                            allocation.status === 'FAILED' ? 'bg-red-100' : 'bg-yellow-100'
                                                                                        }`}>
                                                                                        <span className="text-xs font-bold">{allocation.assetTicker}</span>
                                                                                        <span className="text-xs">{allocation.percentAmount}% ({formatCurrency(allocation.dollarAmount)})</span>
                                                                                    </div>
                                                                                ))}
                                                                            </div>
                                                                            <span className="text-xs mt-2">Allocations</span>
                                                                        </div>

                                                                        <ArrowRight className="w-6 h-6 text-gray-400 flex-shrink-0" />

                                                                        {/* Exchange Orders */}
                                                                        <div className="flex flex-col items-center min-w-[140px]">
                                                                            <div className="space-y-2 max-h-32 overflow-y-auto">
                                                                                {flowDetails.exchangeOrders.map((order, idx) => (
                                                                                    <div key={idx} className={`w-36 h-12 rounded-lg flex flex-col items-center justify-center ${order.status === 'FILLED' ? 'bg-green-100' :
                                                                                            order.status === 'FAILED' ? 'bg-red-100' : 'bg-yellow-100'
                                                                                        }`}>
                                                                                        <span className="text-xs font-bold">{order.ticker}</span>
                                                                                        <span className="text-xs">{order.exchange} - {order.status}</span>
                                                                                    </div>
                                                                                ))}
                                                                            </div>
                                                                            <span className="text-xs mt-2">Exchange Orders</span>
                                                                        </div>

                                                                        <ArrowRight className="w-6 h-6 text-gray-400 flex-shrink-0" />

                                                                        {/* Reconciliation */}
                                                                        <div className="flex flex-col items-center min-w-[100px]">
                                                                            <div className={`w-24 h-24 rounded-lg flex flex-col items-center justify-center ${flowDetails.reconciliationStatus === 'Complete' ? 'bg-green-100' :
                                                                                    flowDetails.reconciliationStatus === 'Failed' ? 'bg-red-100' :
                                                                                        flowDetails.reconciliationStatus === 'Partial' ? 'bg-orange-100' : 'bg-yellow-100'
                                                                                }`}>
                                                                                <BarChart3 className="w-8 h-8 text-gray-700" />
                                                                                <span className="text-xs mt-1">Reconciliation</span>
                                                                            </div>
                                                                            {getStatusBadge(flowDetails.reconciliationStatus)}
                                                                        </div>
                                                                    </div>
                                                                </div>

                                                                {/* Reconciliation Details */}
                                                                {flowDetails.reconciliationDetails && (
                                                                    <div className="bg-white rounded-lg p-4">
                                                                        <h3 className="font-semibold text-gray-900 mb-3">Reconciliation Details</h3>
                                                                        <div className="grid grid-cols-4 gap-4 mb-3">
                                                                            <div>
                                                                                <p className="text-xs text-gray-500">Expected</p>
                                                                                <p className="font-medium">{formatCurrency(flowDetails.reconciliationDetails.totalExpected)}</p>
                                                                            </div>
                                                                            <div>
                                                                                <p className="text-xs text-gray-500">Ordered</p>
                                                                                <p className="font-medium">{formatCurrency(flowDetails.reconciliationDetails.totalOrdered)}</p>
                                                                            </div>
                                                                            <div>
                                                                                <p className="text-xs text-gray-500">Filled</p>
                                                                                <p className="font-medium">{formatCurrency(flowDetails.reconciliationDetails.totalFilled)}</p>
                                                                            </div>
                                                                            <div>
                                                                                <p className="text-xs text-gray-500">Variance</p>
                                                                                <p className={`font-medium ${Math.abs(flowDetails.reconciliationDetails.variance) > 1 ? 'text-red-600' : 'text-green-600'}`}>
                                                                                    {formatCurrency(flowDetails.reconciliationDetails.variance)}
                                                                                </p>
                                                                            </div>
                                                                        </div>

                                                                        {/* Asset Breakdown Table */}
                                                                        <div className="overflow-x-auto">
                                                                            <table className="min-w-full divide-y divide-gray-200">
                                                                                <thead>
                                                                                    <tr>
                                                                                        <th className="px-4 py-2 text-left text-xs font-medium text-gray-500">Asset</th>
                                                                                        <th className="px-4 py-2 text-left text-xs font-medium text-gray-500">Expected</th>
                                                                                        <th className="px-4 py-2 text-left text-xs font-medium text-gray-500">Ordered</th>
                                                                                        <th className="px-4 py-2 text-left text-xs font-medium text-gray-500">Filled</th>
                                                                                        <th className="px-4 py-2 text-left text-xs font-medium text-gray-500">Orders</th>
                                                                                        <th className="px-4 py-2 text-left text-xs font-medium text-gray-500">Status</th>
                                                                                    </tr>
                                                                                </thead>
                                                                                <tbody className="divide-y divide-gray-200">
                                                                                    {flowDetails.reconciliationDetails.assetBreakdown.map((asset, idx) => (
                                                                                        <tr key={idx}>
                                                                                            <td className="px-4 py-2 text-sm font-medium">{asset.assetTicker}</td>
                                                                                            <td className="px-4 py-2 text-sm">{formatCurrency(asset.expectedAmount)}</td>
                                                                                            <td className="px-4 py-2 text-sm">{formatCurrency(asset.orderedAmount)}</td>
                                                                                            <td className="px-4 py-2 text-sm">{formatCurrency(asset.filledAmount)}</td>
                                                                                            <td className="px-4 py-2 text-sm">{asset.orderCount}</td>
                                                                                            <td className="px-4 py-2">{getStatusBadge(asset.status)}</td>
                                                                                        </tr>
                                                                                    ))}
                                                                                </tbody>
                                                                            </table>
                                                                        </div>
                                                                    </div>
                                                                )}

                                                                {/* Exchange Order Details */}
                                                                <div className="bg-white rounded-lg p-4">
                                                                    <h3 className="font-semibold text-gray-900 mb-3">Exchange Order Details</h3>
                                                                    <div className="overflow-x-auto">
                                                                        <table className="min-w-full divide-y divide-gray-200">
                                                                            <thead>
                                                                                <tr>
                                                                                    <th className="px-4 py-2 text-left text-xs font-medium text-gray-500">Order ID</th>
                                                                                    <th className="px-4 py-2 text-left text-xs font-medium text-gray-500">Asset</th>
                                                                                    <th className="px-4 py-2 text-left text-xs font-medium text-gray-500">Exchange</th>
                                                                                    <th className="px-4 py-2 text-left text-xs font-medium text-gray-500">Quantity</th>
                                                                                    <th className="px-4 py-2 text-left text-xs font-medium text-gray-500">Price</th>
                                                                                    <th className="px-4 py-2 text-left text-xs font-medium text-gray-500">Filled</th>
                                                                                    <th className="px-4 py-2 text-left text-xs font-medium text-gray-500">Status</th>
                                                                                    <th className="px-4 py-2 text-left text-xs font-medium text-gray-500">Created</th>
                                                                                </tr>
                                                                            </thead>
                                                                            <tbody className="divide-y divide-gray-200">
                                                                                {flowDetails.exchangeOrders.map((order) => (
                                                                                    <tr key={order.orderId}>
                                                                                        <td className="px-4 py-2 text-sm">{order.placedOrderId || 'Pending'}</td>
                                                                                        <td className="px-4 py-2 text-sm">{order.ticker}</td>
                                                                                        <td className="px-4 py-2 text-sm">{order.exchange}</td>
                                                                                        <td className="px-4 py-2 text-sm">{order.quantity?.toFixed(6) || '-'}</td>
                                                                                        <td className="px-4 py-2 text-sm">{order.price ? formatCurrency(order.price) : '-'}</td>
                                                                                        <td className="px-4 py-2 text-sm">
                                                                                            {order.quoteQuantityFilled ?
                                                                                                `${formatCurrency(order.quoteQuantityFilled)}/${formatCurrency(order.quoteQuantity)}` :
                                                                                                '-'
                                                                                            }
                                                                                        </td>
                                                                                        <td className="px-4 py-2">{getStatusBadge(order.status)}</td>
                                                                                        <td className="px-4 py-2 text-sm text-gray-500">{formatDate(order.createdAt)}</td>
                                                                                    </tr>
                                                                                ))}
                                                                            </tbody>
                                                                        </table>
                                                                    </div>
                                                                </div>

                                                                {/* Actions for Failed/Pending Orders */}
                                                                {(flowDetails.status === 'Failed' || flowDetails.reconciliationStatus === 'Failed') && (
                                                                    <div className="bg-red-50 rounded-lg p-4">
                                                                        <div className="flex items-center justify-between">
                                                                            <div className="flex items-center gap-2">
                                                                                <AlertTriangle className="w-5 h-5 text-red-600" />
                                                                                <div>
                                                                                    <p className="text-sm font-medium text-red-900">Action Required</p>
                                                                                    <p className="text-xs text-red-700">
                                                                                        {flowDetails.errorMessage || 'Payment or order execution failed'}
                                                                                    </p>
                                                                                </div>
                                                                            </div>
                                                                            <div className="flex gap-2">
                                                                                <button
                                                                                    onClick={() => handleReconcilePayment(flowDetails.paymentId)}
                                                                                    disabled={reconcilingPayments.has(flowDetails.paymentId)}
                                                                                    className="px-3 py-1 text-sm bg-white border border-red-300 text-red-700 rounded hover:bg-red-50 disabled:opacity-50"
                                                                                >
                                                                                    {reconcilingPayments.has(flowDetails.paymentId) ? (
                                                                                        <Loader2 className="w-3 h-3 animate-spin" />
                                                                                    ) : (
                                                                                        'Reconcile'
                                                                                    )}
                                                                                </button>
                                                                                <button
                                                                                    onClick={() => handleRetryPayment(flowDetails.paymentId)}
                                                                                    disabled={retryingPayments.has(flowDetails.paymentId) || flowDetails.retryCount >= 5}
                                                                                    className="px-3 py-1 text-sm bg-red-600 text-white rounded hover:bg-red-700 disabled:opacity-50"
                                                                                >
                                                                                    {retryingPayments.has(flowDetails.paymentId) ? (
                                                                                        <Loader2 className="w-3 h-3 animate-spin" />
                                                                                    ) : (
                                                                                        `Retry (${flowDetails.retryCount}/5)`
                                                                                    )}
                                                                                </button>
                                                                            </div>
                                                                        </div>
                                                                    </div>
                                                                )}
                                                            </div>
                                                        </td>
                                                    </tr>
                                                )}
                                            </React.Fragment>
                                        );
                                    })}
                                </tbody>
                            </table>
                        </div>
                    )}
                </div>

                {/* Pagination */}
                {totalPages > 1 && (
                    <div className="mt-6 flex items-center justify-between">
                        <div className="text-sm text-gray-700">
                            Showing {((page - 1) * pageSize) + 1} to {Math.min(page * pageSize, totalCount)} of {totalCount} results
                        </div>
                        <div className="flex items-center gap-2">
                            <button
                                onClick={() => setPage(Math.max(1, page - 1))}
                                disabled={page === 1}
                                className={`px-4 py-2 border border-gray-300 rounded-lg text-sm font-medium ${page === 1 ? 'bg-gray-100 text-gray-400 cursor-not-allowed' : 'bg-white text-gray-700 hover:bg-gray-50'
                                    }`}
                            >
                                Previous
                            </button>

                            <div className="flex items-center gap-1">
                                {Array.from({ length: Math.min(5, totalPages) }, (_, i) => {
                                    let pageNum: number;
                                    if (totalPages <= 5) {
                                        pageNum = i + 1;
                                    } else if (page <= 3) {
                                        pageNum = i + 1;
                                    } else if (page >= totalPages - 2) {
                                        pageNum = totalPages - 4 + i;
                                    } else {
                                        pageNum = page - 2 + i;
                                    }

                                    return (
                                        <button
                                            key={pageNum}
                                            onClick={() => setPage(pageNum)}
                                            className={`px-3 py-1 rounded ${pageNum === page
                                                    ? 'bg-blue-600 text-white'
                                                    : 'bg-white text-gray-700 hover:bg-gray-50'
                                                }`}
                                        >
                                            {pageNum}
                                        </button>
                                    );
                                })}
                            </div>

                            <button
                                onClick={() => setPage(Math.min(totalPages, page + 1))}
                                disabled={page === totalPages}
                                className={`px-4 py-2 border border-gray-300 rounded-lg text-sm font-medium ${page === totalPages ? 'bg-gray-100 text-gray-400 cursor-not-allowed' : 'bg-white text-gray-700 hover:bg-gray-50'
                                    }`}
                            >
                                Next
                            </button>
                        </div>
                    </div>
                )}
            </div>
        </div>
    );
};

export default AdminPaymentTracker;