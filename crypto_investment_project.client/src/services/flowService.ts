// flowEngineApiService.ts
// API client service for FlowEngine Admin Panel

import api from './api';

// API endpoints
const ENDPOINTS = {
    GET_FLOWS: () => `/flow/flows`,
    GET_FLOW: (flowId: string) => `/flow/flows/${flowId}`,
    GET_FLOW_TIMELINE: (flowId: string) => `/flow/flows/${flowId}/timeline`,
    GET_STATISTICS: () => `/flow/statistics`,
    PAUSE_FLOW: (flowId: string) => `/flow/flows/${flowId}/pause`,
    RESUME_FLOW: (flowId: string) => `/flow/flows/${flowId}/resume`,
    CANCEL_FLOW: (flowId: string) => `/flow/flows/${flowId}/cancel`,
    RESOLVE_FLOW: (flowId: string) => `/flow/flows/${flowId}/resolve`,
    RETRY_FLOW: (flowId: string) => `/flow/flows/${flowId}/retry`,
    BATCH_OPERATION: (operation: string) => `/flow/flows/batch/${operation}`,
    RECOVER_CRASHED: () => `/flow/recovery/crashed`,
    RESTORE_RUNTIME: () => `/flow/recovery/restore-runtime`,
    HEALTH_CHECK: '/flow/health'
} as const;

// Types matching the backend DTOs
export interface FlowSummaryDto {
    flowId: string;
    flowType: string;
    status: string;
    userId: string;
    correlationId: string;
    createdAt: string;
    startedAt?: string;
    completedAt?: string;
    currentStepName: string;
    pauseReason?: string;
    errorMessage?: string;
    duration?: number;
    currentStepIndex: number;
    totalSteps: number;
}

export interface PagedResult<T> {
    items: T[];
    totalCount: number;
    pageNumber: number;
    pageSize: number;
    totalPages: number;
}

export interface FlowDetailDto {
    flowId: string;
    flowType: string;
    status: string;
    userId: string;
    correlationId: string;
    createdAt: string;
    startedAt?: string;
    completedAt?: string;
    pausedAt?: string;
    currentStepName: string;
    currentStepIndex: number;
    pauseReason?: string;
    pauseMessage?: string;
    lastError?: string;
    steps: StepDto[];
    events: FlowEvent[];
    data: Record<string, any>;
    totalSteps: number;
}

export interface StepDto {
    name: string;
    status: string;
    stepDependencies: string[];
    dataDependencies: Record<string, string>;
    maxRetries: number;
    retryDelay: string;
    timeout?: string;
    isCritical: boolean;
    isIdempotent: boolean;
    canRunInParallel: boolean;
    result?: StepResultDto;
    branches?: BranchDto[];
}

export interface StepResultDto {
    isSuccess: boolean;
    message: string;
    data: Record<string, any>;
}

export interface BranchDto {
    steps: string[];
    isDefault: boolean;
    condition: string;
}

export interface FlowEvent {
    flowId: string;
    eventType: string;
    description: string;
    timestamp: string;
    data?: Record<string, any>;
}

export interface FlowStatisticsDto {
    period: string;
    total: number;
    running: number;
    completed: number;
    failed: number;
    paused: number;
    cancelled: number;
    averageDuration: number;
    successRate: number;
    flowsByType: Record<string, number>;
    pauseReasons: Record<string, number>;
}

export interface BatchOperationResultDto {
    operation: string;
    totalFlows: number;
    successCount: number;
    failureCount: number;
    results: BatchOperationItemResult[];
}

export interface BatchOperationItemResult {
    flowId: string;
    success: boolean;
    message: string;
}

export interface FlowQuery {
    status?: string;
    userId?: string;
    flowType?: string;
    correlationId?: string;
    createdAfter?: string;
    createdBefore?: string;
    pauseReason?: string;
    page?: number;
    pageSize?: number;
}

export interface PauseRequestDto {
    reason?: string;
    message?: string;
}

