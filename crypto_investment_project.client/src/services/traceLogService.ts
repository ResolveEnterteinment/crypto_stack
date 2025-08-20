import ITraceLogNode from "../interfaces/TraceLog/ITraceLogNode";
import api, { PaginatedResult } from "./api";

// Interface for purge response
export interface PurgeLogsResponse {
    isSuccess: boolean;
    data: {
        isSuccess: boolean;
        matchedCount: number;
        modifiedCount: number;
        affectedIds: string[];
    };
    dataMessage: string;
}

// API endpoints
const ENDPOINTS = {
    TREE: () => `/trace/tree`,
    ALL: () => `/trace/tree/all`,
    RESOLVE: (id: string) => `/trace/resolve/${id}`,
    PURGE: () => `/trace/purge`,
    HEALTH_CHECK: '/trace/health'
} as const;

// Export trace functions
export const getTraceTree = async (): Promise<ITraceLogNode[]> => {
    try {
        var response = await api.get<ITraceLogNode[]>(ENDPOINTS.ALL());

        if (response == null || response.data == null || !response.success) {
            throw new Error(response.message || `Failed to fetch trace logs`);
        }

        return response.data;
    } catch (error: any) {
        console.error(`Error fetching trace logs:`, error);
        throw error;
    }
}

export const getTraceTreePaginated = async (
    page: number = 1,
    pageSize: number = 20,
    filterLevel: number = 0,
    rootId?: string)
    : Promise<PaginatedResult<ITraceLogNode>> => {
    const params = new URLSearchParams({
        page: page.toString(),
        pageSize: pageSize.toString(),
        filterLevel: filterLevel.toString(),
        ...(rootId && { rootId })
    });
    try {
        var response = await api.get<PaginatedResult<ITraceLogNode>>(ENDPOINTS.TREE() + `?${params}`);

        if (response == null || response.data == null || !response.success) {
            throw new Error(response.message || `Failed to fetch trace logs`);
        }

        return response.data;
    } catch (error: any) {
        console.error(`Error fetching paginated trace logs:`, error);
        throw error;
    }
};

export const resolveTraceLog = async (id: string, comment: string): Promise<void> => {
    try {
        var response = await api.post(ENDPOINTS.RESOLVE(id), comment);

        if (response == null || response.data == null || !response.success) {
            throw new Error(response.message || `Failed to resolve trace log`);
        }
    } catch (error: any) {
        console.error(`Error resolving trace log:`, error);
        throw error;
    }
}

export const purgeLogs = async (maxLevel: number): Promise<PurgeLogsResponse> => {
    try {
        const params = new URLSearchParams({
            maxLevel: maxLevel.toString()
        });

        var response = await api.delete<PurgeLogsResponse>(ENDPOINTS.PURGE() + `?${params}`);

        if (response == null || response.data == null || !response.success) {
            throw new Error(response.message || `Failed to purge logs`);
        }

        return response.data;
    } catch (error: any) {
        console.error(`Error purging logs:`, error);
        throw error;
    }
}