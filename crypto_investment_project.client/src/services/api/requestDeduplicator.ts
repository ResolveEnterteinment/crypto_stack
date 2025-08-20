// src/services/api/requestDeduplicator.ts
import { PendingRequest } from './types';
import { API_CONFIG } from './config';
import { metricsTracker } from './metricsTracker';

class RequestDeduplicator {
    private static instance: RequestDeduplicator;
    private pendingRequests: Map<string, PendingRequest> = new Map();

    static getInstance(): RequestDeduplicator {
        if (!RequestDeduplicator.instance) {
            RequestDeduplicator.instance = new RequestDeduplicator();
        }
        return RequestDeduplicator.instance;
    }

    generateKey(method: string, url: string, data?: any): string {
        const dataString = data ? JSON.stringify(data) : '';
        return `${method}-${url}-${dataString}`;
    }

    async dedupe<T>(
        key: string,
        requestFn: () => Promise<T>,
        ttl: number = API_CONFIG.SIDE_EFFECTS.DEDUP_TTL
    ): Promise<T> {
        const now = Date.now();
        const pending = this.pendingRequests.get(key);

        // Return existing promise if not expired
        if (pending && (now - pending.timestamp) < ttl) {
            console.log(`[Dedup] Returning existing request for: ${key}`);
            metricsTracker.recordDuplicate();
            return pending.promise;
        }

        // Clean expired entries
        this.cleanExpired();

        // Create new request
        const promise = requestFn();
        this.pendingRequests.set(key, { promise, timestamp: now });

        try {
            const result = await promise;
            // Keep successful responses in cache for the TTL
            return result;
        } catch (error) {
            // Remove failed requests immediately
            this.pendingRequests.delete(key);
            throw error;
        }
    }

    cancel(key: string): void {
        const pending = this.pendingRequests.get(key);
        if (pending?.abortController) {
            pending.abortController.abort();
            this.pendingRequests.delete(key);
            metricsTracker.recordCancellation();
        }
    }

    cancelAll(): void {
        this.pendingRequests.forEach((request, key) => {
            if (request.abortController) {
                request.abortController.abort();
            }
        });
        this.pendingRequests.clear();
    }

    private cleanExpired(): void {
        const now = Date.now();
        const ttl = API_CONFIG.SIDE_EFFECTS.DEDUP_TTL;

        for (const [key, request] of this.pendingRequests.entries()) {
            if (now - request.timestamp > ttl) {
                this.pendingRequests.delete(key);
            }
        }
    }

    clear(): void {
        this.pendingRequests.clear();
    }
}

export const requestDeduplicator = RequestDeduplicator.getInstance();