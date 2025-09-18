import {
    BranchesOutlined,
    CaretRightOutlined,
    CheckCircleOutlined,
    ClockCircleOutlined,
    CloseCircleOutlined,
    DownloadOutlined,
    ExclamationCircleOutlined,
    EyeOutlined,
    FlagFilled,
    LoadingOutlined,
    NodeIndexOutlined,
    PauseCircleOutlined,
    PlayCircleOutlined,
    RedoOutlined,
    ReloadOutlined,
    SearchOutlined,
    SisternodeOutlined,
    StopFilled,
    StopOutlined
} from '@ant-design/icons';
import {
    Alert,
    Badge,
    Button,
    Card,
    Col,
    Collapse,
    Descriptions,
    Drawer,
    Input,
    Layout,
    Modal,
    Progress,
    Row,
    Select,
    Space,
    Statistic,
    Steps,
    Table,
    Tag,
    Timeline,
    Tooltip,
    Typography,
    message
} from 'antd';
import React, { useCallback, useEffect, useMemo, useState } from 'react';

// Import the flow service and SignalR
import { useAdminFlowSignalR } from '../../../hooks/useAdminFlowSignalR';
import type { StepStatusUpdateDto } from '../../../services/adminFlowSignalR';
import type {
    BranchDto,
    FlowDetailDto,
    FlowStatisticsDto,
    FlowStatusKey,
    FlowSummaryDto,
    StepDto,
    TriggeredFlowDataDto
} from '../../../services/flowService';
import flowService from '../../../services/flowService';

const { Header, Content } = Layout;
const { Option } = Select;
const { Text, Title } = Typography;
const { Panel } = Collapse;

// Flow Status Colors and Configuration with Ant Design equivalents
const flowStatusConfig: Record<FlowStatusKey, {
    color: string;
    icon: React.ReactElement;
    antdColor: string;
    progressStatus: 'success' | 'normal' | 'exception' | 'active' | undefined;
}> = {
    Initializing: {
        color: 'text-gray-600',
        icon: <LoadingOutlined spin />,
        antdColor: 'default',
        progressStatus: 'active'
    },
    Ready: {
        color: 'text-cyan-600',
        icon: <ClockCircleOutlined />,
        antdColor: 'cyan',
        progressStatus: 'normal'
    },
    Running: {
        color: 'text-blue-600',
        icon: <LoadingOutlined spin />,
        antdColor: 'blue',
        progressStatus: 'active'
    },
    Paused: {
        color: 'text-yellow-600',
        icon: <PauseCircleOutlined />,
        antdColor: 'orange',
        progressStatus: 'normal'
    },
    Completed: {
        color: 'text-green-600',
        icon: <CheckCircleOutlined />,
        antdColor: 'green',
        progressStatus: 'success'
    },
    Failed: {
        color: 'text-red-600',
        icon: <CloseCircleOutlined />,
        antdColor: 'red',
        progressStatus: 'exception'
    },
    Cancelled: {
        color: 'text-gray-600',
        icon: <StopFilled />,
        antdColor: 'default',
        progressStatus: 'normal'
    }
};

// Real-time update indicator
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
        <Badge
            status={pulse ? "processing" : "success"}
            text="Live Updates Active"
        />
    );
};

// Enhanced SubStep Renderer Component
const SubStepRenderer: React.FC<{
    subSteps: any[];
    onSubStepClick?: (subStep: any) => void;
    compact?: boolean;
}> = ({ subSteps = [], onSubStepClick, compact = false }) => {
    const getStatusConfig = (status: string) => {
        return flowStatusConfig[status as FlowStatusKey] || flowStatusConfig.Initializing;
    };

    if (!subSteps || subSteps.length === 0) {
        return <Text type="secondary" style={{ fontSize: '11px' }}>No substeps</Text>;
    }

    if (compact) {
        // Compact view for inline display
        return (
            <Space wrap size="small" style={{ marginTop: 4 }}>
                {subSteps.map((subStep, index) => {
                    const config = getStatusConfig(subStep.status);
                    return (
                        <Tooltip
                            key={index}
                            title={
                                <div>
                                    <div><strong>{subStep.name}</strong></div>
                                    <div>Status: {subStep.status}</div>
                                    {subStep.priority > 0 && <div>Priority: {subStep.priority}</div>}
                                    {subStep.resourceGroup && <div>Resource Group: {subStep.resourceGroup}</div>}
                                    {subStep.result?.message && <div>Result: {subStep.result.message}</div>}
                                </div>
                            }
                        >
                            <Tag
                                color={config.antdColor}
                                icon={config.icon}
                                style={{
                                    fontSize: '10px',
                                    cursor: onSubStepClick ? 'pointer' : 'default',
                                    margin: '1px'
                                }}
                                onClick={onSubStepClick ? () => onSubStepClick(subStep) : undefined}
                            >
                                {subStep.name}
                            </Tag>
                        </Tooltip>
                    );
                })}
            </Space>
        );
    }

    // Detailed view for expanded display
    return (
        <div style={{ marginTop: 8, marginLeft: 16 }}>
            {subSteps.map((subStep, index) => {
                const config = getStatusConfig(subStep.status);
                return (
                    <Card
                        key={index}
                        size="small"
                        style={{
                            marginBottom: 8,
                            border: '1px solid #f0f0f0',
                            cursor: onSubStepClick ? 'pointer' : 'default'
                        }}
                        onClick={onSubStepClick ? () => onSubStepClick(subStep) : undefined}
                    >
                        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
                            <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                                <NodeIndexOutlined style={{ color: '#666', fontSize: '12px' }} />
                                <Text strong style={{ fontSize: '12px' }}>{subStep.name}</Text>
                                <Tag
                                    color={config.antdColor}
                                    icon={config.icon}
                                    style={{ fontSize: '10px' }}
                                >
                                    {subStep.status}
                                </Tag>
                            </div>
                            <div style={{ display: 'flex', alignItems: 'center', gap: 8, fontSize: '11px' }}>
                                {subStep.priority > 0 && (
                                    <Tag color="blue" style={{ fontSize: '10px' }}>
                                        P{subStep.priority}
                                    </Tag>
                                )}
                                {subStep.resourceGroup && (
                                    <Tag color="green" style={{ fontSize: '10px' }}>
                                        {subStep.resourceGroup}
                                    </Tag>
                                )}
                                {subStep.index >= 0 && (
                                    <Text type="secondary" style={{ fontSize: '10px' }}>
                                        #{subStep.index}
                                    </Text>
                                )}
                            </div>
                        </div>
                        {subStep.result?.message && (
                            <div style={{ marginTop: 4, paddingTop: 4, borderTop: '1px solid #f0f0f0' }}>
                                <Text type="secondary" style={{ fontSize: '11px' }}>
                                    {subStep.result.message}
                                </Text>
                            </div>
                        )}
                    </Card>
                );
            })}
        </div>
    );
};

