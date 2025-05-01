import React, { useEffect, useState, useCallback, useMemo } from "react";
import { Disclosure, Transition } from '@headlessui/react';
import { ChevronRightIcon, ExclamationCircleIcon, CheckCircleIcon, XCircleIcon, InformationCircleIcon } from '@heroicons/react/24/outline';
import { getTraceTree, resolveTraceLog } from "../../services/api";
import ITraceLogNode from "../../interfaces/TraceLog/ITraceLogNode";
import ITraceLog, { ResolutionStatus, LogLevel } from "../../interfaces/TraceLog/ITraceLog";
import LogContextDisplay from "./LogContextDisplay";

/**
 * Admin Logs Panel Component
 * Displays a hierarchical view of system logs with resolution capabilities
 * Grouped by correlation ID at the root level
 */
const AdminLogsPanel: React.FC = () => {
    const [treeData, setTreeData] = useState<ITraceLogNode[]>([]);
    const [groupedTreeData, setGroupedTreeData] = useState<ITraceLogNode[]>([]);
    const [resolutions, setResolutions] = useState<Record<string, string>>({});
    const [loading, setLoading] = useState<boolean>(true);
    const [error, setError] = useState<string | null>(null);
    const [submitting, setSubmitting] = useState<Record<string, boolean>>({});
    const [expandedNodes, setExpandedNodes] = useState<Set<string>>(new Set());
    const [searchTerm, setSearchTerm] = useState<string>("");
    const [filterLevel, setFilterLevel] = useState<number | null>(null);
    const [autoRefresh, setAutoRefresh] = useState<boolean>(false);
    const [lastRefreshTime, setLastRefreshTime] = useState<Date | null>(null);
    const [dropdownOpen, setDropdownOpen] = useState<boolean>(false);
    const [groupByCorrelation, setGroupByCorrelation] = useState<boolean>(true);

    // Fetch trace tree data
    const fetchTree = useCallback(async () => {
        setLoading(true);
        setError(null);
        try {
            const response = await getTraceTree();
            setTreeData(response.data);
            setLastRefreshTime(new Date());
        } catch (err: any) {
            console.error("Error fetching trace logs:", err);
            setError('Failed to load logs. Please try again later.');
        } finally {
            setLoading(false);
        }
    }, []);

    // Group logs by correlation ID
    const groupLogsByCorrelationId = useCallback((logs: ITraceLogNode[]): ITraceLogNode[] => {
        // Create a map to store correlation groups
        const correlationGroups = new Map<string, ITraceLogNode[]>();
        // Logs without correlation ID
        const ungroupedLogs: ITraceLogNode[] = [];

        // First pass: categorize logs by correlation ID
        logs.forEach(node => {
            const correlationId = node.log.correlationId;

            if (correlationId && logs.filter(l => l.log.correlationId == node.log.correlationId).length > 1) {
                if (!correlationGroups.has(correlationId)) {
                    correlationGroups.set(correlationId, []);
                }
                correlationGroups.get(correlationId)?.push(node);
            } else {
                // Handle logs without correlation ID
                ungroupedLogs.push(node);
            }
        });

        // Create a grouped tree
        const result: ITraceLogNode[] = [];

        // Add correlation groups as parent nodes
        correlationGroups.forEach((nodes, correlationId) => {
            // Sort nodes by timestamp (oldest first)
            nodes.sort((a, b) =>
                new Date(a.log.createdAt).getTime() - new Date(b.log.createdAt).getTime()
            );

            // Find the earliest log in the group to use as parent
            const firstNode = nodes[0];

            // Create correlation group node
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

        // Add ungrouped logs
        result.push(...ungroupedLogs);

        // Sort correlation groups by time (newest first for better UX)
        result.sort((a, b) =>
            new Date(b.log.createdAt).getTime() - new Date(a.log.createdAt).getTime()
        );

        return result;
    }, []);

    // Process tree data when it changes
    useEffect(() => {
        if (treeData.length > 0) {
            const grouped = groupByCorrelation ? groupLogsByCorrelationId(treeData) : treeData;
            setGroupedTreeData(grouped);

            // Auto-expand correlation groups
            if (groupByCorrelation) {
                const newExpandedNodes = new Set(expandedNodes);
                grouped.forEach(node => {
                    if (isCorrelationGroup(node)) {
                        newExpandedNodes.add(node.log.id);
                    }
                });
                setExpandedNodes(newExpandedNodes);
            }
        } else {
            setGroupedTreeData([]);
        }
    }, [treeData, groupByCorrelation, groupLogsByCorrelationId]);

    // Initialize data on component mount
    useEffect(() => {
        fetchTree();
    }, [fetchTree]);

    // Auto-refresh logs if enabled
    useEffect(() => {
        let interval: NodeJS.Timeout | null = null;

        if (autoRefresh) {
            interval = setInterval(() => {
                fetchTree();
            }, 30000); // Refresh every 30 seconds
        }

        return () => {
            if (interval) clearInterval(interval);
        };
    }, [autoRefresh, fetchTree]);

    // Handle resolution text changes
    const handleResolveChange = (id: string, value: string) => {
        setResolutions(prev => ({ ...prev, [id]: value }));
    };

    // Submit resolution for a log entry
    const submitResolution = async (id: string) => {
        const resolution = resolutions[id];
        if (!resolution?.trim()) {
            return; // Don't submit empty resolutions
        }

        setSubmitting(prev => ({ ...prev, [id]: true }));
        try {
            await resolveTraceLog(id, resolution);
            // Success - refresh tree data
            fetchTree();
            // Clear resolution text
            setResolutions(prev => {
                const newResolutions = { ...prev };
                delete newResolutions[id];
                return newResolutions;
            });
        } catch (err: any) {
            console.error("Failed to submit resolution:", err);
            alert(err.response?.data?.message || 'Failed to submit resolution. Please try again.');
        } finally {
            setSubmitting(prev => ({ ...prev, [id]: false }));
        }
    };

    // Get CSS class based on log level
    const getLevelClass = (level: LogLevel): string => {
        switch (level) {
            case LogLevel.TRACE: return "text-gray-600"; // Trace
            case LogLevel.DEBUG: return "text-blue-600"; // Debug
            case LogLevel.INFORMATION: return "text-green-600"; //Information
            case LogLevel.WARNING: return "text-yellow-600"; // Warning
            case LogLevel.ERROR: return "text-red-600"; // Error
            case LogLevel.CRITICAL: return "text-purple-600"; // Critical
            default: return "text-gray-600";
        }
    };

    // Get background class based on log level and resolution status
    const getNodeBgClass = (level: LogLevel, requiresResolution: boolean, resolutionStatus: ResolutionStatus, isCorrelationGroup: boolean = false): string => {
        if (isCorrelationGroup) return "bg-indigo-50 border border-indigo-200";
        if (resolutionStatus === ResolutionStatus.RECONCILED) return "bg-green-50 border border-green-200";
        if (requiresResolution) return "bg-red-50 border border-red-200";
        if (level >= LogLevel.ERROR) return "bg-red-50 border border-red-200"; // Error or Critical
        if (level === LogLevel.WARNING) return "bg-yellow-50 border border-yellow-200"; // Warning
        return "hover:bg-gray-100";
    };

    // Get icon based on log level and resolution status
    const getLevelIcon = (level: LogLevel, requiresResolution: boolean, resolutionStatus: ResolutionStatus, isCorrelationGroup: boolean = false) => {
        if (isCorrelationGroup) {
            return (
                <svg className="h-5 w-5 text-indigo-500" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M13 10V3L4 14h7v7l9-11h-7z" />
                </svg>
            );
        }

        if (resolutionStatus === ResolutionStatus.RECONCILED) return <CheckCircleIcon className="h-5 w-5 text-green-500" />;
        if (resolutionStatus === ResolutionStatus.ACKNOWLEDGED) return <CheckCircleIcon className="h-5 w-5 text-orange-500" />;
        if (requiresResolution || level >= LogLevel.ERROR) return <XCircleIcon className="h-5 w-5 text-red-500" />;
        if (level === LogLevel.WARNING) return <ExclamationCircleIcon className="h-5 w-5 text-yellow-500" />;
        if (level === LogLevel.INFORMATION) return <InformationCircleIcon className="h-5 w-5 text-green-500" />;
        if (level === LogLevel.DEBUG) return <InformationCircleIcon className="h-5 w-5 text-blue-500" />;
        return <InformationCircleIcon className="h-5 w-5 text-gray-400" />;
    };

    // Get level name from enum
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

    // Toggle expanded state for a node
    const toggleNodeExpanded = (id: string) => {
        setExpandedNodes(prev => {
            const newSet = new Set(prev);
            if (newSet.has(id)) {
                newSet.delete(id);
            } else {
                newSet.add(id);
            }
            return newSet;
        });
    };

    // Expand or collapse all nodes
    const expandCollapseAll = (expand: boolean) => {
        if (expand) {
            // Create a set of all node IDs
            const allNodeIds = new Set<string>();

            // Helper function to collect all IDs recursively
            const collectNodeIds = (nodes: ITraceLogNode[]) => {
                nodes.forEach(node => {
                    allNodeIds.add(node.log.id);
                    if (node.children.length > 0) {
                        collectNodeIds(node.children);
                    }
                });
            };

            collectNodeIds(groupedTreeData);
            setExpandedNodes(allNodeIds);
        } else {
            // Collapse all by clearing the set
            setExpandedNodes(new Set());
        }

        // Close dropdown after action
        setDropdownOpen(false);
    };

    // Check if a node is a correlation group node
    const isCorrelationGroup = (node: ITraceLogNode): boolean => {
        return node.log.id.startsWith('correlation-');
    };

    // Filter nodes based on search term and level filter
    const filterNodes = useCallback((nodes: ITraceLogNode[]): ITraceLogNode[] => {
        if (!searchTerm && filterLevel === null) return nodes;

        return nodes.filter(node => {
            // Special handling for correlation group nodes
            if (isCorrelationGroup(node)) {
                // Filter the children
                const filteredChildren = filterNodes(node.children);

                // Keep the group if any children match
                if (filteredChildren.length > 0) {
                    return {
                        ...node,
                        children: filteredChildren
                    };
                }
                return false;
            }

            // Check if current node matches filters
            const matchesSearch = searchTerm
                ? node.log.message.toLowerCase().includes(searchTerm.toLowerCase())
                : true;

            const matchesLevel = filterLevel !== null
                ? node.log.level === filterLevel
                : true;

            // Check if any children match filters
            const filteredChildren = filterNodes(node.children);

            // Include node if it matches filters or has children that match
            return (matchesSearch && matchesLevel) || filteredChildren.length > 0;
        }).map(node => ({
            ...node,
            children: filterNodes(node.children)
        }));
    }, [searchTerm, filterLevel]);

    // Memoize filtered tree data to avoid recalculating on every render
    const filteredTreeData = useMemo(() => filterNodes(groupedTreeData), [filterNodes, groupedTreeData]);

    // Handle click outside to close dropdown
    useEffect(() => {
        const handleClickOutside = (event: MouseEvent) => {
            const dropdown = document.getElementById('expand-dropdown');
            if (dropdown && !dropdown.contains(event.target as Node)) {
                setDropdownOpen(false);
            }
        };

        document.addEventListener('mousedown', handleClickOutside);
        return () => {
            document.removeEventListener('mousedown', handleClickOutside);
        };
    }, []);

    // Recursive function to render the tree nodes
    const renderNode = (node: ITraceLogNode, depth = 0) => {
        const { log, children } = node;
        const isNodeExpanded = expandedNodes.has(log.id);
        const isSubmitting = submitting[log.id] || false;
        const resolutionStatusEnum = log.resolutionStatus ?? ResolutionStatus.UNRESOLVED;
        const isGroup = isCorrelationGroup(node);

        return (
            <div key={log.id} className={`${depth > 0 ? 'ml-4' : 'ml-0'} mt-2`}>
                <Disclosure defaultOpen={isNodeExpanded}>
                    {({ open }) => (
                        <>
                            <Disclosure.Button
                                className={`flex items-center w-full p-2 rounded-md text-left ${getNodeBgClass(log.level, log.requiresResolution, resolutionStatusEnum, isGroup)}`}
                                onClick={() => toggleNodeExpanded(log.id)}
                            >
                                <ChevronRightIcon
                                    className={`h-5 w-5 transform transition-transform ${open ? 'rotate-90' : ''}`} />
                                <span className="ml-2">{getLevelIcon(log.level, log.requiresResolution, resolutionStatusEnum, isGroup)}</span>
                                <span className={`ml-2 font-medium ${isGroup ? 'text-indigo-700' : getLevelClass(log.level)}`}>
                                    {log.message}
                                </span>

                                {isGroup && (
                                    <span className="ml-2 text-xs bg-indigo-100 text-indigo-800 px-2 py-0.5 rounded-full">
                                        {children.length} related logs
                                    </span>
                                )}

                                {!isGroup && log.requiresResolution && resolutionStatusEnum !== ResolutionStatus.RECONCILED && (
                                    <span className="ml-2 text-xs bg-red-100 text-red-800 px-2 py-0.5 rounded-full">
                                        Needs Resolution
                                    </span>
                                )}
                                {!isGroup && resolutionStatusEnum === ResolutionStatus.RECONCILED && (
                                    <span className="ml-2 text-xs bg-green-100 text-green-800 px-2 py-0.5 rounded-full">
                                        Resolved
                                    </span>
                                )}
                                {!isGroup && resolutionStatusEnum === ResolutionStatus.ACKNOWLEDGED && (
                                    <span className="ml-2 text-xs bg-orange-100 text-orange-800 px-2 py-0.5 rounded-full">
                                        Acknowledged
                                    </span>
                                )}
                            </Disclosure.Button>

                            <Transition
                                show={open}
                                enter="transition duration-100 ease-out"
                                enterFrom="transform scale-95 opacity-0"
                                enterTo="transform scale-100 opacity-100"
                                leave="transition duration-75 ease-out"
                                leaveFrom="transform scale-100 opacity-100"
                                leaveTo="transform scale-95 opacity-0"
                            >
                                <Disclosure.Panel className="pl-8 py-3">
                                    {/* Special rendering for correlation groups */}
                                    {isGroup ? (
                                        <div className="space-y-4">
                                            <div className="space-y-2">
                                                {children.map(child => renderNode(child, 1))}
                                            </div>
                                        </div>
                                    ) : (
                                        <>
                                            {/* Standard log details */}
                                            <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mb-3">
                                                <div>
                                                    <p className="text-sm text-gray-500">Log ID:</p>
                                                    <p className="text-sm font-mono">{log.id}</p>
                                                </div>
                                                <div>
                                                    <p className="text-sm text-gray-500">Timestamp:</p>
                                                    <p className="text-sm">{new Date(log.createdAt).toLocaleString()}</p>
                                                </div>
                                                <div>
                                                    <p className="text-sm text-gray-500">Operation:</p>
                                                    <p className="text-sm">{log.operation || 'Unknown'}</p>
                                                </div>
                                                <div>
                                                    <p className="text-sm text-gray-500">Level:</p>
                                                    <p className={`text-sm ${getLevelClass(log.level)}`}>
                                                        {getLevelName(log.level)}
                                                    </p>
                                                </div>
                                                {log.correlationId && (
                                                    <div>
                                                        <p className="text-sm text-gray-500">Correlation ID:</p>
                                                        <p className="text-sm font-mono">{log.correlationId}</p>
                                                    </div>
                                                )}
                                            </div>

                                            {/* Enhanced Additional Data section with LogContextDisplay */}
                                            {log.context && (
                                                <div className="mb-3">
                                                    <p className="text-sm text-gray-500 mb-2">Additional Data:</p>
                                                    <LogContextDisplay context={log.context} />
                                                </div>
                                            )}

                                            {/* Resolution form */}
                                            {(log.requiresResolution || log.level >= LogLevel.WARNING) && resolutionStatusEnum !== ResolutionStatus.RECONCILED && (
                                                <div className="mt-3 p-3 bg-gray-50 rounded-md border border-gray-200">
                                                    <p className="text-sm font-medium text-gray-700 mb-2">Resolution:</p>
                                                    <textarea
                                                        rows={3}
                                                        placeholder="Explain how you resolved this issue..."
                                                        className="w-full p-2 border rounded-md focus:ring-blue-500 focus:border-blue-500"
                                                        value={resolutions[log.id] || ''}
                                                        onChange={e => handleResolveChange(log.id, e.target.value)}
                                                        disabled={isSubmitting}
                                                    />
                                                    <div className="mt-2 flex justify-end">
                                                        <button
                                                            onClick={() => submitResolution(log.id)}
                                                            disabled={isSubmitting || !resolutions[log.id]?.trim()}
                                                            className="bg-blue-600 text-white px-4 py-2 rounded-md hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:ring-offset-2 disabled:opacity-50 disabled:cursor-not-allowed flex items-center"
                                                        >
                                                            {isSubmitting ? (
                                                                <>
                                                                    <svg className="animate-spin -ml-1 mr-2 h-4 w-4 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                                                                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                                                                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                                                                    </svg>
                                                                    Submitting...
                                                                </>
                                                            ) : "Submit Resolution"}
                                                        </button>
                                                    </div>
                                                </div>
                                            )}

                                            {/* Show resolution if resolved */}
                                            {resolutionStatusEnum === ResolutionStatus.RECONCILED && log.resolutionComment && (
                                                <div className="mt-3 p-3 bg-green-50 rounded-md border border-green-200">
                                                    <p className="text-sm font-medium text-gray-700 mb-2">Resolution:</p>
                                                    <p className="text-sm text-gray-800">{log.resolutionComment}</p>
                                                    <p className="text-xs text-gray-500 mt-2">
                                                        Resolved by: {log.resolvedBy || 'Unknown'} at {log.resolvedAt ? new Date(log.resolvedAt).toLocaleString() : 'Unknown'}
                                                    </p>
                                                </div>
                                            )}

                                            {/* Child nodes */}
                                            {children.length > 0 && (
                                                <div className="mt-4 border-l-2 border-gray-200 pl-4">
                                                    <p className="text-sm text-gray-500 mb-2">Related Logs ({children.length}):</p>
                                                    {children.map(child => renderNode(child, depth + 1))}
                                                </div>
                                            )}
                                        </>
                                    )}
                                </Disclosure.Panel>
                            </Transition>
                        </>
                    )}
                </Disclosure>
            </div>
        );
    };

    // Loading state
    if (loading) {
        return (
            <div className="flex items-center justify-center h-64">
                <div className="animate-spin h-10 w-10 border-4 border-blue-600 border-t-transparent rounded-full"></div>
                <p className="ml-3 text-gray-600">Loading logs...</p>
            </div>
        );
    }

    return (
        <div className="p-6 bg-white rounded-lg shadow-lg max-w-6xl mx-auto mb-10">
            <div className="flex flex-col md:flex-row justify-between items-start md:items-center gap-4 mb-6">
                <div>
                    <h2 className="text-2xl font-bold text-gray-800">System Log Explorer</h2>
                    {lastRefreshTime && (
                        <p className="text-sm text-gray-500">
                            Last updated: {lastRefreshTime.toLocaleTimeString()}
                        </p>
                    )}
                </div>

                <div className="flex flex-wrap gap-2">
                    <div className="flex items-center mr-4">
                        <input
                            type="checkbox"
                            id="auto-refresh"
                            checked={autoRefresh}
                            onChange={(e) => setAutoRefresh(e.target.checked)}
                            className="mr-2 h-4 w-4 text-blue-600 rounded border-gray-300 focus:ring-blue-500"
                        />
                        <label htmlFor="auto-refresh" className="text-sm text-gray-700">
                            Auto-refresh (30s)
                        </label>
                    </div>

                    <div className="flex items-center mr-4">
                        <input
                            type="checkbox"
                            id="group-by-correlation"
                            checked={groupByCorrelation}
                            onChange={(e) => setGroupByCorrelation(e.target.checked)}
                            className="mr-2 h-4 w-4 text-indigo-600 rounded border-gray-300 focus:ring-indigo-500"
                        />
                        <label htmlFor="group-by-correlation" className="text-sm text-gray-700">
                            Group by Correlation ID
                        </label>
                    </div>

                    <button
                        onClick={fetchTree}
                        className="flex items-center px-4 py-2 bg-blue-100 text-blue-700 rounded-md hover:bg-blue-200 transition-colors"
                    >
                        <svg className="w-4 h-4 mr-2" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
                        </svg>
                        Refresh
                    </button>

                    <div className="relative" id="expand-dropdown">
                        <button
                            className="flex items-center px-4 py-2 bg-gray-100 text-gray-700 rounded-md hover:bg-gray-200 transition-colors"
                            onClick={() => setDropdownOpen(!dropdownOpen)}
                        >
                            <span className="mr-1">Expand</span>
                            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M19 9l-7 7-7-7" />
                            </svg>
                        </button>
                        {dropdownOpen && (
                            <div className="absolute mt-1 bg-white shadow-lg rounded-md border border-gray-200 py-1 z-10 right-0 w-40">
                                <button
                                    onClick={() => expandCollapseAll(true)}
                                    className="w-full text-left px-4 py-2 hover:bg-gray-100 text-sm"
                                >
                                    Expand All
                                </button>
                                <button
                                    onClick={() => expandCollapseAll(false)}
                                    className="w-full text-left px-4 py-2 hover:bg-gray-100 text-sm"
                                >
                                    Collapse All
                                </button>
                            </div>
                        )}
                    </div>
                </div>
            </div>

            {/* Filter controls */}
            <div className="flex flex-col md:flex-row gap-4 mb-6">
                <div className="flex-1">
                    <label htmlFor="search" className="block text-sm font-medium text-gray-700 mb-1">
                        Search Logs
                    </label>
                    <div className="relative">
                        <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
                            <svg className="h-5 w-5 text-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                            </svg>
                        </div>
                        <input
                            type="text"
                            id="search"
                            className="pl-10 focus:ring-blue-500 focus:border-blue-500 block w-full shadow-sm sm:text-sm border-gray-300 rounded-md"
                            placeholder="Search by message content..."
                            value={searchTerm}
                            onChange={e => setSearchTerm(e.target.value)}
                        />
                    </div>
                </div>

                <div className="md:w-64">
                    <label htmlFor="level-filter" className="block text-sm font-medium text-gray-700 mb-1">
                        Filter by Level
                    </label>
                    <select
                        id="level-filter"
                        className="mt-1 block w-full pl-3 pr-10 py-2 text-base border-gray-300 focus:outline-none focus:ring-blue-500 focus:border-blue-500 sm:text-sm rounded-md"
                        value={filterLevel === null ? '' : filterLevel}
                        onChange={e => setFilterLevel(e.target.value === '' ? null : Number(e.target.value))}
                    >
                        <option value="">All Levels</option>
                        <option value={LogLevel.TRACE}>Trace</option>
                        <option value={LogLevel.DEBUG}>Debug</option>
                        <option value={LogLevel.INFORMATION}>Information</option>
                        <option value={LogLevel.WARNING}>Warning</option>
                        <option value={LogLevel.ERROR}>Error</option>
                        <option value={LogLevel.CRITICAL}>Critical</option>
                    </select>
                </div>
            </div>

            {/* Error message */}
            {error && (
                <div className="bg-red-50 border-l-4 border-red-500 p-4 mb-6">
                    <div className="flex">
                        <div className="flex-shrink-0">
                            <XCircleIcon className="h-5 w-5 text-red-500" />
                        </div>
                        <div className="ml-3">
                            <p className="text-sm text-red-700">{error}</p>
                        </div>
                    </div>
                </div>
            )}

            {/* Log entries */}
            {filteredTreeData.length === 0 ? (
                <div className="text-center py-12 bg-gray-50 rounded-lg">
                    <svg className="mx-auto h-12 w-12 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                    </svg>
                    <h3 className="mt-2 text-sm font-medium text-gray-900">No logs found</h3>
                    <p className="mt-1 text-sm text-gray-500">
                        {searchTerm || filterLevel !== null ?
                            "No logs match your current filters." :
                            "There are no logs in the system."}
                    </p>
                    {(searchTerm || filterLevel !== null) && (
                        <div className="mt-6">
                            <button
                                type="button"
                                className="inline-flex items-center px-4 py-2 border border-transparent shadow-sm text-sm font-medium rounded-md text-white bg-blue-600 hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500"
                                onClick={() => {
                                    setSearchTerm("");
                                    setFilterLevel(null);
                                }}
                            >
                                Clear Filters
                            </button>
                        </div>
                    )}
                </div>
            ) : (
                <div className="bg-gray-50 rounded-lg p-4 overflow-hidden">
                    <div className="space-y-2">
                        {filteredTreeData.map(node => renderNode(node))}
                    </div>
                </div>
            )}

            {/* Stats summary */}
            {treeData.length > 0 && (
                <div className="mt-4 text-sm text-gray-500">
                    {groupByCorrelation ? (
                        <>Showing {filteredTreeData.length} groups from {treeData.length} logs</>
                    ) : (
                        <>Showing {filteredTreeData.length} of {treeData.length} logs</>
                    )}
                </div>
            )}

            {/* Legend */}
            <div className="mt-6 border-t pt-4">
                <h3 className="text-sm font-medium text-gray-700 mb-2">Log Level Legend:</h3>
                <div className="grid grid-cols-2 md:grid-cols-7 gap-4">
                    <div className="flex items-center">
                        <svg className="h-5 w-5 text-indigo-500 mr-2" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M13 10V3L4 14h7v7l9-11h-7z" />
                        </svg>
                        <span className="text-sm">Correlation Group</span>
                    </div>
                    <div className="flex items-center">
                        <InformationCircleIcon className="h-5 w-5 text-gray-400 mr-2" />
                        <span className="text-sm">Trace</span>
                    </div>
                    <div className="flex items-center">
                        <InformationCircleIcon className="h-5 w-5 text-blue-500 mr-2" />
                        <span className="text-sm">Debug</span>
                    </div>
                    <div className="flex items-center">
                        <InformationCircleIcon className="h-5 w-5 text-green-500 mr-2" />
                        <span className="text-sm">Information</span>
                    </div>
                    <div className="flex items-center">
                        <ExclamationCircleIcon className="h-5 w-5 text-yellow-500 mr-2" />
                        <span className="text-sm">Warning</span>
                    </div>
                    <div className="flex items-center">
                        <XCircleIcon className="h-5 w-5 text-red-500 mr-2" />
                        <span className="text-sm">Error</span>
                    </div>
                    <div className="flex items-center">
                        <CheckCircleIcon className="h-5 w-5 text-green-500 mr-2" />
                        <span className="text-sm">Resolved</span>
                    </div>
                </div>
            </div>
        </div>
    );
};

export default AdminLogsPanel;