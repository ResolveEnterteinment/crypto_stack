// src/services/paymentFlowService.ts
import api, { PaginatedResult } from './api';

// DTOs matching the C# PaymentFlowService
export interface PaymentFlowDto {
    paymentId: string;
    paymentProviderId: string;
    userId: string;
    subscriptionId: string;
    totalAmount: number;
    netAmount: number;
    processingFee: number;
    currency: string;
    status: string;
    createdAt: string;
    updatedAt: string;
    retryCount: number;
    errorMessage?: string;
    allocations: AllocationFlowDto[];
    exchangeOrders: ExchangeOrderFlowDto[];
    transactions: TransactionFlowDto[];
    reconciliationStatus: ReconciliationStatus;
    reconciliationDetails: ReconciliationDetailsDto;
}

export interface AllocationFlowDto {
    assetId: string;
    assetName: string;
    assetTicker: string;
    percentAmount: number;
    dollarAmount: number;
    status: string;
}

export interface ExchangeOrderFlowDto {
    orderId: string;
    assetId: string;
    ticker: string;
    exchange: string;
    side: string;
    placedOrderId?: string;
    quoteTicker: string;
    quoteQuantity: number;
    quoteQuantityFilled?: number;
    price?: number;
    quantity?: number;
    status: string;
    retryCount: number;
    createdAt: string;
    updatedAt: string;
}

export interface TransactionFlowDto {
    transactionId: string;
    assetId: string;
    action: string;
    amount: number;
    sourceName: string;
    sourceId: string;
    createdAt: string;
}

export interface ReconciliationDetailsDto {
    totalExpected: number;
    totalOrdered: number;
    totalFilled: number;
    variance: number;
    isReconciled: boolean;
    assetBreakdown: AssetReconciliationDto[];
}

export interface AssetReconciliationDto {
    assetTicker: string;
    expectedAmount: number;
    orderedAmount: number;
    filledAmount: number;
    orderCount: number;
    status: string;
}

export interface PaymentFlowSummaryDto {
    paymentId: string;
    paymentProviderId: string;
    amount: number;
    currency: string;
    status: string;
    createdAt: string;
    allocationCount: number;
    allocationsCompleted: number;
    orderCount: number;
    ordersCompleted: number;
    ordersFailed: number;
    reconciliationStatus: ReconciliationStatus;
}

export interface PaymentFlowQuery {
    status?: string;
    startDate?: string;
    endDate?: string;
    userId?: string;
    subscriptionId?: string;
    searchTerm?: string;
    page: number;
    pageSize: number;
}

export interface ReconciliationReportDto {
    startDate: string;
    endDate: string;
    generatedAt: string;
    totalPayments: number;
    totalAmount: number;
    fullyReconciled: number;
    reconciledAmount: number;
    partiallyReconciled: number;
    partialAmount: number;
    failed: number;
    failedAmount: number;
    pending: number;
    pendingAmount: number;
    reconciliationRate: number;
    failedPayments: FailedPaymentSummary[];
}

export interface FailedPaymentSummary {
    paymentId: string;
    amount: number;
    errorMessage?: string;
    failedAt: string;
}

export interface PaymentFlowMetricsDto {
    timestamp: string;
    todayPaymentCount: number;
    todayVolume: number;
    todaySuccessRate: number;
    pendingPayments: number;
    pendingAmount: number;
    failedLast24Hours: number;
    failedAmount: number;
    averageProcessingTimeSeconds: number;
    medianProcessingTimeSeconds: number;
}

export interface FailedPaymentDto {
    paymentId: string;
    paymentProviderId: string;
    userId: string;
    amount: number;
    currency: string;
    status: string;
    errorMessage?: string;
    failedAt: string;
    retryCount: number;
    failureReason: string;
    failureStage: string;
    canRetry: boolean;
    recommendedAction: string;
}

export interface ReconcileResponse {
    success: boolean;
    message?: string;
}

