import {
    ArrowRightOutlined,
    CheckCircleOutlined,
    ClockCircleOutlined,
    CloseCircleOutlined,
    DashboardOutlined,
    DownloadOutlined,
    EyeOutlined,
    LoadingOutlined,
    NodeIndexOutlined,
    PauseCircleOutlined,
    PlayCircleOutlined,
    ReloadOutlined,
    SearchOutlined,
    StopOutlined,
    SyncOutlined
} from '@ant-design/icons';
import {
    Alert,
    Badge,
    Button,
    Card,
    Col,
    DatePicker,
    Descriptions,
    Divider,
    Input,
    message,
    Modal,
    Progress,
    Row,
    Select,
    Space,
    Statistic,
    Table,
    Tabs,
    Tag,
    Timeline,
    Tooltip,
    Typography
} from 'antd';
import { JSX, useCallback, useEffect, useState } from 'react';

// Import the flow service and SignalR
import { useAdminFlowSignalR } from '../../../hooks/useAdminFlowSignalR';

import type {
    FlowDetailDto,
    FlowStatisticsDto,
    FlowStatusKey,
    FlowSummaryDto,
    StepDto,
    SubStepDto
} from '../../../services/flowService';
import flowService from '../../../services/flowService';
import FlowStep from './FlowStep';

const { Title, Text } = Typography;
const { RangePicker } = DatePicker;
const { TabPane } = Tabs;
const { Option } = Select;

// Flow Status Colors and Labels
const flowStatusConfig: Record<FlowStatusKey, { color: string; icon: JSX.Element; label: string; bgColor: string }> = {
    Initializing: { color: 'default', icon: <LoadingOutlined />, label: 'Initializing', bgColor: 'bg-gray-100' },
    Ready: { color: 'cyan', icon: <ClockCircleOutlined />, label: 'Ready', bgColor: 'bg-cyan-100' },
    Running: { color: 'processing', icon: <SyncOutlined spin />, label: 'Running', bgColor: 'bg-blue-100' },
    Paused: { color: 'warning', icon: <PauseCircleOutlined />, label: 'Paused', bgColor: 'bg-yellow-100' },
    Completed: { color: 'success', icon: <CheckCircleOutlined />, label: 'Completed', bgColor: 'bg-green-100' },
    Failed: { color: 'error', icon: <CloseCircleOutlined />, label: 'Failed', bgColor: 'bg-red-100' },
    Cancelled: { color: 'default', icon: <StopOutlined />, label: 'Cancelled', bgColor: 'bg-gray-100' }
};

// Real-time update indicator component
const UpdateIndicator: React.FC<{ lastUpdate?: Date }> = ({ lastUpdate }) => {
    const [pulse, setPulse] = useState(false);

    useEffect(() => {
        if (lastUpdate) {
            setPulse(true);
            const timer = setTimeout(() => setPulse(false), 1000);
            return () => clearTimeout(timer);
        }
    }, [lastUpdate]);

    return (
        <div
            style={{
                display: 'inline-block',
                width: '8px',
                height: '8px',
                borderRadius: '50%',
                backgroundColor: '#52c41a',
                animation: pulse ? 'pulse 1s ease-out' : 'none',
                marginRight: '8px'
            }}
        />
    );
};

