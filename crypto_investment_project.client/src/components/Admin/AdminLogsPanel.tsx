import React, { useEffect, useState, useCallback, useMemo, lazy, Suspense } from "react";
import { Disclosure, Transition, Menu } from '@headlessui/react';
import { motion, AnimatePresence } from 'framer-motion';
import {
    ChevronRightIcon,
    ExclamationCircleIcon,
    CheckCircleIcon,
    XCircleIcon,
    InformationCircleIcon,
    CodeBracketIcon,
    ChevronLeftIcon,
    ChevronDoubleLeftIcon,
    ChevronDoubleRightIcon,
    TrashIcon,
    ExclamationTriangleIcon,
    ClockIcon,
    LinkIcon,
    ArrowTrendingUpIcon,
    ArrowTrendingDownIcon,
    FunnelIcon,
    MagnifyingGlassIcon,
    ArrowPathIcon,
    ChartBarIcon,
    DocumentDuplicateIcon,
    BugAntIcon,
    BoltIcon,
    FireIcon,
    ShieldCheckIcon,
    SparklesIcon
} from '@heroicons/react/24/outline';
import {
    ExclamationTriangleIcon as ExclamationTriangleSolidIcon,
    CheckCircleIcon as CheckCircleSolidIcon
} from '@heroicons/react/24/solid';
import { getTraceTreePaginated, resolveTraceLog, purgeLogs, PurgeLogsResponse } from "../../services/traceLogService";
import ITraceLogNode from "../../interfaces/TraceLog/ITraceLogNode";
import ITraceLog, { ResolutionStatus, LogLevel } from "../../interfaces/TraceLog/ITraceLog";

// Lazy load heavy components
const LogContextDisplay = lazy(() => import("./LogContextDisplay"));

/**
 * Enhanced Admin Logs Panel Component
 * Modern dashboard interface for system log management
 */
