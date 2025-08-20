// src/services/api/metricsTracker.ts
import { RequestMetrics } from './types';

class MetricsTracker {
    private static instance: MetricsTracker;
    private metrics: RequestMetrics = {
        duplicatesPrevented: 0,
        requestsCancelled: 0,
        requestsQueued: 0,
        requestsThrottled: 0,
        requestsDebounced: 0,
        requestsRetried: 0,
        totalRequests: 0
    };

    static getInstance(): MetricsTracker {
        if (!MetricsTracker.instance) {
            MetricsTracker.instance = new MetricsTracker();
        }
        return MetricsTracker.instance;
    }

    recordRequest(): void {
        this.metrics.totalRequests++;
    }

    recordDuplicate(): void {
        this.metrics.duplicatesPrevented++;
    }

    recordCancellation(): void {
        this.metrics.requestsCancelled++;
    }

    recordQueued(): void {
        this.metrics.requestsQueued++;
    }

    recordThrottled(): void {
        this.metrics.requestsThrottled++;
    }

    recordDebounced(): void {
        this.metrics.requestsDebounced++;
    }

    recordRetry(): void {
        this.metrics.requestsRetried++;
    }

    getMetrics(): RequestMetrics {
        return { ...this.metrics };
    }

    reset(): void {
        this.metrics = {
            duplicatesPrevented: 0,
            requestsCancelled: 0,
            requestsQueued: 0,
            requestsThrottled: 0,
            requestsDebounced: 0,
            requestsRetried: 0,
            totalRequests: 0
        };
    }

    // Log metrics periodically in development
    startLogging(intervalMs: number = 30000): void {
        if (import.meta.env.DEV) {
            setInterval(() => {
                const metrics = this.getMetrics();
                if (metrics.totalRequests > 0) {
                    console.log('[API Metrics]', {
                        ...metrics,
                        duplicateRate: `${((metrics.duplicatesPrevented / metrics.totalRequests) * 100).toFixed(2)}%`,
                        queueRate: `${((metrics.requestsQueued / metrics.totalRequests) * 100).toFixed(2)}%`
                    });
                }
            }, intervalMs);
        }
    }
}

export const metricsTracker = MetricsTracker.getInstance();