// FIXED: Enhanced Branch Renderer Component with Independent Context
const BranchRenderer: React.FC<{
    branches: BranchDto[];
    stepName: string; // NEW: Add stepName for unique context
    onSubStepClick?: (subStep: any) => void;
    expandedBranches?: string[];
    onBranchToggle?: (branchKey: string) => void;
}> = ({ branches = [], stepName, onSubStepClick, expandedBranches = [], onBranchToggle }) => {
    if (!branches || branches.length === 0) {
        return null;
    }

    return (
        <div style={{ marginTop: 8 }}>
            <Collapse
                ghost
                size="small"
                expandIcon={({ isActive }) => <CaretRightOutlined rotate={isActive ? 90 : 0} />}
                activeKey={expandedBranches}
                onChange={(keys) => {
                    if (onBranchToggle && Array.isArray(keys)) {
                        // Handle both expand and collapse actions properly
                        const currentKeys = new Set(expandedBranches);
                        const newKeys = new Set(keys as string[]);

                        // Find what changed
                        const added = [...newKeys].filter(key => !currentKeys.has(key));
                        const removed = [...currentKeys].filter(key => !newKeys.has(key));

                        // Process changes one by one to maintain proper state
                        [...added, ...removed].forEach(key => onBranchToggle(key));
                    }
                }}
            >
                {branches.map((branch, branchIndex) => {
                    // FIXED: Create unique branch keys with step context
                    const branchKey = `${stepName}-branch-${branchIndex}`;
                    const hasSubSteps = branch.steps && branch.steps.length > 0;

                    return (
                        <Panel
                            key={branchKey}
                            header={
                                <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                                    <BranchesOutlined style={{ color: '#1890ff', fontSize: '12px' }} />
                                    <Text style={{ fontSize: '12px' }}>
                                        Branch {branchIndex + 1} { branch.name }
                                        {branch.isDefault ? '(Default)' : branch.isConditional ? `(Conditional)` : ''}
                                    </Text>
                                    {hasSubSteps && (
                                        <Badge
                                            count={branch.steps.length}
                                            size="small"
                                            style={{ backgroundColor: '#1890ff' }}
                                        />
                                    )}
                                </div>
                            }
                            style={{ fontSize: '12px' }}
                        >
                            {hasSubSteps ? (
                                <SubStepRenderer
                                    subSteps={branch.steps}
                                    onSubStepClick={onSubStepClick}
                                />
                            ) : (
                                <Text type="secondary" style={{ fontSize: '11px' }}>
                                    No substeps in this branch
                                </Text>
                            )}
                        </Panel>
                    );
                })}
            </Collapse>
        </div>
    );
};

