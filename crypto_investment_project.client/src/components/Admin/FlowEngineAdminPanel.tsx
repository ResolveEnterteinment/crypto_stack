import {
    CheckCircleOutlined,
    ClockCircleOutlined,
    CloseCircleOutlined,
    ControlOutlined, DashboardOutlined,
    EyeOutlined,
    FieldTimeOutlined,
    ForkOutlined,
    HourglassOutlined,
    LoadingOutlined,
    NodeIndexOutlined,
    PauseCircleOutlined,
    PlayCircleOutlined,
    ReloadOutlined,
    RetweetOutlined,
    RightOutlined,
    StopOutlined,
    SyncOutlined,
    WarningOutlined
} from '@ant-design/icons';
import {
    Alert,
    Badge,
    Button,
    Card,
    Col,
    DatePicker,
    Descriptions,
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
import ReactFlow, {
    Background,
    ConnectionLineType,
    Controls,
    Edge,
    Handle,
    MarkerType,
    MiniMap,
    Node,
    Position,
    useEdgesState,
    useNodesState
} from 'reactflow';
import 'reactflow/dist/style.css';

// Import the flow service and SignalR
import { useFlowSignalR } from '../../hooks/useFlowSignalR';
import type {
    FlowDetailDto,
    FlowStatisticsDto,
    FlowSummaryDto,
    StepDto
} from '../../services/flowService';
import flowService from '../../services/flowService';
import { SimpleJsonViewer } from '../../utils/SimpleJsonViewer';

const { Title, Text } = Typography;
const { RangePicker } = DatePicker;
const { TabPane } = Tabs;
const { Option } = Select;

// Define proper types for status configs
type FlowStatusKey = 'Initializing' | 'Ready' | 'Running' | 'Paused' | 'Completed' | 'Failed' | 'Cancelled';
type StepStatusKey = 'Pending' | 'InProgress' | 'Completed' | 'Failed' | 'Skipped' | 'Paused';

// Flow Status Colors and Labels
const flowStatusConfig: Record<FlowStatusKey, { color: string; icon: JSX.Element; label: string }> = {
    Initializing: { color: 'default', icon: <LoadingOutlined />, label: 'Initializing' },
    Ready: { color: 'cyan', icon: <ClockCircleOutlined />, label: 'Ready' },
    Running: { color: 'processing', icon: <SyncOutlined spin />, label: 'Running' },
    Paused: { color: 'warning', icon: <PauseCircleOutlined />, label: 'Paused' },
    Completed: { color: 'success', icon: <CheckCircleOutlined />, label: 'Completed' },
    Failed: { color: 'error', icon: <CloseCircleOutlined />, label: 'Failed' },
    Cancelled: { color: 'default', icon: <StopOutlined />, label: 'Cancelled' }
};

// Step Status Colors
const stepStatusConfig: Record<StepStatusKey, { color: string; icon: JSX.Element; label: string }> = {
    Pending: { color: '#d9d9d9', icon: <ClockCircleOutlined />, label: 'Pending' },
    InProgress: { color: '#1890ff', icon: <LoadingOutlined spin />, label: 'In Progress' },
    Completed: { color: '#52c41a', icon: <CheckCircleOutlined />, label: 'Completed' },
    Failed: { color: '#ff4d4f', icon: <CloseCircleOutlined />, label: 'Failed' },
    Skipped: { color: '#8c8c8c', icon: <RightOutlined />, label: 'Skipped' },
    Paused: { color: '#faad14', icon: <PauseCircleOutlined />, label: 'Paused' }
};

// Custom Node Component for Flow Diagram
interface CustomNodeData {
    label: string;
    status: string;
    timeout?: string;
    retryDelay?: string;
    maxRetries: number;
    isCritical: boolean;
    canRunInParallel: boolean;
    result?: any;
    onClick?: (data: CustomNodeData) => void;
}

const CustomNode = ({ data }: { data: CustomNodeData }) => {
    const statusColor = stepStatusConfig[data.status as StepStatusKey]?.color || '#d9d9d9';
    const StatusIcon = stepStatusConfig[data.status as StepStatusKey]?.icon || <ClockCircleOutlined />;

    return (
        <div
            style={{
                background: 'white',
                border: `2px solid ${statusColor}`,
                borderRadius: '8px',
                padding: '10px 15px',
                minWidth: '150px',
                boxShadow: '0 2px 8px rgba(0,0,0,0.1)',
                cursor: 'pointer',
                transition: 'all 0.3s'
            }}
            onClick={() => data.onClick?.(data)}
        >
            <Handle type="target" position={Position.Top} style={{ background: statusColor }} />

            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '8px' }}>
                <span style={{ color: statusColor, fontSize: '16px' }}>{StatusIcon}</span>
                <Text strong>{data.label}</Text>
            </div>

            <div style={{ display: 'flex', gap: '4px', flexWrap: 'wrap' }}>
                {data.timeout && (
                    <Tooltip title={`Timeout: ${data.timeout}`}>
                        <Tag icon={<FieldTimeOutlined />} color="blue" style={{ margin: 0 }}>
                            {data.timeout}
                        </Tag>
                    </Tooltip>
                )}
                {data.retryDelay && (
                    <Tooltip title={`Retry Delay: ${data.retryDelay}`}>
                        <Tag icon={<HourglassOutlined />} color="orange" style={{ margin: 0 }}>
                            {data.retryDelay}
                        </Tag>
                    </Tooltip>
                )}
                {data.maxRetries > 0 && (
                    <Tooltip title={`Max Retries: ${data.maxRetries}`}>
                        <Tag icon={<RetweetOutlined />} color="purple" style={{ margin: 0 }}>
                            {data.maxRetries}
                        </Tag>
                    </Tooltip>
                )}
                {data.isCritical && (
                    <Tooltip title="Critical Step">
                        <Tag icon={<WarningOutlined />} color="red" style={{ margin: 0 }}>
                            Critical
                        </Tag>
                    </Tooltip>
                )}
                {data.canRunInParallel && (
                    <Tooltip title="Can Run in Parallel">
                        <Tag icon={<ForkOutlined />} color="green" style={{ margin: 0 }}>
                            Parallel
                        </Tag>
                    </Tooltip>
                )}
            </div>

            {data.result && (
                <div style={{ marginTop: '8px' }}>
                    <Text type={data.result.isSuccess ? 'success' : 'danger'} style={{ fontSize: '12px' }}>
                        {data.result.message}
                    </Text>
                </div>
            )}

            <Handle type="source" position={Position.Bottom} style={{ background: statusColor }} />
        </div>
    );
};