// Enhanced Flow Visualization Component with Branch Support
const FlowVisualization: React.FC<{ flow: FlowDetailDto}> = ({ flow }) => {
    // Create a map for quick step lookup
    const stepMap = new Map<string, StepDto>();
    flow.steps.forEach(step => stepMap.set(step.name, step));

    // Track which steps are rendered as part of branches
    const stepsInBranches = new Set<SubStepDto>();

    // Identify steps that are part of branches
    flow.steps.forEach(step => {
        if (step.branches) {
            step.branches.forEach(branch => {
                branch.steps?.forEach(step => {
                    stepsInBranches.add(step);
                });
            });
        }
    });

    // Group main flow steps by their dependencies to create a hierarchical view
    const getMainFlowLevels = () => {
        const levels: StepDto[][] = [];
        const processedSteps = new Set<string>();

        // Filter out steps that are only in branches
        const mainFlowSteps = flow.steps.filter(step => !stepsInBranches.has(step as SubStepDto));

        // First level - steps with no dependencies
        const firstLevel = mainFlowSteps.filter(step =>
            !step.stepDependencies || step.stepDependencies.length === 0
        );
        if (firstLevel.length > 0) {
            levels.push(firstLevel);
            firstLevel.forEach(step => processedSteps.add(step.name));
        }

        // Subsequent levels - steps that depend on previous levels
        let remainingSteps = mainFlowSteps.filter(step => !processedSteps.has(step.name));
        while (remainingSteps.length > 0) {
            const currentLevel = remainingSteps.filter(step =>
                step.stepDependencies?.every(dep => processedSteps.has(dep))
            );

            if (currentLevel.length === 0) {
                // Add remaining steps that might have circular dependencies
                levels.push(remainingSteps);
                break;
            }

            levels.push(currentLevel);
            currentLevel.forEach(step => processedSteps.add(step.name));
            remainingSteps = remainingSteps.filter(step => !processedSteps.has(step.name));
        }

        return levels;
    };

    

    const levels = getMainFlowLevels();

    return (
        <div style={{ padding: '20px', background: '#fff', borderRadius: '8px', overflowX: 'auto' }}>
            <div style={{ minWidth: '800px' }}>
                {/* Flow Header */}
                <div style={{ marginBottom: '24px' }}>
                    <Row align="middle" gutter={16}>
                        <Col>
                            <Text strong style={{ fontSize: '16px' }}>Flow Progress:</Text>
                        </Col>
                        <Col flex="auto">
                            <Progress
                                percent={Math.round((flow.currentStepIndex / flow.totalSteps) * 100)}
                                status={flow.status === 'Failed' ? 'exception' : flow.status === 'Completed' ? 'success' : 'active'}
                                strokeColor={flow.status === 'Paused' ? '#faad14' : undefined}
                            />
                        </Col>
                        <Col>
                            <Tag color={flowStatusConfig[flow.status as FlowStatusKey]?.color}>
                                {flowStatusConfig[flow.status as FlowStatusKey]?.icon}
                                <span style={{ marginLeft: '4px' }}>{flow.status}</span>
                            </Tag>
                        </Col>
                    </Row>
                </div>

                {/* Flow Visualization */}
                <div style={{ display: 'flex', flexDirection: 'column', gap: '40px' }}>
                    {levels.map((level, levelIndex) => (
                        <div key={levelIndex}>
                            {/* Level indicator */}
                            {levelIndex > 0 && (
                                <div style={{ textAlign: 'center', marginBottom: '20px' }}>
                                    <ArrowRightOutlined rotate={90} style={{ fontSize: '24px', color: '#d9d9d9' }} />
                                </div>
                            )}

                            {/* Steps in this level */}
                            <div style={{
                                display: 'flex',
                                gap: '20px',
                                justifyContent: 'center',
                                flexWrap: 'wrap'
                            }}>
                                {level.map((step, stepIndex) => {
                                    const isCurrentStep = step.name === flow.currentStepName;
                                    const hasBranches = step.branches && step.branches.length > 0;

                                    return (
                                        <div key={step.name} style={{ position: 'relative' }}>
                                            {/* Connection line to next step in same level */}
                                            {stepIndex < level.length - 1 && !hasBranches && (
                                                <div style={{
                                                    position: 'absolute',
                                                    top: '50%',
                                                    left: '100%',
                                                    width: '20px',
                                                    height: '2px',
                                                    background: '#d9d9d9',
                                                    zIndex: 0
                                                }} />
                                            )}

                                            {/* Main Step and its Branches */}
                                            <div>
                                                <FlowStep step={step} isCurrentStep={isCurrentStep} isBranchStep={false} />
                                            </div>
                                        </div>
                                    );
                                })}
                            </div>
                        </div>
                    ))}
                </div>

                {/* Flow Summary */}
                <Divider />
                <Row gutter={16} style={{ marginTop: '20px' }}>
                    <Col span={6}>
                        <Statistic
                            title="Total Steps"
                            value={flow.totalSteps}
                            prefix={<NodeIndexOutlined />}
                        />
                    </Col>
                    <Col span={6}>
                        <Statistic
                            title="Completed"
                            value={flow.steps.filter(s => s.status === 'Completed').length}
                            valueStyle={{ color: '#52c41a' }}
                            prefix={<CheckCircleOutlined />}
                        />
                    </Col>
                    <Col span={6}>
                        <Statistic
                            title="Failed"
                            value={flow.steps.filter(s => s.status === 'Failed').length}
                            valueStyle={{ color: '#ff4d4f' }}
                            prefix={<CloseCircleOutlined />}
                        />
                    </Col>
                    <Col span={6}>
                        <Statistic
                            title="Pending"
                            value={flow.steps.filter(s => s.status === 'Pending').length}
                            valueStyle={{ color: '#d9d9d9' }}
                            prefix={<ClockCircleOutlined />}
                        />
                    </Col>
                </Row>
            </div>
        </div>
    );
};