export interface RetryResponse {
    success: boolean;
    paymentFlow: PaymentFlowDto;
}

export type ReconciliationStatus = 'Pending' | 'Partial' | 'Complete' | 'Failed';
export type PaymentStatus = 'Pending' | 'Processing' | 'Completed' | 'Failed' | 'Refunded';

export const PAYMENT_STATUS = {
    // Standard payment statuses
    PENDING: "PENDING",
    PROCESSING: "PROCESSING",
    COMPLETED: "COMPLETED",
    FAILED: "FAILED",
    REFUNDED: "REFUNDED",
    // Enhanced flow statuses
    QUEUED: "QUEUED",
    CANCELLED: "CANCELLED",
    PAID: "PAID",
    // Updated statuses after exchange order
    FILLED: "FILLED",
    PARTIALLY_FILLED: "PARTIALLY_FILLED",
} as const;

// API endpoints
const ENDPOINTS = {
    GET_PAYMENT_FLOW: (paymentId: string) => `/admin/payment-flows/${paymentId}`,
    GET_PAYMENT_FLOW_BY_PROVIDER: (providerId: string) => `/admin/payment-flows/provider/${providerId}`,
    GET_PAYMENT_FLOWS: () => `/admin/payment-flows`,
    RETRY_PAYMENT: (paymentId: string) => `/admin/payment-flows/${paymentId}/retry`,
    RECONCILE_PAYMENT: (paymentId: string) => `/admin/payment-flows/${paymentId}/reconcile`,
    GET_METRICS: () => `/admin/payment-flows/metrics`,
    GET_FAILED_PAYMENTS: () => `/admin/payment-flows/failed`,
    GET_RECONCILIATION_REPORT: () => `/admin/payment-flows/reconciliation-report`,
    EXPORT_REPORT: () => `/admin/payment-flows/export`,
    BULK_RETRY: () => `/admin/payment-flows/bulk-retry`,
    BULK_RECONCILE: () => `/admin/payment-flows/bulk-reconcile`,
    HEALTH_CHECK: () => `/admin/payment-flows/health`
} as const;

class PaymentFlowService {
    /**
     * Get complete payment flow information including allocations and exchange orders
     */
    async getPaymentFlow(paymentId: string): Promise<PaymentFlowDto> {
        try {
            const response = await api.get<PaymentFlowDto>(
                ENDPOINTS.GET_PAYMENT_FLOW(paymentId)
            );

            if (response == null || !response.success) {
                throw new Error(response?.message || 'Failed to fetch payment flow');
            }

            return response.data;
        } catch (error) {
            console.error(`Failed to fetch payment flow for ${paymentId}:`, error);
            throw new Error(`Unable to fetch payment flow details for payment ${paymentId}`);
        }
    }

    /**
     * Get payment flow by provider ID (e.g., Stripe payment ID)
     */
    async getPaymentFlowByProviderId(providerId: string): Promise<PaymentFlowDto> {
        try {
            const response = await api.get<PaymentFlowDto>(
                ENDPOINTS.GET_PAYMENT_FLOW_BY_PROVIDER(providerId)
            );

            if (response == null || !response.success) {
                throw new Error(response?.message || 'Failed to fetch payment flow by provider ID');
            }

            return response.data;
        } catch (error) {
            console.error(`Failed to fetch payment flow for provider ${providerId}:`, error);
            throw new Error(`Unable to fetch payment flow details for provider ${providerId}`);
        }
    }