export interface ResumeRequestDto {
    resumeData?: Record<string, any>;
}

export interface CancelRequestDto {
    reason?: string;
}

export interface ResolveRequestDto {
    resolution?: string;
}

export interface BatchOperationRequestDto {
    flowIds: string[];
    options?: Record<string, any>;
}

export interface RecoveryResultDto {
    recoveredCount: number;
    failedCount: number;
    recoveredFlows: string[];
    failedFlows: Array<{ flowId: string; error: string }>;
}

// Main FlowEngine API Service
class FlowService {
    /**
     * Get paginated list of flows with optional filtering
     */
    async getFlows(query: FlowQuery = {}): Promise<PagedResult<FlowSummaryDto>> {
        try {
            const params = new URLSearchParams();
            Object.entries(query).forEach(([key, value]) => {
                if (value !== undefined && value !== null) {
                    params.append(key, value.toString());
                }
            });

            const response = await api.get<PagedResult<FlowSummaryDto>>(
                `${ENDPOINTS.GET_FLOWS()}?${params.toString()}`
            );

            if (response == null || response.data == null || !response.success) {
                throw new Error(response.message || 'Failed to fetch flows');
            }

            return response.data;
        } catch (error) {
            console.error('Failed to fetch flows:', error);
            throw new Error('Unable to fetch flows');
        }
    }

    /**
     * Get detailed information about a specific flow
     */
    async getFlowById(flowId: string): Promise<FlowDetailDto> {
        try {
            const response = await api.get<FlowDetailDto>(ENDPOINTS.GET_FLOW(flowId));

            if (response == null || response.data == null || !response.success) {
                throw new Error(response.message || `Failed to fetch flow ${flowId}`);
            }

            return response.data;
        } catch (error) {
            console.error(`Failed to fetch flow ${flowId}:`, error);
            throw new Error(`Unable to fetch flow details for ${flowId}`);
        }
    }

    /**
     * Get flow execution timeline
     */
    async getFlowTimeline(flowId: string): Promise<FlowEvent[]> {
        try {
            const response = await api.get<FlowEvent[]>(ENDPOINTS.GET_FLOW_TIMELINE(flowId));

            if (response == null || response.data == null || !response.success) {
                throw new Error(response.message || `Failed to fetch timeline for flow ${flowId}`);
            }

            return response.data;
        } catch (error) {
            console.error(`Failed to fetch timeline for flow ${flowId}:`, error);
            throw new Error(`Unable to fetch timeline for flow ${flowId}`);
        }
    }

    /**
     * Get flow statistics for a date range
     */
    async getStatistics(startDate?: Date, endDate?: Date): Promise<FlowStatisticsDto> {
        try {
            const params = new URLSearchParams();
            if (startDate) params.append('startDate', startDate.toISOString());
            if (endDate) params.append('endDate', endDate.toISOString());

            const response = await api.get<FlowStatisticsDto>(
                `${ENDPOINTS.GET_STATISTICS()}?${params.toString()}`
            );

            if (response == null || response.data == null || !response.success) {
                throw new Error(response.message || 'Failed to fetch statistics');
            }

            return response.data;
        } catch (error) {
            console.error('Failed to fetch statistics:', error);
            throw new Error('Unable to fetch flow statistics');
        }
    }

    /**
     * Pause a running flow
     */
    async pauseFlow(flowId: string, request?: PauseRequestDto): Promise<any> {
        try {
            const response = await api.post<any>(ENDPOINTS.PAUSE_FLOW(flowId), request || {});

            if (response == null || !response.success) {
                throw new Error(response.message || `Failed to pause flow ${flowId}`);
            }

            return response.data;
        } catch (error) {
            console.error(`Failed to pause flow ${flowId}:`, error);
            throw new Error(`Unable to pause flow ${flowId}`);
        }
    }

