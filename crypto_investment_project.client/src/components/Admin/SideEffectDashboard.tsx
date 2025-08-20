// src/components/SideEffectDashboard.tsx
import React, { useState, useEffect } from 'react';
import { useSideEffectMonitoring } from '@/providers/SideEffectProvider';

const SideEffectDashboard: React.FC = () => {
    const { getReport } = useSideEffectMonitoring();
    const [metrics, setMetrics] = useState(getReport());
    const [backendMetrics, setBackendMetrics] = useState(null);

    useEffect(() => {
        const interval = setInterval(() => {
            setMetrics(getReport());
        }, 5000);

        return () => clearInterval(interval);
    }, []);

    useEffect(() => {
        // Fetch backend metrics
        api.get('/api/admin/idempotency/metrics')
            .then(res => setBackendMetrics(res.data));
    }, []);

    return (
        <div className="dashboard">
            <h2>Side Effect Prevention Metrics</h2>

            <div className="metrics-grid">
                <div className="metric-card">
                    <h3>Frontend Prevention</h3>
                    <ul>
                        <li>Duplicates Prevented: {metrics.duplicatesPrevented}</li>
                        <li>Requests Cancelled: {metrics.requestsCancelled}</li>
                        <li>Submissions Blocked: {metrics.submissionsBlocked}</li>
                        <li>Requests Throttled: {metrics.requestsThrottled}</li>
                        <li>Requests Debounced: {metrics.requestsDebounced}</li>
                    </ul>
                </div>

                {backendMetrics && (
                    <div className="metric-card">
                        <h3>Backend Prevention</h3>
                        <ul>
                            <li>Cache Hits: {backendMetrics.metrics.cacheHits}</li>
                            <li>Cache Hit Rate: {backendMetrics.metrics.hitRate.toFixed(2)}%</li>
                            <li>Lock Contentions: {backendMetrics.metrics.lockContentions}</li>
                            <li>Total Requests: {backendMetrics.metrics.totalRequests}</li>
                        </ul>
                    </div>
                )}
            </div>

            <div className="savings">
                <h3>Resource Savings</h3>
                <p>
                    Prevented {metrics.duplicatesPrevented + metrics.submissionsBlocked}
                    unnecessary backend calls
                </p>
                <p>
                    Estimated savings: {((metrics.duplicatesPrevented + metrics.submissionsBlocked) * 0.001).toFixed(2)}$
                    (at $0.001 per request)
                </p>
            </div>
        </div>
    );
};