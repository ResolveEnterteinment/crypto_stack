import React, { useState, useEffect, useCallback } from 'react';
import {
    Table, Tag, Button, Modal, Form, Input, Select, Card,
    Tooltip, Alert, Space, Typography, Statistic, Row, Col,
    Badge, Descriptions, Divider, Avatar, Spin, Empty,
    message, Drawer,
} from 'antd';
import {
    ClockCircleOutlined, CheckCircleOutlined, CloseCircleOutlined,
    UserOutlined, DollarOutlined, BankOutlined, WalletOutlined,
    ExclamationCircleOutlined, ReloadOutlined,
    FilterOutlined, ExportOutlined, SearchOutlined, EyeOutlined
} from '@ant-design/icons';
import type { TablePaginationConfig, ColumnsType } from 'antd/es/table';
import { Withdrawal, WithdrawalLimits } from '../../types/withdrawal';
import * as balanceService from '../../services/balance';
import withdrawalService from '../../services/withdrawalService';
import { Balance } from '../../types/balanceTypes';

const { Option } = Select;
const { TextArea } = Input;
const { Title, Text } = Typography;

interface PaginatedResult<T> {
    page: number;
    pageSize: number;
    totalCount: number;
    totalPages: number;
    items: T[];
    hasPreviousPage: boolean;
    hasNextPage: boolean;
}

interface WithdrawalStats {
    totalPending: number;
    totalPendingAmount: number;
    totalProcessedToday: number;
    totalProcessedAmount: number;
}

interface FilterParams {
    status?: string;
    currency?: string;
    method?: string;
    dateRange?: [string, string];
    userId?: string;
}