const FlowEngineAdminPanel: React.FC = () => {
    const [flows, setFlows] = useState<FlowSummaryDto[]>([]);
    const [loading, setLoading] = useState(false);
    const [searchTerm, setSearchTerm] = useState('');
    const [selectedFlow, setSelectedFlow] = useState<FlowDetailDto | null>(null);
    const [selectedStep, setSelectedStep] = useState<StepDto | null>(null);
    const [flowChartVisible, setFlowChartVisible] = useState(false);
    const [stepModalVisible, setStepModalVisible] = useState(false);
    const [statusFilter, setStatusFilter] = useState<string>('ALL');
    const [lastUpdateTime, setLastUpdateTime] = useState<Date | null>(null);
    const [statistics, setStatistics] = useState<FlowStatisticsDto | null>(null);
    const [connectionState, setConnectionState] = useState<'connected' | 'connecting' | 'disconnected'>('disconnected');
    const [actionLoading, setActionLoading] = useState<{ [key: string]: boolean }>({});
    const [expandedBranches, setExpandedBranches] = useState<string[]>([]);

    const [pagination, setPagination] = useState({
        current: 1,
        pageSize: 10,
        total: 0
    });

    // Enhanced SignalR handlers
    const handleFlowStatusChanged = useCallback((update: FlowDetailDto) => {
        setLastUpdateTime(new Date());

        // Update flows list - handle both existing and new flows
        setFlows(prev => {
            const existingIndex = prev.findIndex(flow => flow.flowId === update.flowId);

            if (existingIndex >= 0) {
                // Update existing flow
                const updatedFlows = [...prev];
                updatedFlows[existingIndex] = {
                    ...updatedFlows[existingIndex],
                    status: update.status,
                    currentStepName: update.currentStepName,
                    currentStepIndex: update.currentStepIndex,
                    duration: update.completedAt && update.startedAt
                        ? Math.round((new Date(update.completedAt).getTime() - new Date(update.startedAt).getTime()) / 1000)
                        : updatedFlows[existingIndex].duration
                };
                return updatedFlows;
            } else {
                // Add new flow to the list if it matches current filters
                const matchesFilter = statusFilter === 'ALL' || update.status === statusFilter;
                if (matchesFilter) {
                    const newFlowSummary: FlowSummaryDto = {
                        flowId: update.flowId,
                        flowType: update.flowType,
                        status: update.status,
                        userId: update.userId,
                        correlationId: update.correlationId,
                        createdAt: update.createdAt,
                        currentStepName: update.currentStepName,
                        currentStepIndex: update.currentStepIndex,
                        totalSteps: update.totalSteps,
                        duration: update.completedAt && update.startedAt
                            ? Math.round((new Date(update.completedAt).getTime() - new Date(update.startedAt).getTime()) / 1000)
                            : undefined
                    };

                    // Add to beginning of list (most recent first)
                    return [newFlowSummary, ...prev];
                }
                return prev;
            }
        });

        // Update selected flow if it's the same and modal is open
        if (selectedFlow && update.flowId === selectedFlow.flowId && flowChartVisible) {
            setSelectedFlow(update);

            // Show a subtle notification for live updates
            const statusChanged = selectedFlow.status !== update.status;
            if (statusChanged) {
                message.info({
                    content: `Flow status updated to ${update.status}`,
                    duration: 2,
                    style: { marginTop: '20vh' }
                });
            }
        }

        // Refresh statistics on terminal states
        if (['Completed', 'Failed', 'Cancelled'].includes(update.status)) {
            setTimeout(fetchStatistics, 1000);
        }
    }, [selectedFlow, flowChartVisible, statusFilter]);

    // NEW: Handle step status changes specifically
    const handleStepStatusChanged = useCallback((stepUpdate: StepStatusUpdateDto) => {
        setLastUpdateTime(new Date());

        // Only update if the modal is open for this flow
        if (selectedFlow && stepUpdate.flowId === selectedFlow.flowId && flowChartVisible) {
            setSelectedFlow(prev => {
                if (!prev) return prev;

                // Update the specific step in the steps array
                const updatedSteps = prev.steps.map(step =>
                    step.name === stepUpdate.stepName
                        ? {
                            ...step,
                            status: stepUpdate.stepStatus,
                            result: stepUpdate.stepResult || step.result
                        }
                        : step
                );

                // Return updated flow with new step status and current step info
                return {
                    ...prev,
                    steps: updatedSteps,
                    currentStepIndex: stepUpdate.currentStepIndex,
                    currentStepName: stepUpdate.currentStepName,
                    status: stepUpdate.flowStatus
                };
            });

            // Show step-level notification
            message.info({
                content: `Step "${stepUpdate.stepName}" status: ${stepUpdate.stepStatus}`,
                duration: 1.5,
                style: { marginTop: '20vh' }
            });

            console.log(`Step update: ${stepUpdate.stepName} -> ${stepUpdate.stepStatus}`);
        }

        // Also update the flows list for current step info
        setFlows(prev => prev.map(flow =>
            flow.flowId === stepUpdate.flowId
                ? {
                    ...flow,
                    currentStepIndex: stepUpdate.currentStepIndex,
                    currentStepName: stepUpdate.currentStepName,
                    status: stepUpdate.flowStatus
                }
                : flow
        ));
    }, [selectedFlow, flowChartVisible]);

    const handleError = useCallback((error: string) => {
        console.error('Flow admin connection error:', error);
        message.error('Flow admin connection error');
    }, []);

    // Real-time updates with both flow and step handlers
    const { isConnected: isAdminConnected, reconnect: reconnectAdmin } = useAdminFlowSignalR(
        handleFlowStatusChanged,
        handleStepStatusChanged, // NEW: Pass step handler
        handleError
    );

    // Update connection state
    useEffect(() => {
        setConnectionState(isAdminConnected ? 'connected' : 'disconnected');
    }, [isAdminConnected]);

    const filteredFlows = useMemo(() => {
        return flows.filter(flow => {
            const matchesSearch = flow.flowId.toLowerCase().includes(searchTerm.toLowerCase()) ||
                flow.flowType.toLowerCase().includes(searchTerm.toLowerCase()) ||
                flow.userId.toLowerCase().includes(searchTerm.toLowerCase());
            const matchesStatus = statusFilter === 'ALL' || flow.status === statusFilter;
            return matchesSearch && matchesStatus;
        });
    }, [flows, searchTerm, statusFilter]);

    const fetchFlows = useCallback(async () => {
        setLoading(true);
        try {
            const result = await flowService.getFlows({
                page: pagination.current,
                pageSize: pagination.pageSize,
                status: statusFilter !== 'ALL' ? statusFilter : undefined
            });

            setFlows(result.items);
            setPagination(prev => ({ ...prev, total: result.totalCount }));
            await fetchStatistics();
        } catch (error: any) {
            console.error('Failed to fetch flows:', error);
            message.error('Failed to fetch flows');
        } finally {
            setLoading(false);
        }
    }, [pagination.current, pagination.pageSize, statusFilter]);

    const fetchStatistics = async () => {
        try {
            const stats = await flowService.getStatistics();
            setStatistics(stats);
        } catch (error: any) {
            console.error('Failed to fetch statistics:', error);
            message.error('Failed to fetch statistics');
        }
    };

    useEffect(() => {
        fetchFlows();
    }, [fetchFlows]);

    const getStatusConfig = (status: string) => {
        return flowStatusConfig[status as FlowStatusKey] || flowStatusConfig.Initializing;
    };

    const openFlowChart = async (flow: FlowSummaryDto) => {
        try {
            setLoading(true);
            const flowDetails = await flowService.getFlowById(flow.flowId);
            setSelectedFlow(flowDetails);
            setFlowChartVisible(true);
        } catch (error) {
            console.error('Failed to load flow details:', error);
            message.error('Failed to load flow details');
        } finally {
            setLoading(false);
        }
    };

    const closeFlowChart = () => {
        setFlowChartVisible(false);
        setSelectedStep(null);
        setSelectedFlow(null);
        setExpandedBranches([]);
        // Clear any action loading states
        setActionLoading({});
    };

    const handleStepClick = (step: StepDto) => {
        setSelectedStep(step);
        setStepModalVisible(true);
    };

    const handleSubStepClick = (subStep: any) => {
        setSelectedStep(subStep);
        setStepModalVisible(true);
    };

    // FIXED: Enhanced branch toggle handler with proper state management
    const handleBranchToggle = (branchKey: string) => {
        setExpandedBranches(prev => {
            if (prev.includes(branchKey)) {
                // Remove the key (collapse)
                return prev.filter(key => key !== branchKey);
            } else {
                // Add the key (expand)
                return [...prev, branchKey];
            }
        });
    };

    // Enhanced flow actions that don't close the modal
    const handlePauseFlow = async (flowId: string, keepModalOpen: boolean = false) => {
        const actionKey = `pause-${flowId}`;
        setActionLoading(prev => ({ ...prev, [actionKey]: true }));

        try {
            await flowService.pauseFlow(flowId, { message: 'Manually paused by admin' });
            message.success('Flow paused successfully');

            if (!keepModalOpen) {
                // Only refresh flows if modal is not open
                fetchFlows();
            }
            // If modal is open, the live update will handle the refresh
        } catch (error: any) {
            console.error('Failed to pause flow:', error);
            message.error('Failed to pause flow');
        } finally {
            setActionLoading(prev => ({ ...prev, [actionKey]: false }));
        }
    };

    const handleResumeFlow = async (flowId: string, keepModalOpen: boolean = false) => {
        const actionKey = `resume-${flowId}`;
        setActionLoading(prev => ({ ...prev, [actionKey]: true }));

        try {
            await flowService.resumeFlow(flowId);
            message.success('Flow resumed successfully');

            if (!keepModalOpen) {
                fetchFlows();
            }
        } catch (error: any) {
            console.error('Failed to resume flow:', error);
            message.error('Failed to resume flow');
        } finally {
            setActionLoading(prev => ({ ...prev, [actionKey]: false }));
        }
    };

    const handleCancelFlow = async (flowId: string, keepModalOpen: boolean = false) => {
        const actionKey = `cancel-${flowId}`;

        Modal.confirm({
            title: 'Cancel Flow',
            content: 'Are you sure you want to cancel this flow?',
            onOk: async () => {
                setActionLoading(prev => ({ ...prev, [actionKey]: true }));
                try {
                    await flowService.cancelFlow(flowId, { reason: 'Cancelled by admin' });
                    message.success('Flow cancelled successfully');

                    if (!keepModalOpen) {
                        fetchFlows();
                    }
                } catch (error: any) {
                    console.error('Failed to cancel flow:', error);
                    message.error('Failed to cancel flow');
                } finally {
                    setActionLoading(prev => ({ ...prev, [actionKey]: false }));
                }
            }
        });
    };

    const handleRetryFlow = async (flowId: string, keepModalOpen: boolean = false) => {
        const actionKey = `retry-${flowId}`;
        setActionLoading(prev => ({ ...prev, [actionKey]: true }));

        try {
            await flowService.retryFlow(flowId);
            message.success('Flow retry initiated');

            if (!keepModalOpen) {
                fetchFlows();
            }
        } catch (error: any) {
            console.error('Failed to retry flow:', error);
            message.error('Failed to retry flow');
        } finally {
            setActionLoading(prev => ({ ...prev, [actionKey]: false }));
        }
    };

    const handleExport = async () => {
        try {
            // Create CSV content
            const headers = ['Flow ID', 'Type', 'Status', 'User', 'Created', 'Duration'];
            const rows = flows.map(flow => [
                flow.flowId,
                flow.flowType,
                flow.status,
                flow.userId,
                flow.createdAt,
                flow.duration ? `${Math.round(flow.duration)}s` : ''
            ]);

            const csvContent = [
                headers.join(','),
                ...rows.map(row => row.join(','))
            ].join('\n');

            const blob = new Blob([csvContent], { type: 'text/csv' });
            const url = window.URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = `flows-report-${new Date().toISOString()}.csv`;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            window.URL.revokeObjectURL(url);
            message.success('Export completed');
        } catch (error) {
            console.error('Failed to export:', error);
            message.error('Failed to export');
        }
    };

    const columns = [
        {
            title: 'Flow Details',
            key: 'details',
            render: (record: FlowSummaryDto) => (
                <div>
                    <Text strong>{record.flowId.substring(0, 8)}...</Text>
                    <br />
                    <Text type="secondary">Type: {record.flowType}</Text>
                    <br />
                    <Text type="secondary">
                        {new Date(record.createdAt).toLocaleString()}
                    </Text>
                </div>
            )
        },
        {
            title: 'Status',
            key: 'status',
            render: (record: FlowSummaryDto) => {
                const config = getStatusConfig(record.status);
                return (
                    <Tag color={config.antdColor} icon={config.icon}>
                        {record.status}
                    </Tag>
                );
            }
        },
        {
            title: 'Progress',
            key: 'progress',
            render: (record: FlowSummaryDto) => {
                const percent = record.totalSteps > 0
                    ? Math.round((record.currentStepIndex / record.totalSteps) * 100)
                    : 0;
                const config = getStatusConfig(record.status);

                return (
                    <div>
                        <Text>{record.currentStepIndex || 0}/{record.totalSteps || 0}</Text>
                        <Progress
                            percent={percent}
                            size="small"
                            status={config.progressStatus}
                            style={{ marginTop: 4 }}
                        />
                        {record.currentStepName && (
                            <Text type="secondary" style={{ fontSize: 12 }}>
                                Current: {record.currentStepName}
                            </Text>
                        )}
                    </div>
                );
            }
        },
        {
            title: 'User',
            dataIndex: 'userId',
            key: 'userId'
        },
        {
            title: 'Duration',
            key: 'duration',
            render: (record: FlowSummaryDto) =>
                record.duration ? `${Math.round(record.duration)}s` : '-'
        },
        {
            title: 'Actions',
            key: 'actions',
            render: (record: FlowSummaryDto) => (
                <Space size="small">
                    <Tooltip title="View Flow Chart">
                        <Button
                            type="text"
                            icon={<EyeOutlined />}
                            onClick={() => openFlowChart(record)}
                        />
                    </Tooltip>
                    {record.status === 'Running' && (
                        <Tooltip title="Pause Flow">
                            <Button
                                type="text"
                                icon={<PauseCircleOutlined />}
                                loading={actionLoading[`pause-${record.flowId}`]}
                                onClick={() => handlePauseFlow(record.flowId)}
                            />
                        </Tooltip>
                    )}
                    {record.status === 'Paused' && (
                        <Tooltip title="Resume Flow">
                            <Button
                                type="text"
                                icon={<PlayCircleOutlined />}
                                loading={actionLoading[`resume-${record.flowId}`]}
                                onClick={() => handleResumeFlow(record.flowId)}
                            />
                        </Tooltip>
                    )}
                    {record.status === 'Failed' && (
                        <Tooltip title="Retry Flow">
                            <Button
                                type="text"
                                icon={<RedoOutlined />}
                                loading={actionLoading[`retry-${record.flowId}`]}
                                onClick={() => handleRetryFlow(record.flowId)}
                            />
                        </Tooltip>
                    )}
                    {['Running', 'Paused'].includes(record.status) && (
                        <Tooltip title="Cancel Flow">
                            <Button
                                type="text"
                                danger
                                icon={<StopOutlined />}
                                loading={actionLoading[`cancel-${record.flowId}`]}
                                onClick={() => handleCancelFlow(record.flowId)}
                            />
                        </Tooltip>
                    )}
                </Space>
            )
        }
    ];

    // Add new navigation handler
    const navigateToTriggeredFlow = async (flowId: string) => {
        try {
            setLoading(true);
            const flowDetails = await flowService.getFlowById(flowId);
            setSelectedFlow(flowDetails);
            // Don't close current modal, just update content
        } catch (error) {
            console.error('Failed to load triggered flow details:', error);
            message.error('Failed to load triggered flow details');
        } finally {
            setLoading(false);
        }
    };

    // Add component for triggered flow cards
    const TriggeredFlowCard: React.FC<{
        triggeredFlow: TriggeredFlowDataDto;
        onNavigate: (flowId: string) => void;
        title: string;
    }> = ({ triggeredFlow, onNavigate, title }) => {
        const getStatusColor = (status?: string) => {
            const config = getStatusConfig(status || 'Unknown');
            return config.antdColor;
        };

        return (
            <Card
                size="small"
                title={title}
                extra={
                    triggeredFlow.flowId && (
                        <Button
                            type="link"
                            size="small"
                            icon={<EyeOutlined />}
                            onClick={() => onNavigate(triggeredFlow.flowId!)}
                        >
                            View Flow
                        </Button>
                    )
                }
                style={{ marginBottom: 8, cursor: triggeredFlow.flowId ? 'pointer' : 'default' }}
                onClick={() => triggeredFlow.flowId && onNavigate(triggeredFlow.flowId)}
            >
                <Space direction="vertical" size="small" style={{ width: '100%' }}>
                    <div>
                        <Text strong>Type:</Text> <Text code>{triggeredFlow.type}</Text>
                    </div>
                    {triggeredFlow.triggeredByStep && (
                        <div>
                            <Text strong>Triggered by Step:</Text> <Text>{triggeredFlow.triggeredByStep}</Text>
                        </div>
                    )}
                    {triggeredFlow.status && (
                        <div>
                            <Text strong>Status:</Text>{' '}
                            <Tag color={getStatusColor(triggeredFlow.status)} icon={getStatusConfig(triggeredFlow.status).icon}>
                                {triggeredFlow.status}
                            </Tag>
                        </div>
                    )}
                    {triggeredFlow.createdAt && (
                        <div>
                            <Text strong>Created:</Text>{' '}
                            <Text type="secondary">{new Date(triggeredFlow.createdAt).toLocaleString()}</Text>
                        </div>
                    )}
                    {triggeredFlow.flowId && (
                        <div>
                            <Text strong>Flow ID:</Text>{' '}
                            <Text code style={{ fontSize: '12px' }}>{triggeredFlow.flowId.substring(0, 8)}...</Text>
                        </div>
                    )}
                </Space>
            </Card>
        );
    };

    return (
        <Layout style={{ minHeight: '100vh', background: '#f5f5f5' }}>
            {/* Header */}
            <Header style={{ background: '#fff', padding: '0 24px', borderBottom: '1px solid #f0f0f0' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', height: '100%' }}>
                    <div>
                        <Title level={2} style={{ margin: 0 }}>Flow Engine</Title>
                        <Text type="secondary">Manage and monitor your automated workflows</Text>
                    </div>
                    <Space>
                        {lastUpdateTime && <UpdateIndicator lastUpdate={lastUpdateTime} />}
                        <Badge
                            status={connectionState === 'connected' ? 'success' : 'error'}
                            text={connectionState === 'connected' ? 'Connected' : 'Disconnected'}
                        />
                        {connectionState === 'disconnected' && (
                            <Button
                                type="primary"
                                icon={<ReloadOutlined />}
                                onClick={reconnectAdmin}
                            >
                                Reconnect
                            </Button>
                        )}
                    </Space>
                </div>
            </Header>

            <Content style={{ padding: '24px' }}>
                {/* Search and Filters */}
                <Card style={{ marginBottom: 24 }}>
                    <Space size="large" style={{ width: '100%' }}>
                        <Input
                            placeholder="Search flows by ID, type, or user..."
                            prefix={<SearchOutlined />}
                            value={searchTerm}
                            onChange={(e) => setSearchTerm(e.target.value)}
                            style={{ width: 300 }}
                        />
                        <Select
                            value={statusFilter}
                            onChange={setStatusFilter}
                            style={{ width: 150 }}
                        >
                            <Option value="ALL">All Status</Option>
                            {Object.keys(flowStatusConfig).map(status => (
                                <Option key={status} value={status}>{status}</Option>
                            ))}
                        </Select>
                        <Button
                            type="primary"
                            icon={<ReloadOutlined />}
                            onClick={fetchFlows}
                        >
                            Refresh
                        </Button>
                        <Button
                            icon={<DownloadOutlined />}
                            onClick={handleExport}
                        >
                            Export
                        </Button>
                    </Space>
                </Card>

                {/* Stats Cards */}
                <Row gutter={16} style={{ marginBottom: 24 }}>
                    <Col span={6}>
                        <Card>
                            <Statistic
                                title="Total Flows"
                                value={statistics?.total || flows.length}
                                prefix={<BranchesOutlined />}
                            />
                        </Card>
                    </Col>
                    <Col span={6}>
                        <Card>
                            <Statistic
                                title="Running"
                                value={statistics?.running || flows.filter(f => f.status === 'Running').length}
                                prefix={<LoadingOutlined />}
                                valueStyle={{ color: '#1890ff' }}
                            />
                        </Card>
                    </Col>
                    <Col span={6}>
                        <Card>
                            <Statistic
                                title="Completed"
                                value={statistics?.completed || flows.filter(f => f.status === 'Completed').length}
                                prefix={<CheckCircleOutlined />}
                                valueStyle={{ color: '#52c41a' }}
                            />
                        </Card>
                    </Col>
                    <Col span={6}>
                        <Card>
                            <Statistic
                                title="Failed"
                                value={statistics?.failed || flows.filter(f => f.status === 'Failed').length}
                                prefix={<ExclamationCircleOutlined />}
                                valueStyle={{ color: '#ff4d4f' }}
                            />
                        </Card>
                    </Col>
                </Row>

                {/* Flows Table */}
                <Card>
                    <Table
                        columns={columns}
                        dataSource={filteredFlows}
                        rowKey="flowId"
                        loading={loading}
                        pagination={{
                            current: pagination.current,
                            pageSize: pagination.pageSize,
                            total: pagination.total,
                            onChange: (page, pageSize) => {
                                setPagination({ current: page, pageSize, total: pagination.total });
                            }
                        }}
                    />
                </Card>
            </Content>

            {/* Enhanced Flow Chart Drawer */}
            <Drawer
                title={
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <span>{selectedFlow ? `${selectedFlow.flowType} - ${selectedFlow.flowId}` : ''}</span>
                        {selectedFlow && (
                            <Tag color={getStatusConfig(selectedFlow.status).antdColor}>
                                {selectedFlow.status}
                            </Tag>
                        )}
                        {lastUpdateTime && flowChartVisible && (
                            <Badge
                                status="processing"
                                text="Live Updates"
                                style={{ marginLeft: '8px' }}
                            />
                        )}
                    </div>
                }
                placement="bottom"
                size="large"
                onClose={closeFlowChart}
                open={flowChartVisible}
                extra={
                    <Space>
                        {selectedFlow && selectedFlow.status === 'Running' && (
                            <Button
                                type="primary"
                                danger
                                icon={<PauseCircleOutlined />}
                                loading={actionLoading[`pause-${selectedFlow.flowId}`]}
                                onClick={() => handlePauseFlow(selectedFlow.flowId, true)}
                            >
                                Pause Flow
                            </Button>
                        )}
                        {selectedFlow && selectedFlow.status === 'Paused' && (
                            <Button
                                type="primary"
                                icon={<PlayCircleOutlined />}
                                loading={actionLoading[`resume-${selectedFlow.flowId}`]}
                                onClick={() => handleResumeFlow(selectedFlow.flowId, true)}
                            >
                                Resume Flow
                            </Button>
                        )}
                        {selectedFlow && selectedFlow.status === 'Failed' && (
                            <Button
                                type="primary"
                                icon={<RedoOutlined />}
                                loading={actionLoading[`retry-${selectedFlow.flowId}`]}
                                onClick={() => handleRetryFlow(selectedFlow.flowId, true)}
                            >
                                Retry Flow
                            </Button>
                        )}
                        {selectedFlow && ['Running', 'Paused'].includes(selectedFlow.status) && (
                            <Button
                                danger
                                icon={<StopOutlined />}
                                loading={actionLoading[`cancel-${selectedFlow.flowId}`]}
                                onClick={() => handleCancelFlow(selectedFlow.flowId, true)}
                            >
                                Cancel Flow
                            </Button>
                        )}
                    </Space>
                }
            >
                {selectedFlow && (
                    <Row gutter={24}>
                        <Col span={16}>
                            {/* NEW: Triggered By Information */}
                            {selectedFlow.triggeredBy && (
                                <Card title="Triggered By" style={{ marginBottom: 16 }}>
                                    <TriggeredFlowCard
                                        triggeredFlow={selectedFlow.triggeredBy}
                                        onNavigate={navigateToTriggeredFlow}
                                        title="Parent Flow"
                                    />
                                </Card>
                            )}

                            {/* Flow Steps - FIXED WITH PROPER BRANCH RENDERING */}
                            <Card title="Flow Steps" style={{ marginBottom: 16 }}>
                                <Steps
                                    direction="vertical"
                                    current={selectedFlow.currentStepIndex}
                                    items={[
                                        {
                                            title: 'Start',
                                            description: 'Flow initialization',
                                            icon: <BranchesOutlined />,
                                            status: 'finish'
                                        },
                                        ...selectedFlow.steps.map((step) => {
                                            const config = getStatusConfig(step.status);
                                            let status: 'wait' | 'process' | 'finish' | 'error' = 'wait';

                                            if (step.status === 'Completed') status = 'finish';
                                            else if (step.status === 'Failed') status = 'error';
                                            else if (step.status === 'InProgress') status = 'process';

                                            return {
                                                title: (
                                                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}
                                                        onClick={() => handleStepClick(step)}>
                                                        <span >{step.name}</span>
                                                        {step.triggeredFlows && step.triggeredFlows.length > 0 && (
                                                            <Badge
                                                                count={step.triggeredFlows.length}
                                                                size="small"
                                                                title={`${step.triggeredFlows.length} triggered flow(s)`}
                                                            >
                                                                <SisternodeOutlined style={{ color: '#1890ff' }} />
                                                            </Badge>
                                                        )}
                                                        {step.branches && step.branches.length > 0 && (
                                                            <Badge
                                                                count={step.branches.length}
                                                                size="small"
                                                                title={`${step.branches.length} branch(es)`}
                                                            >
                                                                <BranchesOutlined style={{ color: '#1890ff' }} />
                                                            </Badge>
                                                        )}
                                                    </div>
                                                ),
                                                description: (
                                                    <div>
                                                        <div>{step.result?.message || 'Step'}</div>

                                                        {/* FIXED: Proper Branch Rendering with Step Context */}
                                                        {step.branches && step.branches.length > 0 && (
                                                            <BranchRenderer
                                                                branches={step.branches}
                                                                stepName={step.name} // Pass step name for unique context
                                                                onSubStepClick={handleSubStepClick}
                                                                expandedBranches={expandedBranches}
                                                                onBranchToggle={handleBranchToggle}
                                                            />
                                                        )}

                                                        {/* Show triggered flows inline */}
                                                        {step.triggeredFlows && step.triggeredFlows.length > 0 && (
                                                            <div style={{ marginTop: 8 }}>
                                                                <Text type="secondary" style={{ fontSize: '12px' }}>
                                                                    Triggered Flows:
                                                                </Text>
                                                                <div style={{ marginTop: 4 }}>
                                                                    {step.triggeredFlows.map((tf, index) => (
                                                                        <div onClick={tf.flowId ? () => navigateToTriggeredFlow(tf.flowId!) : undefined} style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                                                                            <NodeIndexOutlined style={{ color: '#666', fontSize: '12px' }} />
                                                                            <Text strong style={{ fontSize: '12px' }}>{tf.type}</Text>
                                                                            <Tag
                                                                                color={config.antdColor}
                                                                                icon={config.icon}
                                                                                style={{ fontSize: '10px' }}
                                                                            >
                                                                                 {tf.status && `(${tf.status})`}
                                                                            </Tag>
                                                                            {tf.flowId && <EyeOutlined style={{ marginLeft: 4 }} />}
                                                                        </div>
                                                                    ))}
                                                                </div>
                                                            </div>
                                                        )}
                                                    </div>
                                                ),
                                                icon: config.icon,
                                                status,
                                                style: { cursor: 'pointer' }
                                            };
                                        }),
                                        {
                                            title: 'End',
                                            description: 'Flow completion',
                                            icon: <FlagFilled />,
                                            status: selectedFlow.status === 'Completed' ? 'finish' : 'wait'
                                        }
                                    ]}
                                />
                            </Card>
                        </Col>
                        <Col span={8}>
                            {/* Flow Information */}
                            <Card title="Flow Information" style={{ marginBottom: 16 }}>
                                <Descriptions column={1} size="small">
                                    <Descriptions.Item label="Status">
                                        <Tag color={getStatusConfig(selectedFlow.status).antdColor}>
                                            {selectedFlow.status}
                                        </Tag>
                                    </Descriptions.Item>
                                    <Descriptions.Item label="Total Steps">
                                        {selectedFlow.totalSteps}
                                    </Descriptions.Item>
                                    <Descriptions.Item label="Current Step">
                                        {selectedFlow.currentStepIndex}/{selectedFlow.totalSteps}
                                    </Descriptions.Item>
                                    <Descriptions.Item label="User">
                                        {selectedFlow.userId}
                                    </Descriptions.Item>
                                    <Descriptions.Item label="Created">
                                        {new Date(selectedFlow.createdAt).toLocaleDateString()}
                                    </Descriptions.Item>
                                    {selectedFlow.startedAt && (
                                        <Descriptions.Item label="Started">
                                            {new Date(selectedFlow.startedAt).toLocaleString()}
                                        </Descriptions.Item>
                                    )}
                                    {selectedFlow.completedAt && (
                                        <Descriptions.Item label="Completed">
                                            {new Date(selectedFlow.completedAt).toLocaleString()}
                                        </Descriptions.Item>
                                    )}
                                    {selectedFlow.pausedAt && (
                                        <Descriptions.Item label="Paused">
                                            {new Date(selectedFlow.pausedAt).toLocaleString()}
                                        </Descriptions.Item>
                                    )}
                                    {selectedFlow.pauseMessage && (
                                        <Descriptions.Item label="Pause Reason">
                                            {selectedFlow.pauseMessage}
                                        </Descriptions.Item>
                                    )}
                                </Descriptions>
                            </Card>

                            {/* Error Information */}
                            <Card title="Error" style={{ marginBottom: 16 }}>
                                <Descriptions column={1} size="small">
                                    <Descriptions.Item label="Message">
                                        {selectedFlow.lastError}
                                    </Descriptions.Item>
                                </Descriptions>
                            </Card>

                            {/* Triggered Flows Summary */}
                            {selectedFlow.steps.some(step => step.triggeredFlows && step.triggeredFlows.length > 0) && (
                                <Card title="Triggered Flows Summary" style={{ marginBottom: 16 }}>
                                    {selectedFlow.steps
                                        .filter(step => step.triggeredFlows && step.triggeredFlows.length > 0)
                                        .map(step => (
                                            <div key={step.name} style={{ marginBottom: 12 }}>
                                                <Text strong>{step.name}:</Text>
                                                <div style={{ marginTop: 4 }}>
                                                    {step.triggeredFlows!.map((tf, index) => (
                                                        <TriggeredFlowCard
                                                            key={index}
                                                            triggeredFlow={tf}
                                                            onNavigate={navigateToTriggeredFlow}
                                                            title={`Triggered Flow ${index + 1}`}
                                                        />
                                                    ))}
                                                </div>
                                            </div>
                                        ))}
                                </Card>
                            )}

                            {/* Recent Events */}
                            {selectedFlow.events && selectedFlow.events.length > 0 && (
                                <Card title="Recent Events">
                                    <Timeline
                                        items={selectedFlow.events.slice(-5).map((event) => ({
                                            children: (
                                                <div>
                                                    <Text strong>{event.eventType}</Text>
                                                    <br />
                                                    <Text type="secondary">{event.description}</Text>
                                                    <br />
                                                    <Text type="secondary" style={{ fontSize: 12 }}>
                                                        {new Date(event.timestamp).toLocaleTimeString()}
                                                    </Text>
                                                </div>
                                            )
                                        }))}
                                    />
                                </Card>
                            )}
                        </Col>
                    </Row>
                )}
            </Drawer>

            {/* Enhanced Step Details Modal */}
            <Modal
                title={selectedStep ? `${selectedStep.name} - Step Details` : ''}
                open={stepModalVisible}
                onCancel={() => setStepModalVisible(false)}
                footer={null}
                width={800}
            >
                {selectedStep && (
                    <div>
                        <Descriptions title="Step Information" column={2} style={{ marginBottom: 16 }}>
                            <Descriptions.Item label="Status">
                                <Tag color={getStatusConfig(selectedStep.status).antdColor} icon={getStatusConfig(selectedStep.status).icon}>
                                    {selectedStep.status}
                                </Tag>
                            </Descriptions.Item>
                            <Descriptions.Item label="Critical">
                                {selectedStep.isCritical ? 'Yes' : 'No'}
                            </Descriptions.Item>
                            <Descriptions.Item label="Idempotent">
                                {selectedStep.isIdempotent ? 'Yes' : 'No'}
                            </Descriptions.Item>
                            <Descriptions.Item label="Max Retries">
                                {selectedStep.maxRetries}
                            </Descriptions.Item>
                            {(selectedStep as any).priority !== undefined && (
                                <Descriptions.Item label="Priority">
                                    {(selectedStep as any).priority}
                                </Descriptions.Item>
                            )}
                            {(selectedStep as any).resourceGroup && (
                                <Descriptions.Item label="Resource Group">
                                    {(selectedStep as any).resourceGroup}
                                </Descriptions.Item>
                            )}
                            {(selectedStep as any).index !== undefined && (selectedStep as any).index >= 0 && (
                                <Descriptions.Item label="Index">
                                    {(selectedStep as any).index}
                                </Descriptions.Item>
                            )}
                        </Descriptions>

                        {/* Step Result */}
                        {selectedStep.result && (
                            <Alert
                                message="Result"
                                description={selectedStep.result.message}
                                type={selectedStep.result.isSuccess ? 'success' : 'error'}
                                style={{ marginBottom: 16 }}
                            />
                        )}

                        {/* Step Error */}
                        {selectedStep.status == 'Failed' && (
                            <Alert
                                banner={ true }
                                message={selectedStep.error?.message ?? "Error"}
                                description={selectedStep.error?.stackTrace ?? "An unknown error occured during step execution"}
                                type={'error'}
                                style={{ marginBottom: 16 }}
                            />
                        )}

                        {/* Triggered Flows in Step Modal */}
                        {selectedStep.triggeredFlows && selectedStep.triggeredFlows.length > 0 && (
                            <div style={{ marginBottom: 16 }}>
                                <Title level={5}>Triggered Flows</Title>
                                {selectedStep.triggeredFlows.map((tf, index) => (
                                    <TriggeredFlowCard
                                        key={index}
                                        triggeredFlow={tf}
                                        onNavigate={navigateToTriggeredFlow}
                                        title={`Triggered Flow ${index + 1}`}
                                    />
                                ))}
                            </div>
                        )}

                        {/* Branches in Step Modal - FIXED */}
                        {selectedStep.branches && selectedStep.branches.length > 0 && (
                            <div style={{ marginBottom: 16 }}>
                                <Title level={5}>Branches</Title>
                                <BranchRenderer
                                    branches={selectedStep.branches}
                                    stepName={`modal-${selectedStep.name}`} // Unique context for modal
                                    onSubStepClick={handleSubStepClick}
                                    expandedBranches={expandedBranches}
                                    onBranchToggle={handleBranchToggle}
                                />
                            </div>
                        )}

                        {/* Dependencies */}
                        {Object.keys(selectedStep.dataDependencies || {}).length > 0 && (
                            <div>
                                <Title level={5}>Data Dependencies</Title>
                                <Card size="small">
                                    {Object.entries(selectedStep.dataDependencies || {}).map(([key, type]) => (
                                        <div key={key} style={{ marginBottom: 8 }}>
                                            <Text strong>{key}</Text>
                                            <Text type="secondary" style={{ marginLeft: 8 }}>({type})</Text>
                                        </div>
                                    ))}
                                </Card>
                            </div>
                        )}
                    </div>
                )}
            </Modal>
        </Layout>
    );
};

export default FlowEngineAdminPanel;