const nodeTypes = {
    custom: CustomNode
};

// Main Admin Panel Component
export default function FlowEngineAdminPanel() {
    const [flows, setFlows] = useState<FlowSummaryDto[]>([]);
    const [loading, setLoading] = useState(false);
    const [selectedFlow, setSelectedFlow] = useState<FlowDetailDto | null>(null);
    const [selectedStep, setSelectedStep] = useState<StepDto | null>(null);
    const [flowDetailModal, setFlowDetailModal] = useState(false);
    const [stepDetailModal, setStepDetailModal] = useState(false);
    const [selectedRows, setSelectedRows] = useState<string[]>([]);
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

    // React Flow states
    const [nodes, setNodes, onNodesChange] = useNodesState([]);
    const [edges, setEdges, onEdgesChange] = useEdgesState([]);

    // Connection state - properly managed
    const [connectionState, setConnectionState] = useState<'connected' | 'connecting' | 'disconnected'>('disconnected');

    // Real-time updates hook for the selected flow
    const { isConnected } = useFlowSignalR(
        flowDetailModal && selectedFlow ? selectedFlow.flowId : null,
        (update) => {
            // Only update if the modal is open and the correct flow is selected
            if (selectedFlow && update.flowId === selectedFlow.flowId) {
                setSelectedFlow(update);
                generateFlowDiagram(update);
            }
            // Also update the main table
            setFlows(prev => prev.map(flow =>
                flow.flowId === update.flowId
                    ? { ...flow, status: update.status, currentStepName: update.currentStepName }
                    : flow
            ));
        },
        (result) => {
            // Handle batch operation results
            message.success(`Batch operation completed: ${result.successCount} succeeded, ${result.failureCount} failed`);
            fetchFlows();
        },
        (error) => {
            message.error(error || 'Flow real-time connection error');
        }
    );

    // Update connection state based on SignalR connection
    useEffect(() => {
        setConnectionState(isConnected ? 'connected' : 'disconnected');
    }, [isConnected]);

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
        const interval = setInterval(fetchFlows, 5000); // Refresh every 5 seconds
        return () => clearInterval(interval);
    }, [fetchFlows]);

    // Fetch detailed flow information
    const fetchFlowDetails = async (flowId: string) => {
        try {
            const details = await flowService.getFlowById(flowId);
            setSelectedFlow(details);
            generateFlowDiagram(details);
        } catch (error: any) {
            message.error(error.message || 'Failed to fetch flow details');
        }
    };

    // Generate flow diagram from flow data
    const generateFlowDiagram = (flow: FlowDetailDto) => {
        const newNodes: Node[] = [];
        const newEdges: Edge[] = [];
        const stepMap = new Map();

        // Create nodes
        flow.steps.forEach((step, index) => {
            const node: Node = {
                id: step.name,
                type: 'custom',
                position: {
                    x: 250 + (index % 3) * 200,
                    y: 50 + Math.floor(index / 3) * 150
                },
                data: {
                    label: step.name,
                    status: step.status,
                    timeout: step.timeout,
                    retryDelay: step.retryDelay,
                    maxRetries: step.maxRetries,
                    isCritical: step.isCritical,
                    canRunInParallel: step.canRunInParallel,
                    result: step.result,
                    onClick: () => {
                        setSelectedStep(step);
                        setStepDetailModal(true);
                    }
                }
            };
            newNodes.push(node);
            stepMap.set(step.name, node);
        });

        // Create edges for dependencies
        flow.steps.forEach(step => {
            step.stepDependencies?.forEach(dep => {
                newEdges.push({
                    id: `${dep}-${step.name}`,
                    source: dep,
                    target: step.name,
                    type: 'smoothstep',
                    animated: step.status === 'InProgress',
                    style: {
                        stroke: step.status === 'Failed' ? '#ff4d4f' : '#1890ff',
                        strokeWidth: 2
                    },
                    markerEnd: {
                        type: MarkerType.ArrowClosed,
                        color: step.status === 'Failed' ? '#ff4d4f' : '#1890ff'
                    }
                });
            });

            // Add branch edges
            step.branches?.forEach((branch, branchIndex) => {
                branch.steps?.forEach(branchStep => {
                    newEdges.push({
                        id: `${step.name}-branch-${branchIndex}-${branchStep}`,
                        source: step.name,
                        target: branchStep,
                        type: 'smoothstep',
                        label: branch.isDefault ? 'default' : branch.condition,
                        style: {
                            stroke: '#722ed1',
                            strokeDasharray: branch.isDefault ? '0' : '5 5'
                        }
                    });
                });
            });
        });

        setNodes(newNodes);
        setEdges(newEdges);
    };

    // Flow Actions using the service
    const handlePauseFlow = async (flowId: string) => {
        try {
            await flowService.pauseFlow(flowId, {
                message: 'Manually paused by admin'
            });
            message.success('Flow paused successfully');
            fetchFlows();
        } catch (error: any) {
            message.error(error.message || 'Failed to pause flow');
        }
    };

    const handleResumeFlow = async (flowId: string) => {
        try {
            await flowService.resumeFlow(flowId);
            message.success('Flow resumed successfully');
            fetchFlows();
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
        } catch (error: any) {
            message.error(error.message || 'Failed to resolve flow');
        }
    };

    const handleRetryFlow = async (flowId: string) => {
        try {
            await flowService.retryFlow(flowId);
            message.success('Flow retry initiated successfully');
            fetchFlows();
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
                } catch (error: any) {
                    message.error(error.message || `Failed to ${operation} flows`);
                }
            }
        });
    };

    // Table columns
    const columns = [
        {
            title: 'Flow ID',
            dataIndex: 'flowId',
            key: 'flowId',
            width: 120,
            render: (id: string) => (
                <Tooltip title={id}>
                    <Text copyable={{ text: id }}>
                        {id.substring(0, 8)}...
                    </Text>
                </Tooltip>
            )
        },
        {
            title: 'Type',
            dataIndex: 'flowType',
            key: 'flowType',
            width: 150,
            render: (type: string) => <Tag color="blue">{type}</Tag>
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
            title: 'Current Step',
            dataIndex: 'currentStepName',
            key: 'currentStepName',
            width: 150,
            render: (step: string) => step || '-'
        },
        {
            title: 'User',
            dataIndex: 'userId',
            key: 'userId',
            width: 100
        },
        {
            title: 'Progress',
            dataIndex: 'progress',
            key: 'progress',
            width: 150,
            render: (_: any, record: FlowSummaryDto) => {
                const progress = record.currentStepIndex && record.totalSteps
                    ? Math.round((record.currentStepIndex / record.totalSteps) * 100)
                    : 0;
                return (
                    <Progress
                        percent={progress}
                        size="small"
                        status={record.status === 'Failed' ? 'exception' : 'active'}
                    />
                );
            }
        },
        {
            title: 'Started',
            dataIndex: 'startedAt',
            key: 'startedAt',
            width: 150,
            render: (date: string) => date ? new Date(date).toLocaleString() : '-'
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
            render: (_: any, record: FlowSummaryDto) => (
                <Space size="small">
                    <Tooltip title="View Details">
                        <Button
                            type="link"
                            icon={<EyeOutlined />}
                            onClick={() => {
                                fetchFlowDetails(record.flowId);
                                setFlowDetailModal(true);
                            }}
                        />
                    </Tooltip>

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
            )
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
                    <Badge
                        status={connectionState === 'connected' ? 'success' : connectionState === 'connecting' ? 'processing' : 'error'}
                        text={connectionState === 'connected' ? 'Connected' : connectionState === 'connecting' ? 'Connecting...' : 'Disconnected'}
                    />
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
                            onChange={(e) => setFilters({ ...filters, userId: e.target.value })}
                        />
                    </Col>
                    <Col span={6}>
                        <RangePicker
                            style={{ width: '100%' }}
                            onChange={(dates) => setFilters({ ...filters, dateRange: dates as any })}
                        />
                    </Col>
                    <Col span={4}>
                        <Button
                            type="primary"
                            icon={<ReloadOutlined />}
                            onClick={fetchFlows}
                            loading={loading}
                        >
                            Refresh
                        </Button>
                    </Col>
                    <Col span={6} style={{ textAlign: 'right' }}>
                        <Space>
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

            {/* Flows Table */}
            <Card>
                <Table
                    columns={columns}
                    dataSource={flows}
                    rowKey="flowId"
                    loading={loading}
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
                    scroll={{ x: 1300 }}
                />
            </Card>

            {/* Flow Detail Modal with Diagram */}
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
                    <Tabs defaultActiveKey="diagram">
                        <TabPane tab="Flow Diagram" key="diagram">
                            <div style={{ height: 500, border: '1px solid #f0f0f0', borderRadius: 8 }}>
                                <ReactFlow
                                    nodes={nodes}
                                    edges={edges}
                                    onNodesChange={onNodesChange}
                                    onEdgesChange={onEdgesChange}
                                    nodeTypes={nodeTypes}
                                    connectionLineType={ConnectionLineType.SmoothStep}
                                    fitView
                                >
                                    <Background color="#aaa" gap={16} />
                                    <Controls />
                                    <MiniMap />
                                </ReactFlow>
                            </div>
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

            {/* Step Detail Modal */}
            <Modal
                title={
                    <Space>
                        <ControlOutlined />
                        Step Details: {selectedStep?.name}
                    </Space>
                }
                visible={stepDetailModal}
                onCancel={() => setStepDetailModal(false)}
                width={800}
                footer={[
                    <Button key="close" onClick={() => setStepDetailModal(false)}>
                        Close
                    </Button>
                ]}
            >
                {selectedStep && (
                    <Descriptions bordered column={2} size="small">
                        <Descriptions.Item label="Step Name" span={2}>
                            {selectedStep.name}
                        </Descriptions.Item>
                        <Descriptions.Item label="Status">
                            <Tag color={stepStatusConfig[selectedStep.status as StepStatusKey]?.color}>
                                {stepStatusConfig[selectedStep.status as StepStatusKey]?.label || selectedStep.status}
                            </Tag>
                        </Descriptions.Item>
                        <Descriptions.Item label="Critical">
                            {selectedStep.isCritical ?
                                <Tag color="red">Yes</Tag> :
                                <Tag color="green">No</Tag>
                            }
                        </Descriptions.Item>
                        <Descriptions.Item label="Can Run Parallel">
                            {selectedStep.canRunInParallel ? 'Yes' : 'No'}
                        </Descriptions.Item>
                        <Descriptions.Item label="Idempotent">
                            {selectedStep.isIdempotent ? 'Yes' : 'No'}
                        </Descriptions.Item>
                        <Descriptions.Item label="Max Retries">
                            {selectedStep.maxRetries || 0}
                        </Descriptions.Item>
                        <Descriptions.Item label="Retry Delay">
                            {selectedStep.retryDelay || '-'}
                        </Descriptions.Item>
                        <Descriptions.Item label="Timeout">
                            {selectedStep.timeout || '-'}
                        </Descriptions.Item>

                        {selectedStep.stepDependencies?.length > 0 && (
                            <Descriptions.Item label="Step Dependencies" span={2}>
                                <Space wrap>
                                    {selectedStep.stepDependencies.map(dep => (
                                        <Tag key={dep} color="blue">{dep}</Tag>
                                    ))}
                                </Space>
                            </Descriptions.Item>
                        )}

                        {selectedStep.dataDependencies && Object.keys(selectedStep.dataDependencies).length > 0 && (
                            <Descriptions.Item label="Data Dependencies" span={2}>
                                <Space direction="vertical" style={{ width: '100%' }}>
                                    {Object.entries(selectedStep.dataDependencies).map(([key, value]) => (
                                        <div key={key}>
                                            <Text code>{key}</Text>: <Text type="secondary">{value}</Text>
                                        </div>
                                    ))}
                                </Space>
                            </Descriptions.Item>
                        )}

                        {selectedStep.result && (
                            <>
                                <Descriptions.Item label="Result Status" span={2}>
                                    {selectedStep.result.isSuccess ?
                                        <Tag color="success">Success</Tag> :
                                        <Tag color="error">Failed</Tag>
                                    }
                                </Descriptions.Item>
                                <Descriptions.Item label="Result Message" span={2}>
                                    <Text>{selectedStep.result.message}</Text>
                                </Descriptions.Item>
                                {selectedStep.result.data && Object.keys(selectedStep.result.data).length > 0 && (
                                    <Descriptions.Item label="Result Data" span={2}>
                                        <SimpleJsonViewer data={selectedStep.result.data} collapsedLevels={2} />
                                    </Descriptions.Item>
                                )}
                            </>
                        )}

                        {(selectedStep.branches?.length ?? 0) > 0 && (
                            <Descriptions.Item label="Branches" span={2}>
                                <Space direction="vertical" style={{ width: '100%' }}>
                                    {(selectedStep.branches ?? []).map((branch, index) => (
                                        <Card key={index} size="small">
                                            <Text>Steps: {branch.steps?.join(', ') || 'None'}</Text>
                                            <br />
                                            <Text type="secondary">
                                                {branch.isDefault ? 'Default Branch' : `Condition: ${branch.condition}`}
                                            </Text>
                                        </Card>
                                    ))}
                                </Space>
                            </Descriptions.Item>
                        )}
                    </Descriptions>
                )}
            </Modal>
        </div>
    );
}