    /**
     * Get paginated list of payment flows with filtering
     */
    async getPaymentFlows(query: PaymentFlowQuery): Promise<PaginatedResult<PaymentFlowSummaryDto>> {
        try {
            // Build query parameters
            const queryParams = new URLSearchParams();
            queryParams.append('page', query.page.toString());
            queryParams.append('pageSize', query.pageSize.toString());

            if (query.status && query.status != 'ALL') queryParams.append('status', query.status);
            if (query.startDate) queryParams.append('startDate', query.startDate);
            if (query.endDate) queryParams.append('endDate', query.endDate);
            if (query.userId) queryParams.append('userId', query.userId);
            if (query.subscriptionId) queryParams.append('subscriptionId', query.subscriptionId);
            if (query.searchTerm) queryParams.append('searchTerm', query.searchTerm);

            const response = await api.get<PaginatedResult<PaymentFlowSummaryDto>>(
                `${ENDPOINTS.GET_PAYMENT_FLOWS()}?${queryParams.toString()}`
            );

            if (response == null || !response.success) {
                throw new Error(response?.message || 'Failed to fetch payment flows');
            }

            return response.data || {
                items: [],
                totalCount: 0,
                page: query.page,
                pageSize: query.pageSize
            };
        } catch (error) {
            console.error('Failed to fetch payment flows:', error);
            throw new Error('Unable to fetch payment flows');
        }
    }

    /**
     * Retry processing for a failed payment
     */
    async retryPaymentProcessing(paymentId: string): Promise<PaymentFlowDto> {
        try {
            const response = await api.post<RetryResponse>(
                ENDPOINTS.RETRY_PAYMENT(paymentId)
            );

            if (response == null || !response.success) {
                throw new Error(response?.message || 'Failed to retry payment processing');
            }

            if (!response.data?.success) {
                throw new Error(response.data?.paymentFlow.errorMessage || 'Payment retry was not successful');
            }

            return response.data.paymentFlow;
        } catch (error) {
            console.error(`Failed to retry payment ${paymentId}:`, error);
            throw new Error(`Unable to retry payment processing for ${paymentId}`);
        }
    }

    /**
     * Manually reconcile a payment
     */
    async reconcilePayment(paymentId: string): Promise<ReconcileResponse> {
        try {
            const response = await api.post<ReconcileResponse>(
                ENDPOINTS.RECONCILE_PAYMENT(paymentId)
            );

            if (response == null || !response.success) {
                throw new Error(response?.message || 'Failed to reconcile payment');
            }

            return response.data || { success: false, message: 'Unknown error' };
        } catch (error) {
            console.error(`Failed to reconcile payment ${paymentId}:`, error);
            throw new Error(`Unable to reconcile payment ${paymentId}`);
        }
    }

    /**
     * Get real-time payment flow metrics
     */
    async getMetrics(): Promise<PaymentFlowMetricsDto> {
        try {
            const response = await api.get<PaymentFlowMetricsDto>(
                ENDPOINTS.GET_METRICS()
            );

            if (response == null || !response.success) {
                throw new Error(response?.message || 'Failed to fetch metrics');
            }

            return response.data;
        } catch (error) {
            console.error('Failed to fetch payment flow metrics:', error);
            throw new Error('Unable to fetch payment flow metrics');
        }
    }

    /**
     * Get list of failed payments requiring attention
     */
    async getFailedPayments(limit: number = 50): Promise<FailedPaymentDto[]> {
        try {
            const queryParams = new URLSearchParams();
            queryParams.append('limit', limit.toString());

            const response = await api.get<FailedPaymentDto[]>(
                `${ENDPOINTS.GET_FAILED_PAYMENTS()}?${queryParams.toString()}`
            );

            if (response == null || !response.success) {
                throw new Error(response?.message || 'Failed to fetch failed payments');
            }

            return response.data || [];
        } catch (error) {
            console.error('Failed to fetch failed payments:', error);
            throw new Error('Unable to fetch failed payments');
        }
    }