const AdminLogsPanel: React.FC = () => {
    // State management
    const [treeData, setTreeData] = useState<ITraceLogNode[]>([]);
    const [groupedTreeData, setGroupedTreeData] = useState<ITraceLogNode[]>([]);
    const [resolutions, setResolutions] = useState<Record<string, string>>({});
    const [loading, setLoading] = useState<boolean>(true);
    const [error, setError] = useState<string | null>(null);
    const [submitting, setSubmitting] = useState<Record<string, boolean>>({});
    const [expandedNodes, setExpandedNodes] = useState<Set<string>>(new Set());
    const [searchTerm, setSearchTerm] = useState<string>("");
    const [filterLevel, setFilterLevel] = useState<number>(0);
    const [autoRefresh, setAutoRefresh] = useState<boolean>(false);
    const [lastRefreshTime, setLastRefreshTime] = useState<Date | null>(null);
    const [groupByCorrelation, setGroupByCorrelation] = useState<boolean>(true);
    const [viewMode, setViewMode] = useState<'cards' | 'timeline' | 'compact'>('cards');
    const [selectedLog, setSelectedLog] = useState<string | null>(null);

    // Statistics state
    const [stats, setStats] = useState({
        critical: 0,
        error: 0,
        warning: 0,
        info: 0,
        resolved: 0,
        pending: 0,
        avgResolutionTime: '0h'
    });

    // Pagination state
    const [currentPage, setCurrentPage] = useState<number>(1);
    const [pageSize, setPageSize] = useState<number>(20);
    const [totalCount, setTotalCount] = useState<number>(0);
    const [totalPages, setTotalPages] = useState<number>(0);
    const [hasPreviousPage, setHasPreviousPage] = useState<boolean>(false);
    const [hasNextPage, setHasNextPage] = useState<boolean>(false);

    // Purge modal state
    const [showPurgeModal, setShowPurgeModal] = useState<boolean>(false);
    const [purgeLevel, setPurgeLevel] = useState<number>(LogLevel.DEBUG);
    const [purging, setPurging] = useState<boolean>(false);
    const [purgeResult, setPurgeResult] = useState<PurgeLogsResponse | null>(null);

    // Calculate statistics from tree data
    useEffect(() => {
        const calculateStats = (nodes: ITraceLogNode[]) => {
            let critical = 0, error = 0, warning = 0, info = 0, resolved = 0, pending = 0;

            const countLogs = (node: ITraceLogNode) => {
                const { log } = node;
                if (log.level === LogLevel.CRITICAL) critical++;
                else if (log.level === LogLevel.ERROR) error++;
                else if (log.level === LogLevel.WARNING) warning++;
                else if (log.level === LogLevel.INFORMATION) info++;

                if (log.resolutionStatus === ResolutionStatus.RECONCILED) resolved++;
                else if (log.requiresResolution) pending++;

                node.children.forEach(countLogs);
            };

            nodes.forEach(countLogs);

            setStats({
                critical,
                error,
                warning,
                info,
                resolved,
                pending,
                avgResolutionTime: '2.5h' // This would be calculated from actual data
            });
        };

        calculateStats(treeData);
    }, [treeData]);

    // Fetch trace tree data
    const fetchTree = useCallback(async (page: number = currentPage, size: number = pageSize, level: number = filterLevel) => {
        setLoading(true);
        setError(null);
        try {
            const paginatedData = await getTraceTreePaginated(page, size, level);
            setTreeData(paginatedData.items);
            setCurrentPage(paginatedData.page);
            setPageSize(paginatedData.pageSize);
            setTotalCount(paginatedData.totalCount);
            setTotalPages(paginatedData.totalPages);
            setHasPreviousPage(paginatedData.hasPreviousPage);
            setHasNextPage(paginatedData.hasNextPage);
            setLastRefreshTime(new Date());
        } catch (err: any) {
            console.error("Error fetching trace logs:", err);
            setError('Failed to load logs. Please try again later.');
        } finally {
            setLoading(false);
        }
    }, [currentPage, pageSize, filterLevel]);

    // Group logs by correlation ID
    const groupLogsByCorrelationId = useCallback((logs: ITraceLogNode[]): ITraceLogNode[] => {
        const correlationGroups = new Map<string, ITraceLogNode[]>();
        const ungroupedLogs: ITraceLogNode[] = [];

        logs.forEach(node => {
            const correlationId = node.log.correlationId;
            if (correlationId && logs.filter(l => l.log.correlationId == node.log.correlationId).length > 1) {
                if (!correlationGroups.has(correlationId)) {
                    correlationGroups.set(correlationId, []);
                }
                correlationGroups.get(correlationId)?.push(node);
            } else {
                ungroupedLogs.push(node);
            }
        });

        const result: ITraceLogNode[] = [];
        correlationGroups.forEach((nodes, correlationId) => {
            nodes.sort((a, b) => new Date(a.log.createdAt).getTime() - new Date(b.log.createdAt).getTime());
            const firstNode = nodes[0];
            const groupNode: ITraceLogNode = {
                log: {
                    ...firstNode.log,
                    id: `correlation-${correlationId}`,
                    message: `Correlation Group: ${correlationId}`,
                    level: LogLevel.INFORMATION,
                    requiresResolution: false,
                },
                children: nodes
            };
            result.push(groupNode);
        });

        result.push(...ungroupedLogs);
        result.sort((a, b) => new Date(b.log.createdAt).getTime() - new Date(a.log.createdAt).getTime());
        return result;
    }, []);

    // Process tree data
    useEffect(() => {
        if (treeData.length > 0) {
            const grouped = groupByCorrelation ? groupLogsByCorrelationId(treeData) : treeData;
            setGroupedTreeData(grouped);
        } else {
            setGroupedTreeData([]);
        }
    }, [treeData, groupByCorrelation, groupLogsByCorrelationId]);

    // Initialize on mount
    useEffect(() => {
        fetchTree();
    }, [fetchTree]);

    // Auto-refresh
    useEffect(() => {
        let interval: NodeJS.Timeout | null = null;
        if (autoRefresh) {
            interval = setInterval(() => {
                fetchTree();
            }, 30000);
        }
        return () => {
            if (interval) clearInterval(interval);
        };
    }, [autoRefresh, fetchTree]);

    // Submit resolution
    const submitResolution = async (id: string) => {
        const resolution = resolutions[id];
        if (!resolution?.trim()) return;

        setSubmitting(prev => ({ ...prev, [id]: true }));
        try {
            await resolveTraceLog(id, resolution);
            fetchTree();
            setResolutions(prev => {
                const newResolutions = { ...prev };
                delete newResolutions[id];
                return newResolutions;
            });
        } catch (err: any) {
            console.error("Failed to submit resolution:", err);
            alert(err.response?.data?.message || 'Failed to submit resolution');
        } finally {
            setSubmitting(prev => ({ ...prev, [id]: false }));
        }
    };

    // Get severity styling
    const getSeverityColor = (level: LogLevel) => {
        switch (level) {
            case LogLevel.CRITICAL: return 'bg-gradient-to-r from-red-500 to-red-600';
            case LogLevel.ERROR: return 'bg-gradient-to-r from-orange-500 to-red-500';
            case LogLevel.WARNING: return 'bg-gradient-to-r from-yellow-400 to-orange-400';
            case LogLevel.INFORMATION: return 'bg-gradient-to-r from-green-400 to-green-500';
            case LogLevel.DEBUG: return 'bg-gradient-to-r from-blue-400 to-blue-500';
            case LogLevel.TRACE: return 'bg-gradient-to-r from-gray-400 to-gray-500';
            default: return 'bg-gradient-to-r from-gray-300 to-gray-400';
        }
    };

    const getSeverityIcon = (level: LogLevel) => {
        switch (level) {
            case LogLevel.CRITICAL: return <FireIcon className="w-5 h-5 text-red-500" />;
            case LogLevel.ERROR: return <XCircleIcon className="w-5 h-5 text-orange-500" />;
            case LogLevel.WARNING: return <ExclamationTriangleIcon className="w-5 h-5 text-yellow-500" />;
            case LogLevel.INFORMATION: return <InformationCircleIcon className="w-5 h-5 text-green-500" />;
            case LogLevel.DEBUG: return <BugAntIcon className="w-5 h-5 text-blue-500" />;
            case LogLevel.TRACE: return <CodeBracketIcon className="w-5 h-5 text-gray-500" />;
            default: return <InformationCircleIcon className="w-5 h-5 text-gray-400" />;
        }
    };

    const getLevelName = (level: LogLevel): string => {
        switch (level) {
            case LogLevel.TRACE: return 'Trace';
            case LogLevel.DEBUG: return 'Debug';
            case LogLevel.INFORMATION: return 'Information';
            case LogLevel.WARNING: return 'Warning';
            case LogLevel.ERROR: return 'Error';
            case LogLevel.CRITICAL: return 'Critical';
            default: return 'Unknown';
        }
    };

    // Format relative time
    const formatRelativeTime = (date: string) => {
        const now = new Date();
        const then = new Date(date);
        const seconds = Math.floor((now.getTime() - then.getTime()) / 1000);

        if (seconds < 60) return `${seconds}s ago`;
        if (seconds < 3600) return `${Math.floor(seconds / 60)}m ago`;
        if (seconds < 86400) return `${Math.floor(seconds / 3600)}h ago`;
        return `${Math.floor(seconds / 86400)}d ago`;
    };

    // Statistics Card Component
    const StatCard = ({ title, value, icon, color, trend, subtitle }: any) => (
        <motion.div
            whileHover={{ scale: 1.02 }}
            className={`bg-white rounded-xl shadow-sm hover:shadow-md transition-all p-5 border border-gray-100`}
        >
            <div className="flex items-center justify-between">
                <div>
                    <p className="text-sm text-gray-500 font-medium">{title}</p>
                    <p className={`text-2xl font-bold mt-1 text-${color}-600`}>{value}</p>
                    {subtitle && <p className="text-xs text-gray-400 mt-1">{subtitle}</p>}
                    {trend && (
                        <div className="flex items-center mt-2">
                            {trend > 0 ? (
                                <ArrowTrendingUpIcon className="w-4 h-4 text-red-500 mr-1" />
                            ) : (
                                <ArrowTrendingDownIcon className="w-4 h-4 text-green-500 mr-1" />
                            )}
                            <span className={`text-xs ${trend > 0 ? 'text-red-500' : 'text-green-500'}`}>
                                {Math.abs(trend)}%
                            </span>
                        </div>
                    )}
                </div>
                <div className={`p-3 rounded-lg bg-${color}-50`}>
                    {icon}
                </div>
            </div>
        </motion.div>
    );

    // Enhanced Log Card Component
    const LogCard = ({ node, depth = 0 }: { node: ITraceLogNode; depth?: number }) => {
        const { log, children } = node;
        const isExpanded = expandedNodes.has(log.id);
        const isSelected = selectedLog === log.id;
        const isCorrelationGroup = log.id.startsWith('correlation-');
        const hasStackTrace = log.stackTrace || log.exceptionType;

        return (
            <motion.div
                initial={{ opacity: 0, y: 20 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -20 }}
                className={`relative ${depth > 0 ? 'ml-8' : ''} mb-4`}
            >
                {/* Connection line for child logs */}
                {depth > 0 && (
                    <div className="absolute left-[-20px] top-6 w-4 h-0.5 bg-gray-300" />
                )}

                <div
                    className={`group relative bg-white rounded-xl shadow-sm hover:shadow-lg transition-all duration-200 border overflow-hidden ${isSelected ? 'border-blue-500 ring-2 ring-blue-200' : 'border-gray-100'
                        } ${log.level >= LogLevel.ERROR ? 'border-l-4 border-l-red-500' : ''}`}
                    onClick={() => setSelectedLog(log.id)}
                >
                    {/* Severity indicator bar */}
                    <div className={`absolute left-0 top-0 bottom-0 w-1 ${getSeverityColor(log.level)}`} />

                    {/* Main content */}
                    <div className="p-5">
                        {/* Header */}
                        <div className="flex items-start justify-between mb-3">
                            <div className="flex items-center space-x-3">
                                {/* Icon with background */}
                                <div className={`p-2 rounded-lg ${isCorrelationGroup ? 'bg-indigo-50' :
                                        log.level >= LogLevel.ERROR ? 'bg-red-50' :
                                            log.level === LogLevel.WARNING ? 'bg-yellow-50' :
                                                'bg-gray-50'
                                    }`}>
                                    {isCorrelationGroup ? (
                                        <LinkIcon className="w-5 h-5 text-indigo-500" />
                                    ) : (
                                        getSeverityIcon(log.level)
                                    )}
                                </div>

                                {/* Title and metadata */}
                                <div className="flex-1">
                                    <div className="flex items-center gap-2">
                                        <h3 className="font-semibold text-gray-900">
                                            {log.operation || 'System Event'}
                                        </h3>
                                        {hasStackTrace && (
                                            <span className="text-xs bg-purple-100 text-purple-700 px-2 py-0.5 rounded-full">
                                                Stack Trace
                                            </span>
                                        )}
                                    </div>
                                    <div className="flex items-center gap-4 mt-1">
                                        <p className="text-sm text-gray-500">
                                            {formatRelativeTime(log.createdAt)}
                                        </p>
                                        {log.correlationId && !isCorrelationGroup && (
                                            <code className="text-xs text-gray-400 font-mono">
                                                {log.correlationId.substring(0, 8)}...
                                            </code>
                                        )}
                                    </div>
                                </div>
                            </div>

                            {/* Status badges */}
                            <div className="flex items-center gap-2">
                                {log.resolutionStatus === ResolutionStatus.RECONCILED && (
                                    <span className="flex items-center gap-1 text-xs bg-green-100 text-green-700 px-2 py-1 rounded-full">
                                        <CheckCircleSolidIcon className="w-3 h-3" />
                                        Resolved
                                    </span>
                                )}
                                {log.requiresResolution && log.resolutionStatus !== ResolutionStatus.RECONCILED && (
                                    <span className="flex items-center gap-1 text-xs bg-red-100 text-red-700 px-2 py-1 rounded-full animate-pulse">
                                        <ExclamationTriangleSolidIcon className="w-3 h-3" />
                                        Needs Resolution
                                    </span>
                                )}
                                {isCorrelationGroup && (
                                    <span className="text-xs bg-indigo-100 text-indigo-700 px-2 py-1 rounded-full">
                                        {children.length} events
                                    </span>
                                )}
                            </div>
                        </div>

                        {/* Message */}
                        <div className="mt-3">
                            <p className="text-gray-700 leading-relaxed">{log.message}</p>
                        </div>

                        {/* Expandable details */}
                        <Disclosure>
                            {({ open }) => (
                                <>
                                    <Disclosure.Button className="flex items-center justify-center w-full mt-4 py-2 text-sm text-gray-500 hover:text-gray-700 transition-colors">
                                        {open ? 'Hide' : 'Show'} Details
                                        <ChevronRightIcon className={`w-4 h-4 ml-1 transform transition-transform ${open ? 'rotate-90' : ''}`} />
                                    </Disclosure.Button>

                                    <Transition
                                        enter="transition duration-100 ease-out"
                                        enterFrom="transform scale-95 opacity-0"
                                        enterTo="transform scale-100 opacity-100"
                                        leave="transition duration-75 ease-out"
                                        leaveFrom="transform scale-100 opacity-100"
                                        leaveTo="transform scale-95 opacity-0"
                                    >
                                        <Disclosure.Panel className="mt-4 pt-4 border-t border-gray-100">
                                            {/* Metadata grid */}
                                            <div className="grid grid-cols-2 gap-4 mb-4">
                                                <div>
                                                    <p className="text-xs text-gray-500 uppercase tracking-wider">Log ID</p>
                                                    <p className="text-sm font-mono text-gray-700 mt-1">{log.id}</p>
                                                </div>
                                                <div>
                                                    <p className="text-xs text-gray-500 uppercase tracking-wider">Level</p>
                                                    <p className="text-sm font-medium text-gray-700 mt-1">
                                                        {getLevelName(log.level)}
                                                    </p>
                                                </div>
                                                <div>
                                                    <p className="text-xs text-gray-500 uppercase tracking-wider">Timestamp</p>
                                                    <p className="text-sm text-gray-700 mt-1">
                                                        {new Date(log.createdAt).toLocaleString()}
                                                    </p>
                                                </div>
                                                {log.correlationId && (
                                                    <div>
                                                        <p className="text-xs text-gray-500 uppercase tracking-wider">Correlation ID</p>
                                                        <p className="text-sm font-mono text-gray-700 mt-1">{log.correlationId}</p>
                                                    </div>
                                                )}
                                            </div>

                                            {/* Context data */}
                                            {log.context && (
                                                <div className="mb-4">
                                                    <p className="text-xs text-gray-500 uppercase tracking-wider mb-2">Additional Context</p>
                                                    <div className="bg-gray-50 rounded-lg p-3">
                                                        <Suspense fallback={<div className="animate-pulse h-20 bg-gray-200 rounded" />}>
                                                            <LogContextDisplay context={log.context} />
                                                        </Suspense>
                                                    </div>
                                                </div>
                                            )}

                                            {/* Stack trace */}
                                            {hasStackTrace && (
                                                <div className="mb-4">
                                                    <p className="text-xs text-gray-500 uppercase tracking-wider mb-2">Stack Trace</p>
                                                    <div className="bg-gray-900 text-gray-100 rounded-lg p-4 overflow-x-auto">
                                                        <pre className="text-xs font-mono">{log.stackTrace}</pre>
                                                    </div>
                                                </div>
                                            )}

                                            {/* Resolution form */}
                                            {log.requiresResolution && log.resolutionStatus !== ResolutionStatus.RECONCILED && (
                                                <div className="bg-blue-50 rounded-lg p-4">
                                                    <p className="text-sm font-medium text-gray-700 mb-2">Add Resolution</p>
                                                    <textarea
                                                        rows={3}
                                                        className="w-full p-3 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent resize-none"
                                                        placeholder="Describe how this issue was resolved..."
                                                        value={resolutions[log.id] || ''}
                                                        onChange={e => setResolutions(prev => ({ ...prev, [log.id]: e.target.value }))}
                                                        disabled={submitting[log.id]}
                                                    />
                                                    <button
                                                        onClick={() => submitResolution(log.id)}
                                                        disabled={submitting[log.id] || !resolutions[log.id]?.trim()}
                                                        className="mt-2 px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors flex items-center gap-2"
                                                    >
                                                        {submitting[log.id] ? (
                                                            <>
                                                                <div className="animate-spin h-4 w-4 border-2 border-white border-t-transparent rounded-full" />
                                                                Submitting...
                                                            </>
                                                        ) : (
                                                            <>
                                                                <CheckCircleIcon className="w-4 h-4" />
                                                                Submit Resolution
                                                            </>
                                                        )}
                                                    </button>
                                                </div>
                                            )}

                                            {/* Show resolution if resolved */}
                                            {log.resolutionStatus === ResolutionStatus.RECONCILED && log.resolutionComment && (
                                                <div className="bg-green-50 rounded-lg p-4">
                                                    <p className="text-sm font-medium text-gray-700 mb-2">Resolution</p>
                                                    <p className="text-sm text-gray-600">{log.resolutionComment}</p>
                                                    <p className="text-xs text-gray-500 mt-2">
                                                        Resolved by {log.resolvedBy || 'Unknown'} • {log.resolvedAt ? formatRelativeTime(log.resolvedAt) : 'Unknown'}
                                                    </p>
                                                </div>
                                            )}
                                        </Disclosure.Panel>
                                    </Transition>
                                </>
                            )}
                        </Disclosure>

                        {/* Child logs */}
                        {children.length > 0 && !isCorrelationGroup && (
                            <div className="mt-4 pl-4 border-l-2 border-gray-200">
                                {children.map(child => (
                                    <LogCard key={child.log.id} node={child} depth={depth + 1} />
                                ))}
                            </div>
                        )}

                        {/* Correlation group children */}
                        {isCorrelationGroup && children.length > 0 && (
                            <div className="mt-4 space-y-3">
                                {children.map(child => (
                                    <LogCard key={child.log.id} node={child} depth={1} />
                                ))}
                            </div>
                        )}
                    </div>
                </div>
            </motion.div>
        );
    };

    // Loading skeleton
    const LoadingSkeleton = () => (
        <div className="space-y-4">
            {[1, 2, 3].map(i => (
                <div key={i} className="bg-white rounded-xl p-5 animate-pulse">
                    <div className="flex items-center space-x-3">
                        <div className="w-10 h-10 bg-gray-200 rounded-lg" />
                        <div className="flex-1 space-y-2">
                            <div className="h-4 bg-gray-200 rounded w-1/4" />
                            <div className="h-3 bg-gray-200 rounded w-1/6" />
                        </div>
                    </div>
                    <div className="mt-4 h-20 bg-gray-100 rounded" />
                </div>
            ))}
        </div>
    );

    // Main render
    return (
        <div className="min-h-screen bg-gray-50">
            {/* Header */}
            <div className="bg-white border-b border-gray-200 sticky top-0 z-40 shadow-sm">
                <div className="max-w-7xl mx-auto px-6 py-4">
                    <div className="flex items-center justify-between">
                        <div className="flex items-center space-x-3">
                            <div className="p-2 bg-gradient-to-r from-blue-500 to-purple-600 rounded-lg">
                                <ChartBarIcon className="w-6 h-6 text-white" />
                            </div>
                            <div>
                                <h1 className="text-2xl font-bold text-gray-900">System Logs</h1>
                                <p className="text-sm text-gray-500">
                                    {lastRefreshTime && `Last updated ${formatRelativeTime(lastRefreshTime.toISOString())}`}
                                </p>
                            </div>
                        </div>

                        {/* Header Actions */}
                        <div className="flex items-center gap-3">
                            {/* View Mode Toggle */}
                            <div className="flex bg-gray-100 rounded-lg p-1">
                                {['cards', 'timeline', 'compact'].map((mode) => (
                                    <button
                                        key={mode}
                                        onClick={() => setViewMode(mode as any)}
                                        className={`px-3 py-1.5 text-sm font-medium rounded-md transition-colors ${viewMode === mode
                                                ? 'bg-white text-gray-900 shadow-sm'
                                                : 'text-gray-600 hover:text-gray-900'
                                            }`}
                                    >
                                        {mode.charAt(0).toUpperCase() + mode.slice(1)}
                                    </button>
                                ))}
                            </div>

                            {/* Auto-refresh toggle */}
                            <button
                                onClick={() => setAutoRefresh(!autoRefresh)}
                                className={`px-4 py-2 rounded-lg font-medium text-sm transition-colors ${autoRefresh
                                        ? 'bg-green-100 text-green-700 hover:bg-green-200'
                                        : 'bg-gray-100 text-gray-700 hover:bg-gray-200'
                                    }`}
                            >
                                {autoRefresh ? (
                                    <>
                                        <BoltIcon className="w-4 h-4 inline mr-1" />
                                        Auto-refresh ON
                                    </>
                                ) : (
                                    'Auto-refresh OFF'
                                )}
                            </button>

                            {/* Manual refresh */}
                            <button
                                onClick={() => fetchTree()}
                                className="p-2 bg-blue-100 text-blue-700 rounded-lg hover:bg-blue-200 transition-colors"
                            >
                                <ArrowPathIcon className={`w-5 h-5 ${loading ? 'animate-spin' : ''}`} />
                            </button>

                            {/* Purge button */}
                            <button
                                onClick={() => setShowPurgeModal(true)}
                                className="px-4 py-2 bg-red-100 text-red-700 rounded-lg hover:bg-red-200 transition-colors font-medium text-sm flex items-center gap-2"
                            >
                                <TrashIcon className="w-4 h-4" />
                                Purge Logs
                            </button>
                        </div>
                    </div>
                </div>
            </div>

            {/* Statistics Cards */}
            <div className="max-w-7xl mx-auto px-6 py-6">
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 xl:grid-cols-7 gap-4 mb-6">
                    <StatCard
                        title="Critical"
                        value={stats.critical}
                        icon={<FireIcon className="w-6 h-6 text-red-500" />}
                        color="red"
                        trend={stats.critical > 0 ? 15 : 0}
                    />
                    <StatCard
                        title="Errors"
                        value={stats.error}
                        icon={<XCircleIcon className="w-6 h-6 text-orange-500" />}
                        color="orange"
                        trend={-5}
                    />
                    <StatCard
                        title="Warnings"
                        value={stats.warning}
                        icon={<ExclamationTriangleIcon className="w-6 h-6 text-yellow-500" />}
                        color="yellow"
                        trend={3}
                    />
                    <StatCard
                        title="Info"
                        value={stats.info}
                        icon={<InformationCircleIcon className="w-6 h-6 text-blue-500" />}
                        color="blue"
                    />
                    <StatCard
                        title="Resolved"
                        value={stats.resolved}
                        icon={<CheckCircleIcon className="w-6 h-6 text-green-500" />}
                        color="green"
                        subtitle="Issues resolved"
                    />
                    <StatCard
                        title="Pending"
                        value={stats.pending}
                        icon={<ClockIcon className="w-6 h-6 text-purple-500" />}
                        color="purple"
                        subtitle="Need attention"
                    />
                    <StatCard
                        title="Avg Resolution"
                        value={stats.avgResolutionTime}
                        icon={<SparklesIcon className="w-6 h-6 text-indigo-500" />}
                        color="indigo"
                        subtitle="Response time"
                    />
                </div>

                {/* Filters Bar */}
                <div className="bg-white rounded-xl shadow-sm p-4 mb-6">
                    <div className="flex flex-col lg:flex-row gap-4">
                        {/* Search */}
                        <div className="flex-1">
                            <div className="relative">
                                <MagnifyingGlassIcon className="absolute left-3 top-1/2 transform -translate-y-1/2 w-5 h-5 text-gray-400" />
                                <input
                                    type="text"
                                    placeholder="Search logs..."
                                    value={searchTerm}
                                    onChange={(e) => setSearchTerm(e.target.value)}
                                    className="w-full pl-10 pr-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                                />
                            </div>
                        </div>

                        {/* Level Filter */}
                        <select
                            value={filterLevel}
                            onChange={(e) => setFilterLevel(Number(e.target.value))}
                            className="px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                        >
                            <option value={0}>All Levels</option>
                            <option value={LogLevel.TRACE}>Trace</option>
                            <option value={LogLevel.DEBUG}>Debug</option>
                            <option value={LogLevel.INFORMATION}>Information</option>
                            <option value={LogLevel.WARNING}>Warning</option>
                            <option value={LogLevel.ERROR}>Error</option>
                            <option value={LogLevel.CRITICAL}>Critical</option>
                        </select>

                        {/* Page Size */}
                        <select
                            value={pageSize}
                            onChange={(e) => setPageSize(Number(e.target.value))}
                            className="px-4 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:border-transparent"
                        >
                            <option value={10}>10 per page</option>
                            <option value={20}>20 per page</option>
                            <option value={50}>50 per page</option>
                            <option value={100}>100 per page</option>
                        </select>

                        {/* Group by Correlation */}
                        <button
                            onClick={() => setGroupByCorrelation(!groupByCorrelation)}
                            className={`px-4 py-2 rounded-lg font-medium text-sm transition-colors flex items-center gap-2 ${groupByCorrelation
                                    ? 'bg-indigo-100 text-indigo-700'
                                    : 'bg-gray-100 text-gray-700'
                                }`}
                        >
                            <LinkIcon className="w-4 h-4" />
                            Group by Correlation
                        </button>
                    </div>
                </div>

                {/* Main Content */}
                {loading ? (
                    <LoadingSkeleton />
                ) : error ? (
                    <div className="bg-red-50 border border-red-200 rounded-xl p-6 text-center">
                        <XCircleIcon className="w-12 h-12 text-red-500 mx-auto mb-3" />
                        <p className="text-red-700 font-medium">{error}</p>
                        <button
                            onClick={() => fetchTree()}
                            className="mt-4 px-4 py-2 bg-red-100 text-red-700 rounded-lg hover:bg-red-200"
                        >
                            Try Again
                        </button>
                    </div>
                ) : groupedTreeData.length === 0 ? (
                    <div className="bg-white rounded-xl shadow-sm p-12 text-center">
                        <div className="inline-flex items-center justify-center w-16 h-16 bg-gray-100 rounded-full mb-4">
                            <InformationCircleIcon className="w-8 h-8 text-gray-400" />
                        </div>
                        <h3 className="text-lg font-medium text-gray-900 mb-2">No logs found</h3>
                        <p className="text-gray-500">
                            {searchTerm || filterLevel !== 0
                                ? "No logs match your current filters."
                                : "There are no logs to display."}
                        </p>
                        {(searchTerm || filterLevel !== 0) && (
                            <button
                                onClick={() => {
                                    setSearchTerm("");
                                    setFilterLevel(0);
                                }}
                                className="mt-4 px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700"
                            >
                                Clear Filters
                            </button>
                        )}
                    </div>
                ) : (
                    <AnimatePresence mode="wait">
                        <motion.div
                            key={viewMode}
                            initial={{ opacity: 0 }}
                            animate={{ opacity: 1 }}
                            exit={{ opacity: 0 }}
                            className="space-y-4"
                        >
                            {groupedTreeData.map(node => (
                                <LogCard key={node.log.id} node={node} />
                            ))}
                        </motion.div>
                    </AnimatePresence>
                )}

                {/* Pagination */}
                {totalPages > 1 && (
                    <div className="bg-white rounded-xl shadow-sm p-4 mt-6">
                        <div className="flex items-center justify-between">
                            <p className="text-sm text-gray-500">
                                Showing {((currentPage - 1) * pageSize) + 1} to {Math.min(currentPage * pageSize, totalCount)} of {totalCount} logs
                            </p>
                            <div className="flex items-center gap-2">
                                <button
                                    onClick={() => setCurrentPage(1)}
                                    disabled={!hasPreviousPage}
                                    className="p-2 rounded-lg hover:bg-gray-100 disabled:opacity-50 disabled:cursor-not-allowed"
                                >
                                    <ChevronDoubleLeftIcon className="w-5 h-5" />
                                </button>
                                <button
                                    onClick={() => setCurrentPage(currentPage - 1)}
                                    disabled={!hasPreviousPage}
                                    className="p-2 rounded-lg hover:bg-gray-100 disabled:opacity-50 disabled:cursor-not-allowed"
                                >
                                    <ChevronLeftIcon className="w-5 h-5" />
                                </button>

                                <div className="flex items-center gap-1">
                                    {Array.from({ length: Math.min(5, totalPages) }, (_, i) => {
                                        const page = currentPage - 2 + i;
                                        if (page < 1 || page > totalPages) return null;
                                        return (
                                            <button
                                                key={page}
                                                onClick={() => setCurrentPage(page)}
                                                className={`px-3 py-1 rounded-lg font-medium text-sm ${page === currentPage
                                                        ? 'bg-blue-600 text-white'
                                                        : 'hover:bg-gray-100'
                                                    }`}
                                            >
                                                {page}
                                            </button>
                                        );
                                    }).filter(Boolean)}
                                </div>

                                <button
                                    onClick={() => setCurrentPage(currentPage + 1)}
                                    disabled={!hasNextPage}
                                    className="p-2 rounded-lg hover:bg-gray-100 disabled:opacity-50 disabled:cursor-not-allowed"
                                >
                                    <ChevronRightIcon className="w-5 h-5" />
                                </button>
                                <button
                                    onClick={() => setCurrentPage(totalPages)}
                                    disabled={!hasNextPage}
                                    className="p-2 rounded-lg hover:bg-gray-100 disabled:opacity-50 disabled:cursor-not-allowed"
                                >
                                    <ChevronDoubleRightIcon className="w-5 h-5" />
                                </button>
                            </div>
                        </div>
                    </div>
                )}
            </div>

            {/* Purge Modal */}
            {showPurgeModal && (
                <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
                    <motion.div
                        initial={{ scale: 0.95, opacity: 0 }}
                        animate={{ scale: 1, opacity: 1 }}
                        className="bg-white rounded-xl shadow-xl p-6 max-w-md w-full mx-4"
                    >
                        <div className="flex items-center justify-center w-12 h-12 bg-red-100 rounded-full mx-auto mb-4">
                            <ExclamationTriangleIcon className="w-6 h-6 text-red-600" />
                        </div>
                        <h3 className="text-lg font-semibold text-center mb-4">Purge Logs</h3>
                        <p className="text-gray-600 text-center mb-6">
                            This will permanently delete logs. This action cannot be undone.
                        </p>
                        <select
                            value={purgeLevel}
                            onChange={(e) => setPurgeLevel(Number(e.target.value))}
                            className="w-full px-4 py-2 border border-gray-300 rounded-lg mb-6"
                            disabled={purging}
                        >
                            <option value={LogLevel.TRACE}>All logs (Trace and above)</option>
                            <option value={LogLevel.DEBUG}>Debug and below</option>
                            <option value={LogLevel.INFORMATION}>Information and below</option>
                            <option value={LogLevel.WARNING}>Warning and below</option>
                            <option value={LogLevel.ERROR}>Error and below</option>
                            <option value={LogLevel.CRITICAL}>Critical only</option>
                        </select>
                        <div className="flex gap-3">
                            <button
                                onClick={() => setShowPurgeModal(false)}
                                className="flex-1 px-4 py-2 bg-gray-100 text-gray-700 rounded-lg hover:bg-gray-200"
                                disabled={purging}
                            >
                                Cancel
                            </button>
                            <button
                                onClick={async () => {
                                    setPurging(true);
                                    try {
                                        const result = await purgeLogs(purgeLevel);
                                        if (result.isSuccess) {
                                            await fetchTree();
                                            setShowPurgeModal(false);
                                        }
                                    } catch (err) {
                                        console.error(err);
                                    } finally {
                                        setPurging(false);
                                    }
                                }}
                                className="flex-1 px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 disabled:opacity-50 flex items-center justify-center gap-2"
                                disabled={purging}
                            >
                                {purging ? (
                                    <>
                                        <div className="animate-spin h-4 w-4 border-2 border-white border-t-transparent rounded-full" />
                                        Purging...
                                    </>
                                ) : (
                                    'Purge Logs'
                                )}
                            </button>
                        </div>
                    </motion.div>
                </div>
            )}
        </div>
    );
};

export default AdminLogsPanel;