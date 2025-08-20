// src/services/api/requestQueue.ts
import { API_CONFIG } from './config';
import { metricsTracker } from './metricsTracker';

interface QueuedRequest {
    id: string;
    priority: number;
    timestamp: number;
    execute: () => Promise<any>;
    resolve: (value: any) => void;
    reject: (error: any) => void;
}

class RequestQueue {
    private static instance: RequestQueue;
    private queue: QueuedRequest[] = [];
    private activeRequests: number = 0;
    private maxConcurrent: number = API_CONFIG.SIDE_EFFECTS.MAX_CONCURRENT_REQUESTS;
    private processing: boolean = false;

    static getInstance(): RequestQueue {
        if (!RequestQueue.instance) {
            RequestQueue.instance = new RequestQueue();
        }
        return RequestQueue.instance;
    }

    async enqueue<T>(
        execute: () => Promise<T>,
        priority: 'low' | 'normal' | 'high' = 'normal'
    ): Promise<T> {
        return new Promise((resolve, reject) => {
            const priorityValue = priority === 'high' ? 3 : priority === 'normal' ? 2 : 1;

            const request: QueuedRequest = {
                id: `${Date.now()}-${Math.random()}`,
                priority: priorityValue,
                timestamp: Date.now(),
                execute,
                resolve,
                reject
            };

            this.queue.push(request);
            this.queue.sort((a, b) => b.priority - a.priority || a.timestamp - b.timestamp);

            metricsTracker.recordQueued();
            this.process();
        });
    }

    private async process(): Promise<void> {
        if (this.processing) return;
        this.processing = true;

        while (this.queue.length > 0 && this.activeRequests < this.maxConcurrent) {
            const request = this.queue.shift();
            if (!request) continue;

            this.activeRequests++;

            request.execute()
                .then(result => request.resolve(result))
                .catch(error => request.reject(error))
                .finally(() => {
                    this.activeRequests--;
                    this.process();
                });
        }

        this.processing = false;
    }

    clear(): void {
        this.queue.forEach(request => {
            request.reject(new Error('Queue cleared'));
        });
        this.queue = [];
    }

    get size(): number {
        return this.queue.length;
    }

    get active(): number {
        return this.activeRequests;
    }
}

export const requestQueue = RequestQueue.getInstance();