// Main Admin Panel Component
export default function FlowEngineAdminPanel() {
    const [flows, setFlows] = useState<FlowSummaryDto[]>([]);
    const [loading, setLoading] = useState(false);
    const [selectedFlow, setSelectedFlow] = useState<FlowDetailDto | null>(null);
    const [flowDetailModal, setFlowDetailModal] = useState(false);
    const [selectedRows, setSelectedRows] = useState<string[]>([]);
    const [expandedRows, setExpandedRows] = useState<Set<string>>(new Set());
    const [flowDetailsCache, setFlowDetailsCache] = useState<Map<string, FlowDetailDto>>(new Map());
    const [loadingFlowDetails, setLoadingFlowDetails] = useState<Set<string>>(new Set());

    const [lastUpdateTime, setLastUpdateTime] = useState<Date | null>(null);
    const [recentUpdates, setRecentUpdates] = useState<Map<string, Date>>(new Map());

    const [filters, setFilters] = useState<{
        status: string | null;
        userId: string;
        dateRange: any;
        flowType: string | null;
    }>({
        status: null,
        userId: '',
        dateRange: null,
        flowType: null
    });

    const [pagination, setPagination] = useState({
        current: 1,
        pageSize: 10,
        total: 0
    });

    const [statistics, setStatistics] = useState<FlowStatisticsDto | null>(null);

    // Connection state
    const [connectionState, setConnectionState] = useState<'connected' | 'connecting' | 'disconnected'>('disconnected');

    // Real-time updates for ALL flows (admin subscription)
    const { isConnected: isAdminConnected, reconnect: reconnectAdmin } = useAdminFlowSignalR(
        (update) => {
            // All the logic from handleFlowUpdate inline here
            // Visual feedback
            setLastUpdateTime(new Date());
            setRecentUpdates(prev => {
                const newMap = new Map(prev);
                newMap.set(update.flowId, new Date());
                return newMap;
            });

            setTimeout(() => {
                setRecentUpdates(prev => {
                    const newMap = new Map(prev);
                    newMap.delete(update.flowId);
                    return newMap;
                });
            }, 5000);

            // Update cache
            setFlowDetailsCache(prev => {
                if (prev.has(update.flowId)) {
                    const newCache = new Map(prev);
                    newCache.set(update.flowId, update);
                    return newCache;
                }
                return prev;
            });

            // Update selected flow
            if (selectedFlow && update.flowId === selectedFlow.flowId) {
                setSelectedFlow(update);
            }

            // Update main table
            setFlows(prev => prev.map(flow => {
                if (flow.flowId === update.flowId) {
                    return {
                        ...flow,
                        status: update.status,
                        currentStepName: update.currentStepName,
                        currentStepIndex: update.currentStepIndex,
                        pauseReason: update.pauseReason,
                        errorMessage: update.lastError,
                        duration: update.completedAt && update.startedAt
                            ? Math.round((new Date(update.completedAt).getTime() - new Date(update.startedAt).getTime()) / 1000)
                            : flow.duration,
                        startedAt: update.startedAt,
                        completedAt: update.completedAt,
                        totalSteps: update.totalSteps
                    };
                }
                return flow;
            }));

            // Refresh statistics on terminal states
            if (['Completed', 'Failed', 'Cancelled'].includes(update.status)) {
                setTimeout(fetchStatistics, 1000);
            }
        },
        (result) => {
            // Handle batch operation results
            message.success(`Batch operation completed: ${result.successCount} succeeded, ${result.failureCount} failed`);
            fetchFlows();
            // Clear cache for affected flows
            result.results?.forEach(item => flowDetailsCache.delete(item.flowId));
        },
        (error) => {
            message.error(error || 'Flow admin connection error');
        }
    );

    // Remove the useFlowSignalR hook entirely - it's not needed

    // Update connection state
    useEffect(() => {
        setConnectionState(isAdminConnected ? 'connected' : 'disconnected');
    }, [isAdminConnected]);

    // Effect to refresh statistics when flows update
    useEffect(() => {
        if (lastUpdateTime) {
            // Debounce statistics refresh
            const timer = setTimeout(() => {
                fetchStatistics();
            }, 1000);

            return () => clearTimeout(timer);
        }
    }, [lastUpdateTime]);

    // Fetch flows data using the service
    const fetchFlows = useCallback(async () => {
        setLoading(true);
        try {
            const result = await flowService.getFlows({
                page: pagination.current,
                pageSize: pagination.pageSize,
                status: filters.status || undefined,
                userId: filters.userId || undefined,
                flowType: filters.flowType || undefined,
                createdAfter: filters.dateRange?.[0]?.format ? filters.dateRange[0].toISOString() : undefined,
                createdBefore: filters.dateRange?.[1]?.format ? filters.dateRange[1].toISOString() : undefined
            });

            setFlows(result.items);
            setPagination(prev => ({ ...prev, total: result.totalCount }));

            // Fetch statistics
            await fetchStatistics();
        } catch (error: any) {
            message.error(error.message || 'Failed to fetch flows');
        } finally {
            setLoading(false);
        }
    }, [pagination.current, pagination.pageSize, filters]);

    // Fetch statistics
    const fetchStatistics = async () => {
        try {
            const stats = await flowService.getStatistics();
            setStatistics(stats);
        } catch (error: any) {
            console.error('Failed to fetch statistics:', error);
        }
    };

    useEffect(() => {
        fetchFlows();
        //const interval = setInterval(fetchFlows, 5000); // Refresh every 5 seconds
        //return () => clearInterval(interval);
    }, [fetchFlows]);

    // Toggle row expansion
    const toggleRowExpansion = async (flowId: string) => {
        const newExpanded = new Set(expandedRows);

        if (newExpanded.has(flowId)) {
            // Collapse row
            newExpanded.delete(flowId);
            setExpandedRows(newExpanded);
        } else {
            // Expand row - load full details if not cached
            const flow = await loadFlowDetails(flowId);
            if (flow) {
                newExpanded.add(flowId);
                setExpandedRows(newExpanded);
            }
        }
    };

    // Load flow details (with caching)
    const loadFlowDetails = async (flowId: string): Promise<FlowDetailDto | null> => {
        // Check cache first
        if (flowDetailsCache.has(flowId)) {
            return flowDetailsCache.get(flowId)!;
        }

        // Add to loading set
        setLoadingFlowDetails(prev => new Set(prev).add(flowId));

        try {
            const flow = await flowService.getFlowById(flowId);

            // Update cache
            const newCache = new Map(flowDetailsCache);
            newCache.set(flowId, flow);
            setFlowDetailsCache(newCache);

            return flow;
        } catch (error) {
            console.error(`Failed to load flow details for ${flowId}:`, error);
            message.error('Failed to load flow details');
            return null;
        } finally {
            // Remove from loading set
            setLoadingFlowDetails(prev => {
                const newSet = new Set(prev);
                newSet.delete(flowId);
                return newSet;
            });
        }
    };

    // Flow Actions using the service
    const handlePauseFlow = async (flowId: string) => {
        try {
            await flowService.pauseFlow(flowId, {
                message: 'Manually paused by admin'
            });
            message.success('Flow paused successfully');
            fetchFlows();
            // Clear cache to force reload
            flowDetailsCache.delete(flowId);
        } catch (error: any) {
            message.error(error.message || 'Failed to pause flow');
        }
    };

    const handleResumeFlow = async (flowId: string) => {
        try {
            await flowService.resumeFlow(flowId);
            message.success('Flow resumed successfully');
            fetchFlows();
            flowDetailsCache.delete(flowId);
        } catch (error: any) {
            message.error(error.message || 'Failed to resume flow');
        }
    };

    const handleCancelFlow = async (flowId: string) => {
        Modal.confirm({
            title: 'Cancel Flow',
            content: 'Are you sure you want to cancel this flow?',
            onOk: async () => {
                try {
                    await flowService.cancelFlow(flowId, {
                        reason: 'Cancelled by admin'
                    });
                    message.success('Flow cancelled successfully');
                    fetchFlows();
                    flowDetailsCache.delete(flowId);
                } catch (error: any) {
                    message.error(error.message || 'Failed to cancel flow');
                }
            }
        });
    };

    const handleResolveFlow = async (flowId: string) => {
        try {
            await flowService.resolveFlow(flowId, {
                resolution: 'Manually resolved by admin'
            });
            message.success('Flow resolved successfully');
            fetchFlows();
            flowDetailsCache.delete(flowId);
        } catch (error: any) {
            message.error(error.message || 'Failed to resolve flow');
        }
    };

    const handleRetryFlow = async (flowId: string) => {
        try {
            await flowService.retryFlow(flowId);
            message.success('Flow retry initiated successfully');
            fetchFlows();
            flowDetailsCache.delete(flowId);
        } catch (error: any) {
            message.error(error.message || 'Failed to retry flow');
        }
    };

    // Batch Operations using the service
    const handleBatchOperation = async (operation: 'pause' | 'resume' | 'cancel' | 'resolve') => {
        if (selectedRows.length === 0) {
            message.warning('Please select flows first');
            return;
        }

        Modal.confirm({
            title: `Batch ${operation}`,
            content: `Are you sure you want to ${operation} ${selectedRows.length} flows?`,
            onOk: async () => {
                try {
                    const result = await flowService.batchOperation(operation, {
                        flowIds: selectedRows
                    });

                    message.success(
                        `Batch ${operation} completed: ${result.successCount} succeeded, ${result.failureCount} failed`
                    );

                    setSelectedRows([]);
                    fetchFlows();
                    // Clear cache for affected flows
                    selectedRows.forEach(id => flowDetailsCache.delete(id));
                } catch (error: any) {
                    message.error(error.message || `Failed to ${operation} flows`);
                }
            }
        });
    };

    // Export functionality
    const handleExport = async () => {
        try {
            // Create CSV content
            const headers = ['Flow ID', 'Type', 'Status', 'User', 'Created', 'Started', 'Completed', 'Duration'];
            const rows = flows.map(flow => [
                flow.flowId,
                flow.flowType,
                flow.status,
                flow.userId,
                flow.createdAt,
                flow.startedAt || '',
                flow.completedAt || '',
                flow.duration || ''
            ]);

            const csvContent = [
                headers.join(','),
                ...rows.map(row => row.join(','))
            ].join('\n');

            // Create and download file
            const blob = new Blob([csvContent], { type: 'text/csv' });
            const url = window.URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = `flow-report-${new Date().toISOString()}.csv`;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            window.URL.revokeObjectURL(url);

            message.success('Export completed');
        } catch (error) {
            message.error('Failed to export data');
        }
    };

    // Table columns
    const columns = [
        {
            title: 'Flow Details',
            key: 'details',
            width: 200,
            render: (_: any, record: FlowSummaryDto) => (
                <div>
                    <div style={{ fontWeight: 500 }}>{record.flowId.substring(0, 8)}...</div>
                    <div style={{ fontSize: '12px', color: '#666' }}>Type: {record.flowType}</div>
                    <div style={{ fontSize: '12px', color: '#666' }}>{new Date(record.createdAt).toLocaleString()}</div>
                </div>
            )
        },
        {
            title: 'Status',
            dataIndex: 'status',
            key: 'status',
            width: 120,
            render: (status: string) => {
                const config = flowStatusConfig[status as FlowStatusKey];
                return config ? (
                    <Badge status={config.color === 'processing' ? 'processing' : 'default'}>
                        <Tag color={config.color} icon={config.icon}>
                            {config.label}
                        </Tag>
                    </Badge>
                ) : (
                    <Tag>{status}</Tag>
                );
            }
        },
        {
            title: 'Progress',
            key: 'progress',
            width: 200,
            render: (_: any, record: FlowSummaryDto) => {
                const progress = record.currentStepIndex && record.totalSteps
                    ? Math.round((record.currentStepIndex / record.totalSteps) * 100)
                    : 0;
                return (
                    <div>
                        <div style={{ marginBottom: '4px' }}>
                            <Text type="secondary" style={{ fontSize: '12px' }}>
                                Step {record.currentStepIndex || 0} of {record.totalSteps || 0}
                            </Text>
                        </div>
                        <Progress
                            percent={progress}
                            size="small"
                            status={record.status === 'Failed' ? 'exception' : 'active'}
                        />
                        {record.currentStepName && (
                            <div style={{ marginTop: '4px' }}>
                                <Text style={{ fontSize: '11px' }}>Current: {record.currentStepName}</Text>
                            </div>
                        )}
                    </div>
                );
            }
        },
        {
            title: 'User',
            dataIndex: 'userId',
            key: 'userId',
            width: 100
        },
        {
            title: 'Duration',
            dataIndex: 'duration',
            key: 'duration',
            width: 100,
            render: (duration: number) => duration ? `${Math.round(duration)}s` : '-'
        },
        {
            title: 'Actions',
            key: 'actions',
            fixed: 'right' as const,
            width: 250,
            render: (_: any, record: FlowSummaryDto) => {
                const isExpanded = expandedRows.has(record.flowId);
                const isLoadingDetails = loadingFlowDetails.has(record.flowId);

                return (
                    <Space size="small">
                        <Button
                            type="link"
                            icon={isLoadingDetails ? <LoadingOutlined /> : isExpanded ? <EyeOutlined /> : <EyeOutlined />}
                            onClick={() => toggleRowExpansion(record.flowId)}
                            loading={isLoadingDetails}
                        >
                            {isExpanded ? 'Hide' : 'Details'}
                        </Button>

                        {record.status === 'Running' && (
                            <Tooltip title="Pause">
                                <Button
                                    type="link"
                                    icon={<PauseCircleOutlined />}
                                    onClick={() => handlePauseFlow(record.flowId)}
                                />
                            </Tooltip>
                        )}

                        {record.status === 'Paused' && (
                            <Tooltip title="Resume">
                                <Button
                                    type="link"
                                    icon={<PlayCircleOutlined />}
                                    onClick={() => handleResumeFlow(record.flowId)}
                                />
                            </Tooltip>
                        )}

                        {['Running', 'Paused'].includes(record.status) && (
                            <Tooltip title="Cancel">
                                <Button
                                    type="link"
                                    danger
                                    icon={<StopOutlined />}
                                    onClick={() => handleCancelFlow(record.flowId)}
                                />
                            </Tooltip>
                        )}

                        {record.status === 'Failed' && (
                            <>
                                <Tooltip title="Retry">
                                    <Button
                                        type="link"
                                        icon={<ReloadOutlined />}
                                        onClick={() => handleRetryFlow(record.flowId)}
                                    />
                                </Tooltip>
                                <Tooltip title="Resolve">
                                    <Button
                                        type="link"
                                        icon={<CheckCircleOutlined />}
                                        onClick={() => handleResolveFlow(record.flowId)}
                                    />
                                </Tooltip>
                            </>
                        )}
                    </Space>
                );
            }
        }
    ];

    return (
        <div style={{ padding: '24px', background: '#f0f2f5', minHeight: '100vh' }}>
            <Row justify="space-between" align="middle" style={{ marginBottom: 24 }}>
                <Col>
                    <Title level={2} style={{ margin: 0 }}>
                        <DashboardOutlined /> FlowEngine Admin Panel
                    </Title>
                </Col>
                <Col>
                    <Space>
                        {lastUpdateTime && <UpdateIndicator lastUpdate={lastUpdateTime} />}
                        <Badge
                            status={connectionState === 'connected' ? 'success' : connectionState === 'connecting' ? 'processing' : 'error'}
                            text={connectionState === 'connected' ? 'Live Updates Active' : connectionState === 'connecting' ? 'Connecting...' : 'Disconnected'}
                        />
                        {connectionState === 'disconnected' && (
                            <Button
                                size="small"
                                type="link"
                                onClick={reconnectAdmin}
                                icon={<ReloadOutlined />}
                            >
                                Reconnect
                            </Button>
                        )}
                        {lastUpdateTime && (
                            <Text type="secondary" style={{ fontSize: '12px' }}>
                                Last update: {new Date(lastUpdateTime).toLocaleTimeString()}
                            </Text>
                        )}
                    </Space>
                </Col>
            </Row>

            {/* Statistics Cards */}
            <Row gutter={16} style={{ marginBottom: 24 }}>
                <Col span={4}>
                    <Card>
                        <Statistic
                            title="Total Flows"
                            value={statistics?.total || 0}
                            prefix={<NodeIndexOutlined />}
                        />
                    </Card>
                </Col>
                <Col span={5}>
                    <Card>
                        <Statistic
                            title="Running"
                            value={statistics?.running || 0}
                            valueStyle={{ color: '#1890ff' }}
                            prefix={<SyncOutlined spin />}
                        />
                    </Card>
                </Col>
                <Col span={5}>
                    <Card>
                        <Statistic
                            title="Completed"
                            value={statistics?.completed || 0}
                            valueStyle={{ color: '#52c41a' }}
                            prefix={<CheckCircleOutlined />}
                        />
                    </Card>
                </Col>
                <Col span={5}>
                    <Card>
                        <Statistic
                            title="Failed"
                            value={statistics?.failed || 0}
                            valueStyle={{ color: '#ff4d4f' }}
                            prefix={<CloseCircleOutlined />}
                        />
                    </Card>
                </Col>
                <Col span={5}>
                    <Card>
                        <Statistic
                            title="Paused"
                            value={statistics?.paused || 0}
                            valueStyle={{ color: '#faad14' }}
                            prefix={<PauseCircleOutlined />}
                        />
                    </Card>
                </Col>
            </Row>

            {/* Filters and Actions */}
            <Card style={{ marginBottom: 16 }}>
                <Row gutter={16} align="middle">
                    <Col span={4}>
                        <Select
                            placeholder="Filter by Status"
                            style={{ width: '100%' }}
                            allowClear
                            onChange={(value) => setFilters({ ...filters, status: value })}
                        >
                            {Object.keys(flowStatusConfig).map(status => (
                                <Option key={status} value={status}>
                                    {flowStatusConfig[status as FlowStatusKey].label}
                                </Option>
                            ))}
                        </Select>
                    </Col>
                    <Col span={4}>
                        <Input
                            placeholder="Filter by User ID"
                            prefix={<SearchOutlined />}
                            onChange={(e) => setFilters({ ...filters, userId: e.target.value })}
                        />
                    </Col>
                    <Col span={6}>
                        <RangePicker
                            style={{ width: '100%' }}
                            onChange={(dates) => setFilters({ ...filters, dateRange: dates as any })}
                        />
                    </Col>
                    <Col span={10} style={{ textAlign: 'right' }}>
                        <Space>
                            <Button
                                type="primary"
                                icon={<ReloadOutlined />}
                                onClick={fetchFlows}
                                loading={loading}
                            >
                                Refresh
                            </Button>
                            <Button
                                icon={<DownloadOutlined />}
                                onClick={handleExport}
                            >
                                Export
                            </Button>
                            <Button
                                onClick={() => handleBatchOperation('pause')}
                                disabled={selectedRows.length === 0}
                            >
                                Batch Pause
                            </Button>
                            <Button
                                onClick={() => handleBatchOperation('resume')}
                                disabled={selectedRows.length === 0}
                            >
                                Batch Resume
                            </Button>
                            <Button
                                danger
                                onClick={() => handleBatchOperation('cancel')}
                                disabled={selectedRows.length === 0}
                            >
                                Batch Cancel
                            </Button>
                        </Space>
                    </Col>
                </Row>
            </Card>

            {/* Flows Table with Expandable Rows */}
            <Card>
                <Table
                    columns={columns}
                    dataSource={flows}
                    rowKey="flowId"
                    loading={loading}
                    rowClassName={(record) => {
                        // Highlight recently updated rows
                        return recentUpdates.has(record.flowId) ? 'flow-row-updated' : '';
                    }}
                    pagination={{
                        ...pagination,
                        showSizeChanger: true,
                        showTotal: (total) => `Total ${total} flows`,
                        onChange: (page, pageSize) => {
                            setPagination({ ...pagination, current: page, pageSize: pageSize! });
                        }
                    }}
                    rowSelection={{
                        selectedRowKeys: selectedRows,
                        onChange: (keys) => setSelectedRows(keys as string[])
                    }}
                    expandable={{
                        expandedRowRender: (record) => {
                            const flowDetails = flowDetailsCache.get(record.flowId);
                            if (!flowDetails) return null;

                            return (
                                <FlowVisualization
                                    flow={flowDetails}
                                />
                            );
                        },
                        expandedRowKeys: Array.from(expandedRows),
                        onExpand: () => { }, // Handled by our custom expand button
                        expandIcon: () => null // Hide default expand icon
                    }}
                    scroll={{ x: 1000 }}
                />
            </Card>

            {/* Flow Detail Modal */}
            <Modal
                title={
                    <Space>
                        <NodeIndexOutlined />
                        Flow Details: {selectedFlow?.flowId}
                    </Space>
                }
                visible={flowDetailModal}
                onCancel={() => {
                    setFlowDetailModal(false);
                    setSelectedFlow(null);
                }}
                width={1200}
                footer={[
                    <Button key="close" onClick={() => {
                        setFlowDetailModal(false);
                        setSelectedFlow(null);
                    }}>
                        Close
                    </Button>
                ]}
            >
                {selectedFlow && (
                    <Tabs defaultActiveKey="visualization">
                        <TabPane tab="Flow Visualization" key="visualization">
                            <FlowVisualization
                                flow={selectedFlow}
                            />
                        </TabPane>

                        <TabPane tab="Flow Information" key="info">
                            <Descriptions bordered column={2}>
                                <Descriptions.Item label="Flow ID" span={2}>
                                    {selectedFlow.flowId}
                                </Descriptions.Item>
                                <Descriptions.Item label="Type">
                                    {selectedFlow.flowType}
                                </Descriptions.Item>
                                <Descriptions.Item label="Status">
                                    <Tag color={flowStatusConfig[selectedFlow.status as FlowStatusKey]?.color}>
                                        {flowStatusConfig[selectedFlow.status as FlowStatusKey]?.label || selectedFlow.status}
                                    </Tag>
                                </Descriptions.Item>
                                <Descriptions.Item label="User ID">
                                    {selectedFlow.userId}
                                </Descriptions.Item>
                                <Descriptions.Item label="Correlation ID">
                                    {selectedFlow.correlationId}
                                </Descriptions.Item>
                                <Descriptions.Item label="Current Step">
                                    {selectedFlow.currentStepName || '-'}
                                </Descriptions.Item>
                                <Descriptions.Item label="Progress">
                                    {selectedFlow.currentStepIndex}/{selectedFlow.totalSteps || 0} steps
                                </Descriptions.Item>
                                <Descriptions.Item label="Created At">
                                    {new Date(selectedFlow.createdAt).toLocaleString()}
                                </Descriptions.Item>
                                <Descriptions.Item label="Started At">
                                    {selectedFlow.startedAt ? new Date(selectedFlow.startedAt).toLocaleString() : '-'}
                                </Descriptions.Item>
                                <Descriptions.Item label="Completed At">
                                    {selectedFlow.completedAt ? new Date(selectedFlow.completedAt).toLocaleString() : '-'}
                                </Descriptions.Item>
                                {selectedFlow.pauseReason && (
                                    <Descriptions.Item label="Pause Reason" span={2}>
                                        <Alert
                                            message={selectedFlow.pauseReason}
                                            description={selectedFlow.pauseMessage}
                                            type="warning"
                                            showIcon
                                        />
                                    </Descriptions.Item>
                                )}
                                {selectedFlow.lastError && (
                                    <Descriptions.Item label="Error" span={2}>
                                        <Alert
                                            message={selectedFlow.lastError}
                                            type="error"
                                            showIcon
                                        />
                                    </Descriptions.Item>
                                )}
                            </Descriptions>
                        </TabPane>

                        <TabPane tab="Timeline" key="timeline">
                            <Timeline mode="left">
                                {selectedFlow.events?.map((event, index) => (
                                    <Timeline.Item
                                        key={index}
                                        color={event.eventType === 'FlowFailed' ? 'red' : 'blue'}
                                        label={new Date(event.timestamp).toLocaleString()}
                                    >
                                        <Text strong>{event.eventType}</Text>
                                        <br />
                                        <Text>{event.description}</Text>
                                    </Timeline.Item>
                                ))}
                            </Timeline>
                        </TabPane>
                    </Tabs>
                )}
            </Modal>
        </div>
    );
}