    /**
     * Get reconciliation report for a date range
     */
    async getReconciliationReport(startDate: string, endDate: string): Promise<ReconciliationReportDto> {
        try {
            const queryParams = new URLSearchParams();
            queryParams.append('startDate', startDate);
            queryParams.append('endDate', endDate);

            const response = await api.get<ReconciliationReportDto>(
                `${ENDPOINTS.GET_RECONCILIATION_REPORT()}?${queryParams.toString()}`
            );

            if (response == null || !response.success) {
                throw new Error(response?.message || 'Failed to fetch reconciliation report');
            }

            return response.data;
        } catch (error) {
            console.error('Failed to fetch reconciliation report:', error);
            throw new Error('Unable to fetch reconciliation report');
        }
    }

    /**
     * Export payment flows report
     */
    async exportReport(query: Partial<PaymentFlowQuery>, format: 'csv' | 'excel' = 'csv'): Promise<Blob> {
        try {
            // Build query parameters
            const queryParams = new URLSearchParams();
            queryParams.append('format', format);

            if (query.status) queryParams.append('status', query.status);
            if (query.startDate) queryParams.append('startDate', query.startDate);
            if (query.endDate) queryParams.append('endDate', query.endDate);
            if (query.userId) queryParams.append('userId', query.userId);
            if (query.subscriptionId) queryParams.append('subscriptionId', query.subscriptionId);
            if (query.searchTerm) queryParams.append('searchTerm', query.searchTerm);

            const response = await api.get<Blob>(
                `${ENDPOINTS.EXPORT_REPORT()}?${queryParams.toString()}`,
                {
                    responseType: 'blob'
                }
            );

            if (response == null || !response.success) {
                throw new Error(response?.message || 'Failed to export report');
            }

            return response.data;
        } catch (error) {
            console.error('Failed to export payment flow report:', error);
            throw new Error('Unable to export payment flow report');
        }
    }

    /**
     * Bulk retry failed payments
     */
    async bulkRetryPayments(paymentIds: string[]): Promise<{ successful: string[]; failed: string[] }> {
        try {
            if (paymentIds.length === 0) {
                return { successful: [], failed: [] };
            }

            const response = await api.post<{ successful: string[]; failed: string[] }>(
                ENDPOINTS.BULK_RETRY(),
                { paymentIds }
            );

            if (response == null || !response.success) {
                throw new Error(response?.message || 'Failed to bulk retry payments');
            }

            return response.data || { successful: [], failed: [] };
        } catch (error) {
            console.error('Failed to bulk retry payments:', error);
            throw new Error('Unable to bulk retry payments');
        }
    }

    /**
     * Bulk reconcile payments
     */
    async bulkReconcilePayments(paymentIds: string[]): Promise<{ successful: string[]; failed: string[] }> {
        try {
            if (paymentIds.length === 0) {
                return { successful: [], failed: [] };
            }

            const response = await api.post<{ successful: string[]; failed: string[] }>(
                ENDPOINTS.BULK_RECONCILE(),
                { paymentIds }
            );

            if (response == null || !response.success) {
                throw new Error(response?.message || 'Failed to bulk reconcile payments');
            }

            return response.data || { successful: [], failed: [] };
        } catch (error) {
            console.error('Failed to bulk reconcile payments:', error);
            throw new Error('Unable to bulk reconcile payments');
        }
    }

    /**
     * Check payment flow service health
     */
    async checkHealth(): Promise<{ status: string; message: string }> {
        try {
            const response = await api.get<{ status: string; message: string }>(
                ENDPOINTS.HEALTH_CHECK()
            );

            if (response == null || !response.success) {
                return { status: 'unhealthy', message: response?.message || 'Service unavailable' };
            }

            return response.data || { status: 'healthy', message: 'Service is running' };
        } catch (error) {
            console.error('Failed to check payment flow service health:', error);
            return { status: 'unhealthy', message: 'Unable to reach service' };
        }
    }