    /**
     * Resume a paused flow
     */
    async resumeFlow(flowId: string, request?: ResumeRequestDto): Promise<any> {
        try {
            const response = await api.post<any>(ENDPOINTS.RESUME_FLOW(flowId), request || {});

            if (response == null || !response.success) {
                throw new Error(response.message || `Failed to resume flow ${flowId}`);
            }

            return response.data;
        } catch (error) {
            console.error(`Failed to resume flow ${flowId}:`, error);
            throw new Error(`Unable to resume flow ${flowId}`);
        }
    }

    /**
     * Cancel a running or paused flow
     */
    async cancelFlow(flowId: string, request?: CancelRequestDto): Promise<any> {
        try {
            const response = await api.post<any>(ENDPOINTS.CANCEL_FLOW(flowId), request || {});

            if (response == null || !response.success) {
                throw new Error(response.message || `Failed to cancel flow ${flowId}`);
            }

            return response.data;
        } catch (error) {
            console.error(`Failed to cancel flow ${flowId}:`, error);
            throw new Error(`Unable to cancel flow ${flowId}`);
        }
    }

    /**
     * Resolve a failed flow
     */
    async resolveFlow(flowId: string, request?: ResolveRequestDto): Promise<any> {
        try {
            const response = await api.post<any>(ENDPOINTS.RESOLVE_FLOW(flowId), request || {});

            if (response == null || !response.success) {
                throw new Error(response.message || `Failed to resolve flow ${flowId}`);
            }

            return response.data;
        } catch (error) {
            console.error(`Failed to resolve flow ${flowId}:`, error);
            throw new Error(`Unable to resolve flow ${flowId}`);
        }
    }

    /**
     * Retry a failed flow from the last failed step
     */
    async retryFlow(flowId: string): Promise<any> {
        try {
            const response = await api.post<any>(ENDPOINTS.RETRY_FLOW(flowId), {});

            if (response == null || !response.success) {
                throw new Error(response.message || `Failed to retry flow ${flowId}`);
            }

            return response.data;
        } catch (error) {
            console.error(`Failed to retry flow ${flowId}:`, error);
            throw new Error(`Unable to retry flow ${flowId}`);
        }
    }

    /**
     * Perform batch operations on multiple flows
     */
    async batchOperation(
        operation: 'pause' | 'resume' | 'cancel' | 'resolve',
        request: BatchOperationRequestDto
    ): Promise<BatchOperationResultDto> {
        try {
            const response = await api.post<BatchOperationResultDto>(
                ENDPOINTS.BATCH_OPERATION(operation),
                request
            );

            if (response == null || response.data == null || !response.success) {
                throw new Error(response.message || `Failed to perform batch ${operation}`);
            }

            return response.data;
        } catch (error) {
            console.error(`Failed to perform batch ${operation}:`, error);
            throw new Error(`Unable to perform batch ${operation}`);
        }
    }

    /**
     * Recover crashed flows
     */
    async recoverCrashedFlows(): Promise<RecoveryResultDto> {
        try {
            const response = await api.post<RecoveryResultDto>(ENDPOINTS.RECOVER_CRASHED(), {});

            if (response == null || response.data == null || !response.success) {
                throw new Error(response.message || 'Failed to recover crashed flows');
            }

            return response.data;
        } catch (error) {
            console.error('Failed to recover crashed flows:', error);
            throw new Error('Unable to recover crashed flows');
        }
    }

    /**
     * Restore flow runtime (useful after server restart)
     */
    async restoreFlowRuntime(): Promise<any> {
        try {
            const response = await api.post<any>(ENDPOINTS.RESTORE_RUNTIME(), {});

            if (response == null || !response.success) {
                throw new Error(response.message || 'Failed to restore flow runtime');
            }

            return response.data;
        } catch (error) {
            console.error('Failed to restore flow runtime:', error);
            throw new Error('Unable to restore flow runtime');
        }
    }
}

// Export singleton instance
export default new FlowService();
export const flowService = new FlowService();