const WithdrawalManagement: React.FC = () => {
    // State management
    const [withdrawals, setWithdrawals] = useState<Withdrawal[]>([]);
    const [loading, setLoading] = useState<boolean>(true);
    const [statsLoading, setStatsLoading] = useState<boolean>(false);
    const [stats, setStats] = useState<WithdrawalStats | null>(null);
    const [pagination, setPagination] = useState<TablePaginationConfig>({
        current: 1,
        pageSize: 20,
        total: 0,
        showSizeChanger: true,
        showQuickJumper: true,
        showTotal: (total, range) => `${range[0]}-${range[1]} of ${total} items`
    });

    // Modal states
    const [modalVisible, setModalVisible] = useState<boolean>(false);
    const [detailsDrawerVisible, setDetailsDrawerVisible] = useState<boolean>(false);
    const [isModalLoading, setModalLoading] = useState<boolean>(false);
    const [processing, setProcessing] = useState<boolean>(false);

    // Current selection states
    const [currentWithdrawal, setCurrentWithdrawal] = useState<Withdrawal | null>(null);
    const [currentBalance, setCurrentBalance] = useState<Balance | null>(null);
    const [currentPendingsTotal, setPendingsTotal] = useState<number | null>(null);
    const [withdrawalLimits, setWithdrawalLimits] = useState<WithdrawalLimits | null>(null);

    // Filter and search states
    const [filters, setFilters] = useState<FilterParams>({});
    const [searchText, setSearchText] = useState<string>('');

    // Error handling
    const [error, setError] = useState<string | null>(null);

    const [form] = Form.useForm();

    // Fetch data on component mount and pagination changes
    useEffect(() => {
        fetchWithdrawals();
        fetchStats();
    }, [pagination.current, pagination.pageSize, filters]);

    const fetchWithdrawals = useCallback(async (): Promise<void> => {
        try {
            setLoading(true);
            setError(null);

            const queryParams = new URLSearchParams({
                page: (pagination.current || 1).toString(),
                pageSize: (pagination.pageSize || 20).toString(),
                ...(filters.status && { status: filters.status }),
                ...(filters.currency && { currency: filters.currency }),
                ...(filters.method && { method: filters.method }),
                ...(searchText && { search: searchText })
            });

            const paginatedResult = await withdrawalService.getPending();

            if (Array.isArray(paginatedResult.items)) {
                setWithdrawals(paginatedResult.items);
                setPagination(prev => ({
                    ...prev,
                    total: paginatedResult.totalCount,
                }));
            } else {
                throw new Error('Unexpected response structure from server');
            }
        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : 'An unknown error occurred';
            setError(errorMessage);
            console.error('Error fetching withdrawals:', err);
            message.error('Failed to fetch withdrawals');
        } finally {
            setLoading(false);
        }
    }, [pagination.current, pagination.pageSize, filters, searchText]);

    const fetchStats = useCallback(async (): Promise<void> => {
        try {
            setStatsLoading(true);
            // This would be a new endpoint for admin stats
            // const response = await api.safeRequest('get', '/withdrawal/admin/stats');
            // For now, calculate from current data
            const pendingTotal = withdrawals.filter(w => w.status === 'PENDING').length;
            const pendingAmount = withdrawals
                .filter(w => w.status === 'PENDING')
                .reduce((sum, w) => sum + w.amount, 0);

            setStats({
                totalPending: pendingTotal,
                totalPendingAmount: pendingAmount,
                totalProcessedToday: 0, // Would come from API
                totalProcessedAmount: 0 // Would come from API
            });
        } catch (err) {
            console.error('Error fetching stats:', err);
        } finally {
            setStatsLoading(false);
        }
    }, [withdrawals]);

    const handleTableChange = (newPagination: TablePaginationConfig): void => {
        setPagination({
            ...newPagination,
            total: pagination.total
        });
    };

    const showProcessModal = async (withdrawal: Withdrawal): Promise<void> => {
        try {
            setModalVisible(true);
            setModalLoading(true);
            setCurrentWithdrawal(withdrawal);
            setError(null);

            const [balance, pendingsTotal, limits] = await Promise.all([
                balanceService.getUserBalance(withdrawal.userId, withdrawal.currency),
                withdrawalService.getUserPendingTotals(withdrawal.userId, withdrawal.currency),
                withdrawalService.getUserLimits(withdrawal.userId)
            ]);

            setCurrentBalance(balance);
            setPendingsTotal(pendingsTotal);
            setWithdrawalLimits(limits);

            form.setFieldsValue({
                comment: '',
                transactionHash: withdrawal.transactionHash || ''
            });
        } catch (e) {
            const errorMessage = e instanceof Error ? e.message : 'An unknown error occurred';
            setError(errorMessage);
            console.error('Error fetching withdrawal details:', e);
            message.error('Failed to load withdrawal details');
        } finally {
            setModalLoading(false);
        }
    };

    const showDetailsDrawer = (withdrawal: Withdrawal): void => {
        setCurrentWithdrawal(withdrawal);
        setDetailsDrawerVisible(true);
    };

    const handleApprove = async (): Promise<void> => {
        try {
            setError(null);
            const values = await form.validateFields();
            setProcessing(true);

            if (!currentWithdrawal) {
                throw new Error('No withdrawal selected');
            }

            const payload = {
                comment: values.comment || '',
                transactionHash: values.transactionHash || null
            };

            await withdrawalService.approve(currentWithdrawal.id, payload);

            message.success('Withdrawal approved successfully');

            setModalVisible(false);
            form.resetFields();

            await fetchWithdrawals();
            await fetchStats();
        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : 'An unknown error occurred';
            setError(errorMessage);
            console.error('Error approving withdrawal:', err);
            message.error('Failed to approve withdrawal');
        } finally {
            setProcessing(false);
        }
    };

    const handleReject = async (): Promise<void> => {
        try {
            setError(null);
            const values = await form.validateFields(['comment']);
            setProcessing(true);

            if (!currentWithdrawal) {
                throw new Error('No withdrawal selected');
            }

            if (!values.comment || values.comment.trim() === '') {
                throw new Error('Comment is required for rejection');
            }

            const payload = {
                comment: values.comment.trim()
            };

            await withdrawalService.reject(currentWithdrawal.id, payload);

            message.success('Withdrawal rejected successfully');

            setModalVisible(false);
            form.resetFields();

            await fetchWithdrawals();
            await fetchStats();
        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : 'An unknown error occurred';
            setError(errorMessage);
            console.error('Error rejecting withdrawal:', err);
            message.error('Failed to reject withdrawal');
        } finally {
            setProcessing(false);
        }
    };

    const getStatusTag = (status: string): React.ReactNode => {
        const statusConfig = {
            'PENDING': { icon: <ClockCircleOutlined />, color: 'processing', text: 'Pending' },
            'APPROVED': { icon: <CheckCircleOutlined />, color: 'blue', text: 'Approved' },
            'COMPLETED': { icon: <CheckCircleOutlined />, color: 'success', text: 'Completed' },
            'REJECTED': { icon: <CloseCircleOutlined />, color: 'error', text: 'Rejected' },
            'CANCELLED': { icon: <CloseCircleOutlined />, color: 'default', text: 'Cancelled' },
            'FAILED': { icon: <CloseCircleOutlined />, color: 'volcano', text: 'Failed' }
        };

        const config = statusConfig[status as keyof typeof statusConfig] || { icon: null, color: 'default', text: status };
        return <Tag icon={config.icon} color={config.color}>{config.text}</Tag>;
    };

    const getMethodIcon = (method: string): React.ReactNode => {
        switch (method) {
            case 'CRYPTO_TRANSFER': return <WalletOutlined />;
            case 'BANK_TRANSFER': return <BankOutlined />;
            case 'PAYPAL': return <DollarOutlined />;
            default: return <DollarOutlined />;
        }
    };

    const columns: ColumnsType<Withdrawal> = [
        {
            title: 'Date',
            dataIndex: 'createdAt',
            key: 'createdAt',
            width: 160,
            sorter: true,
            render: (text: string) => text ? new Date(text).toLocaleString() : '-',
        },
        {
            title: 'User',
            key: 'user',
            width: 200,
            render: (_, record) => (
                <Space>
                    <Avatar size="small" icon={<UserOutlined />} />
                    <div>
                        <div>{record.requestedBy}</div>
                        <Text type="secondary" style={{ fontSize: '12px' }}>
                            {record.userId.substring(0, 8)}...
                        </Text>
                    </div>
                </Space>
            ),
        },
        {
            title: 'Amount',
            key: 'amount',
            width: 140,
            sorter: true,
            render: (_, record) => (
                <Space direction="vertical" size={0}>
                    <Text strong>{record.amount} {record.currency}</Text>
                    <Text type="secondary" style={{ fontSize: '12px' }}>
                        {record.kycLevelAtTime || 'N/A'} Level
                    </Text>
                </Space>
            ),
        },
        {
            title: 'Method',
            dataIndex: 'withdrawalMethod',
            key: 'withdrawalMethod',
            width: 140,
            render: (method: string) => (
                <Space>
                    {getMethodIcon(method)}
                    <span>
                        {method === 'CRYPTO_TRANSFER' ? 'Crypto' :
                            method === 'BANK_TRANSFER' ? 'Bank' :
                                method === 'PAYPAL' ? 'PayPal' : method}
                    </span>
                </Space>
            ),
        },
        {
            title: 'Status',
            dataIndex: 'status',
            key: 'status',
            width: 120,
            filters: [
                { text: 'Pending', value: 'PENDING' },
                { text: 'Approved', value: 'APPROVED' },
                { text: 'Rejected', value: 'REJECTED' },
            ],
            render: (status: string) => getStatusTag(status),
        },
        {
            title: 'Actions',
            key: 'actions',
            width: 200,
            fixed: 'right',
            render: (_, record) => (
                <Space>
                    <Tooltip title="View Details">
                        <Button
                            type="text"
                            icon={<EyeOutlined />}
                            onClick={() => showDetailsDrawer(record)}
                        />
                    </Tooltip>
                    <Button
                        type="primary"
                        size="small"
                        onClick={() => showProcessModal(record)}
                        disabled={record.status !== 'PENDING'}
                    >
                        Process
                    </Button>
                </Space>
            ),
        },
    ];

    return (
        <div className="withdrawal-management">
            {/* Header with Stats */}
            <Card className="mb-4">
                <Row gutter={16} align="middle">
                    <Col flex="auto">
                        <Title level={3} style={{ margin: 0 }}>
                            Withdrawal Management
                        </Title>
                        <Text type="secondary">Manage and process user withdrawal requests</Text>
                    </Col>
                    <Col>
                        <Space>
                            <Button
                                icon={<ReloadOutlined />}
                                onClick={() => { fetchWithdrawals(); fetchStats(); }}
                                loading={loading}
                            >
                                Refresh
                            </Button>
                            <Button icon={<ExportOutlined />}>
                                Export
                            </Button>
                        </Space>
                    </Col>
                </Row>
            </Card>

            {/* Stats Cards */}
            <Row gutter={16} className="mb-4">
                <Col xs={24} sm={12} lg={6}>
                    <Card>
                        <Statistic
                            title="Pending Requests"
                            value={stats?.totalPending || 0}
                            prefix={<ClockCircleOutlined style={{ color: '#1890ff' }} />}
                            loading={statsLoading}
                        />
                    </Card>
                </Col>
                <Col xs={24} sm={12} lg={6}>
                    <Card>
                        <Statistic
                            title="Pending Amount"
                            value={stats?.totalPendingAmount || 0}
                            precision={2}
                            prefix="$"
                            loading={statsLoading}
                        />
                    </Card>
                </Col>
                <Col xs={24} sm={12} lg={6}>
                    <Card>
                        <Statistic
                            title="Processed Today"
                            value={stats?.totalProcessedToday || 0}
                            prefix={<CheckCircleOutlined style={{ color: '#52c41a' }} />}
                            loading={statsLoading}
                        />
                    </Card>
                </Col>
                <Col xs={24} sm={12} lg={6}>
                    <Card>
                        <Statistic
                            title="Processed Amount"
                            value={stats?.totalProcessedAmount || 0}
                            precision={2}
                            prefix="$"
                            loading={statsLoading}
                        />
                    </Card>
                </Col>
            </Row>

            {/* Filters */}
            <Card className="mb-4">
                <Row gutter={16} align="middle">
                    <Col xs={24} sm={8} lg={4}>
                        <Select
                            placeholder="Filter by Status"
                            allowClear
                            style={{ width: '100%' }}
                            onChange={(value) => setFilters(prev => ({ ...prev, status: value }))}
                        >
                            <Option value="PENDING">Pending</Option>
                            <Option value="APPROVED">Approved</Option>
                            <Option value="REJECTED">Rejected</Option>
                        </Select>
                    </Col>
                    <Col xs={24} sm={8} lg={4}>
                        <Select
                            placeholder="Filter by Currency"
                            allowClear
                            style={{ width: '100%' }}
                            onChange={(value) => setFilters(prev => ({ ...prev, currency: value }))}
                        >
                            <Option value="BTC">BTC</Option>
                            <Option value="ETH">ETH</Option>
                            <Option value="USDT">USDT</Option>
                            <Option value="USD">USD</Option>
                        </Select>
                    </Col>
                    <Col xs={24} sm={8} lg={4}>
                        <Select
                            placeholder="Filter by Method"
                            allowClear
                            style={{ width: '100%' }}
                            onChange={(value) => setFilters(prev => ({ ...prev, method: value }))}
                        >
                            <Option value="CRYPTO_TRANSFER">Crypto Transfer</Option>
                            <Option value="BANK_TRANSFER">Bank Transfer</Option>
                            <Option value="PAYPAL">PayPal</Option>
                        </Select>
                    </Col>
                    <Col xs={24} sm={12} lg={8}>
                        <Input
                            placeholder="Search by user ID or email"
                            prefix={<SearchOutlined />}
                            allowClear
                            value={searchText}
                            onChange={(e) => setSearchText(e.target.value)}
                            onPressEnter={fetchWithdrawals}
                        />
                    </Col>
                    <Col xs={24} sm={12} lg={4}>
                        <Button
                            type="primary"
                            icon={<FilterOutlined />}
                            onClick={fetchWithdrawals}
                            loading={loading}
                            style={{ width: '100%' }}
                        >
                            Apply Filters
                        </Button>
                    </Col>
                </Row>
            </Card>

            {/* Error Alert */}
            {error && (
                <Alert
                    message="Error"
                    description={error}
                    type="error"
                    showIcon
                    className="mb-4"
                    closable
                    onClose={() => setError(null)}
                />
            )}

            {/* Main Table */}
            <Card>
                <Table
                    columns={columns}
                    dataSource={withdrawals}
                    rowKey="id"
                    pagination={pagination}
                    loading={loading}
                    onChange={handleTableChange}
                    scroll={{ x: 1200 }}
                    size="middle"
                    locale={{
                        emptyText: <Empty description="No withdrawal requests found" />
                    }}
                />
            </Card>

            {/* Process Modal with Custom Action Buttons */}
            <Modal
                title={
                    <Space>
                        <ExclamationCircleOutlined style={{ color: '#faad14' }} />
                        Process Withdrawal Request
                    </Space>
                }
                open={modalVisible}
                onCancel={() => {
                    setModalVisible(false);
                    form.resetFields();
                }}
                footer={[
                    <Button
                        key="cancel"
                        onClick={() => {
                            setModalVisible(false);
                            form.resetFields();
                        }}
                        disabled={processing}
                    >
                        Cancel
                    </Button>,
                    <Button
                        key="reject"
                        type="default"
                        danger
                        icon={<CloseCircleOutlined />}
                        loading={processing}
                        onClick={handleReject}
                        disabled={isModalLoading}
                    >
                        Reject
                    </Button>,
                    <Button
                        key="approve"
                        type="primary"
                        icon={<CheckCircleOutlined />}
                        loading={processing}
                        onClick={handleApprove}
                        disabled={isModalLoading}
                    >
                        Approve
                    </Button>,
                ]}
                width={800}
                destroyOnClose
            >
                <Spin spinning={isModalLoading}>
                    {currentWithdrawal && (
                        <div>
                            {error && (
                                <Alert
                                    message="Error"
                                    description={error}
                                    type="error"
                                    showIcon
                                    className="mb-4"
                                    closable
                                    onClose={() => setError(null)}
                                />
                            )}

                            <Row gutter={16} className="mb-4">
                                <Col span={12}>
                                    <Card title="Withdrawal Details" size="small">
                                        <Descriptions column={1} size="small">
                                            <Descriptions.Item label="User ID">
                                                {currentWithdrawal.userId}
                                            </Descriptions.Item>
                                            <Descriptions.Item label="Requested By">
                                                {currentWithdrawal.requestedBy}
                                            </Descriptions.Item>
                                            <Descriptions.Item label="Amount">
                                                <Text strong>
                                                    {currentWithdrawal.amount} {currentWithdrawal.currency}
                                                </Text>
                                            </Descriptions.Item>
                                            <Descriptions.Item label="Available Balance">
                                                <Text type={currentBalance && currentBalance.available >= currentWithdrawal.amount ? 'success' : 'danger'}>
                                                    {currentBalance?.available || 'Loading...'} {currentWithdrawal.currency}
                                                </Text>
                                            </Descriptions.Item>
                                            <Descriptions.Item label="Pending Total">
                                                {currentPendingsTotal} {currentWithdrawal.currency}
                                            </Descriptions.Item>
                                            <Descriptions.Item label="Date">
                                                {new Date(currentWithdrawal.createdAt).toLocaleString()}
                                            </Descriptions.Item>
                                        </Descriptions>
                                    </Card>
                                </Col>
                                <Col span={12}>
                                    <Card title="Method Details" size="small">
                                        <Descriptions column={1} size="small">
                                            <Descriptions.Item label="Method">
                                                <Space>
                                                    {getMethodIcon(currentWithdrawal.withdrawalMethod)}
                                                    {currentWithdrawal.withdrawalMethod}
                                                </Space>
                                            </Descriptions.Item>
                                            <Descriptions.Item label="Address/Details">
                                                <Text code style={{ wordBreak: 'break-all' }}>
                                                    {currentWithdrawal.additionalDetails?.WithdrawalAddress}
                                                </Text>
                                            </Descriptions.Item>
                                            {currentWithdrawal.additionalDetails?.Network && (
                                                <Descriptions.Item label="Network">
                                                    {currentWithdrawal.additionalDetails.Network}
                                                </Descriptions.Item>
                                            )}
                                            {currentWithdrawal.additionalDetails?.Memo && (
                                                <Descriptions.Item label="Memo">
                                                    {currentWithdrawal.additionalDetails.Memo}
                                                </Descriptions.Item>
                                            )}
                                            <Descriptions.Item label="KYC Level">
                                                <Badge
                                                    status="processing"
                                                    text={currentWithdrawal.kycLevelAtTime || 'N/A'}
                                                />
                                            </Descriptions.Item>
                                        </Descriptions>
                                    </Card>
                                </Col>
                            </Row>

                            {withdrawalLimits && (
                                <Card title="Withdrawal Limits" size="small" className="mb-4">
                                    <Row gutter={16}>
                                        <Col span={12}>
                                            <Descriptions column={1} size="small">
                                                <Descriptions.Item label="Daily Limit">
                                                    ${withdrawalLimits.dailyLimit}
                                                </Descriptions.Item>
                                                <Descriptions.Item label="Daily Used">
                                                    <Text type={withdrawalLimits.dailyUsed > withdrawalLimits.dailyLimit * 0.8 ? 'warning' : undefined}>
                                                        ${withdrawalLimits.dailyUsed}
                                                    </Text>
                                                </Descriptions.Item>
                                                <Descriptions.Item label="Daily Remaining">
                                                    <Text type={withdrawalLimits.dailyRemaining < withdrawalLimits.dailyLimit * 0.2 ? 'danger' : 'success'}>
                                                        ${withdrawalLimits.dailyRemaining}
                                                    </Text>
                                                </Descriptions.Item>
                                            </Descriptions>
                                        </Col>
                                        <Col span={12}>
                                            <Descriptions column={1} size="small">
                                                <Descriptions.Item label="Monthly Limit">
                                                    ${withdrawalLimits.monthlyLimit}
                                                </Descriptions.Item>
                                                <Descriptions.Item label="Monthly Used">
                                                    <Text type={withdrawalLimits.monthlyUsed > withdrawalLimits.monthlyLimit * 0.8 ? 'warning' : undefined}>
                                                        ${withdrawalLimits.monthlyUsed}
                                                    </Text>
                                                </Descriptions.Item>
                                                <Descriptions.Item label="Monthly Remaining">
                                                    <Text type={withdrawalLimits.monthlyRemaining < withdrawalLimits.monthlyLimit * 0.2 ? 'danger' : 'success'}>
                                                        ${withdrawalLimits.monthlyRemaining}
                                                    </Text>
                                                </Descriptions.Item>
                                            </Descriptions>
                                        </Col>
                                    </Row>
                                </Card>
                            )}

                            <Divider />

                            <Form form={form} layout="vertical">
                                <Form.Item
                                    name="comment"
                                    label="Comment"
                                    rules={[
                                        {
                                            validator: async (_, value) => {
                                                // Only require comment for rejection, not approval
                                                return Promise.resolve();
                                            }
                                        }
                                    ]}
                                >
                                    <TextArea
                                        rows={3}
                                        placeholder="Enter reason for approval/rejection or additional notes..."
                                        showCount
                                        maxLength={500}
                                    />
                                </Form.Item>

                                <Form.Item
                                    name="transactionHash"
                                    label="Transaction Hash (Optional)"
                                    rules={[{ required: false }]}
                                >
                                    <Input
                                        placeholder="Enter transaction hash for crypto withdrawals"
                                        addonBefore="0x"
                                    />
                                </Form.Item>
                            </Form>
                        </div>
                    )}
                </Spin>
            </Modal>

            {/* Details Drawer */}
            <Drawer
                title="Withdrawal Details"
                placement="right"
                onClose={() => setDetailsDrawerVisible(false)}
                open={detailsDrawerVisible}
                width={500}
            >
                {currentWithdrawal && (
                    <div>
                        <Descriptions title="Basic Information" column={1} size="small">
                            <Descriptions.Item label="ID">
                                <Text code>{currentWithdrawal.id}</Text>
                            </Descriptions.Item>
                            <Descriptions.Item label="Status">
                                {getStatusTag(currentWithdrawal.status)}
                            </Descriptions.Item>
                            <Descriptions.Item label="Amount">
                                <Text strong>
                                    {currentWithdrawal.amount} {currentWithdrawal.currency}
                                </Text>
                            </Descriptions.Item>
                            <Descriptions.Item label="Method">
                                <Space>
                                    {getMethodIcon(currentWithdrawal.withdrawalMethod)}
                                    {currentWithdrawal.withdrawalMethod}
                                </Space>
                            </Descriptions.Item>
                            <Descriptions.Item label="Address">
                                <Text code style={{ wordBreak: 'break-all' }}>
                                    {currentWithdrawal.additionalDetails?.WithdrawalAddress}
                                </Text>
                            </Descriptions.Item>
                        </Descriptions>

                        <Divider />

                        <Descriptions title="Timeline" column={1} size="small">
                            <Descriptions.Item label="Created">
                                {new Date(currentWithdrawal.createdAt).toLocaleString()}
                            </Descriptions.Item>
                            {currentWithdrawal.processedAt && (
                                <Descriptions.Item label="Processed">
                                    {new Date(currentWithdrawal.processedAt).toLocaleString()}
                                </Descriptions.Item>
                            )}
                        </Descriptions>

                        {currentWithdrawal.comments && (
                            <>
                                <Divider />
                                <div>
                                    <Text strong>Comments:</Text>
                                    <div style={{ marginTop: 8, padding: 12, backgroundColor: '#f5f5f5', borderRadius: 4 }}>
                                        {currentWithdrawal.comments}
                                    </div>
                                </div>
                            </>
                        )}

                        {currentWithdrawal.transactionHash && (
                            <>
                                <Divider />
                                <div>
                                    <Text strong>Transaction Hash:</Text>
                                    <div style={{ marginTop: 8 }}>
                                        <Text code style={{ wordBreak: 'break-all' }}>
                                            {currentWithdrawal.transactionHash}
                                        </Text>
                                    </div>
                                </div>
                            </>
                        )}
                    </div>
                )}
            </Drawer>
        </div>
    );
};

export default WithdrawalManagement;