    /**
     * Download payment flow details as PDF
     */
    async downloadPaymentFlowPDF(paymentId: string): Promise<Blob> {
        try {
            const response = await api.get<Blob>(
                `${ENDPOINTS.GET_PAYMENT_FLOW(paymentId)}/pdf`,
                {
                    responseType: 'blob'
                }
            );

            if (response == null || !response.success) {
                throw new Error(response?.message || 'Failed to download PDF');
            }

            return response.data;
        } catch (error) {
            console.error(`Failed to download PDF for payment ${paymentId}:`, error);
            throw new Error(`Unable to download payment flow PDF for ${paymentId}`);
        }
    }

    /**
     * Get payment flow statistics for a user
     */
    async getUserPaymentStats(userId: string): Promise<{
        totalPayments: number;
        successfulPayments: number;
        failedPayments: number;
        totalVolume: number;
        successRate: number;
    }> {
        try {
            const response = await api.get<{
                totalPayments: number;
                successfulPayments: number;
                failedPayments: number;
                totalVolume: number;
                successRate: number;
            }>(`/admin/payment-flows/user/${userId}/stats`);

            if (response == null || !response.success) {
                throw new Error(response?.message || 'Failed to fetch user payment statistics');
            }

            return response.data || {
                totalPayments: 0,
                successfulPayments: 0,
                failedPayments: 0,
                totalVolume: 0,
                successRate: 0
            };
        } catch (error) {
            console.error(`Failed to fetch payment stats for user ${userId}:`, error);
            throw new Error(`Unable to fetch payment statistics for user ${userId}`);
        }
    }

    /**
     * Trigger manual payment processing for pending payments
     */
    async processPendingPayments(): Promise<{ processed: number; failed: number }> {
        try {
            const response = await api.post<{ processed: number; failed: number }>(
                '/admin/payment-flows/process-pending'
            );

            if (response == null || !response.success) {
                throw new Error(response?.message || 'Failed to process pending payments');
            }

            return response.data || { processed: 0, failed: 0 };
        } catch (error) {
            console.error('Failed to process pending payments:', error);
            throw new Error('Unable to process pending payments');
        }
    }

    /**
     * Get payment flow audit trail
     */
    async getPaymentAuditTrail(paymentId: string): Promise<Array<{
        timestamp: string;
        action: string;
        details: string;
        performedBy: string;
    }>> {
        try {
            const response = await api.get<Array<{
                timestamp: string;
                action: string;
                details: string;
                performedBy: string;
            }>>(`${ENDPOINTS.GET_PAYMENT_FLOW(paymentId)}/audit`);

            if (response == null || !response.success) {
                throw new Error(response?.message || 'Failed to fetch audit trail');
            }

            return response.data || [];
        } catch (error) {
            console.error(`Failed to fetch audit trail for payment ${paymentId}:`, error);
            throw new Error(`Unable to fetch audit trail for payment ${paymentId}`);
        }
    }

    /**
     * Cache payment flow data for offline access
     */
    private paymentFlowCache = new Map<string, { data: PaymentFlowDto; timestamp: number }>();
    private cacheTimeout = 5 * 60 * 1000; // 5 minutes

    async getCachedPaymentFlow(paymentId: string): Promise<PaymentFlowDto> {
        const cached = this.paymentFlowCache.get(paymentId);
        const now = Date.now();

        if (cached && (now - cached.timestamp) < this.cacheTimeout) {
            console.log(`Returning cached payment flow for ${paymentId}`);
            return cached.data;
        }

        const data = await this.getPaymentFlow(paymentId);
        this.paymentFlowCache.set(paymentId, { data, timestamp: now });

        // Clean old cache entries
        this.cleanCache();

        return data;
    }

    private cleanCache(): void {
        const now = Date.now();
        const entriesToDelete: string[] = [];

        this.paymentFlowCache.forEach((value, key) => {
            if (now - value.timestamp > this.cacheTimeout) {
                entriesToDelete.push(key);
            }
        });

        entriesToDelete.forEach(key => this.paymentFlowCache.delete(key));
    }

    /**
     * Clear all cached data
     */
    clearCache(): void {
        this.paymentFlowCache.clear();
    }
}

export default new PaymentFlowService();