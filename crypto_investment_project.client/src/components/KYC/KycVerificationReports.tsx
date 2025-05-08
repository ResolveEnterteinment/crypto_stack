// src/components/KYC/KycVerificationReports.tsx
import React, { useState, useEffect } from 'react';
import { Card, Table, DatePicker, Button, Row, Col, Statistic, Select, message, Badge, Tag, Tooltip } from 'antd';
import {
    UserOutlined,
    CheckCircleOutlined,
    CloseCircleOutlined,
    DownloadOutlined,
    SyncOutlined,
    RiseOutlined,
    FallOutlined
} from '@ant-design/icons';
import dayjs from 'dayjs';
import type { Dayjs } from 'dayjs';

const { RangePicker } = DatePicker;
const { Option } = Select;

// Interfaces for the component
interface VerificationData {
    id: string;
    userId: string;
    email: string;
    provider: string;
    status: string;
    verificationLevel: string;
    isHighRisk: boolean;
    isPep: boolean;
    country: string;
    submittedAt: string;
    completedAt?: string;
    processingTime?: number;
}

interface VerificationStats {
    totalVerifications: number;
    approvedVerifications: number;
    rejectedVerifications: number;
    pendingVerifications: number;
    averageProcessingTime: number;
    approvalRate: number;
    highRiskRate: number;
    pepRate: number;
}

interface StatsByProvider {
    provider: string;
    totalVerifications: number;
    approvalRate: number;
    averageProcessingTime: number;
    rejectionRate: number;
}

const KycVerificationReports: React.FC = () => {
    const [dateRange, setDateRange] = useState<[Dayjs, Dayjs]>([
        dayjs().subtract(30, 'day'), // 30 days ago
        dayjs() // today
    ]);
    const [provider, setProvider] = useState<string>('all');
    const [status, setStatus] = useState<string>('all');
    const [verifications, setVerifications] = useState<VerificationData[]>([]);
    const [stats, setStats] = useState<VerificationStats | null>(null);
    const [providerStats, setProviderStats] = useState<StatsByProvider[]>([]);
    const [loading, setLoading] = useState<boolean>(true);
    const [exportLoading, setExportLoading] = useState<boolean>(false);
    const [pagination, setPagination] = useState({ current: 1, pageSize: 10, total: 0 });

    // Load data on component mount and when filters change
    useEffect(() => {
        fetchVerificationData();
    }, [dateRange, provider, status, pagination.current]);

    // Mock function to fetch verification data
    const fetchVerificationData = async () => {
        setLoading(true);
        try {
            // In a real application, this would be a call to your backend API
            setTimeout(() => {
                // Get date range as native Date objects for processing
                const startDate = dateRange[0].toDate();
                const endDate = dateRange[1].toDate();

                // Generate mock verification data
                const mockData: VerificationData[] = Array.from({ length: 50 }, (_, i) => {
                    const isApproved = Math.random() > 0.2;
                    const isRejected = !isApproved && Math.random() > 0.5;
                    const isPending = !isApproved && !isRejected;

                    const submittedDate = new Date(startDate.getTime() + Math.random() * (endDate.getTime() - startDate.getTime()));
                    let completedDate = null;
                    let processingTime = undefined;

                    if (!isPending) {
                        // Add 1-48 hours to submitted date for completed date
                        completedDate = new Date(submittedDate.getTime() + Math.random() * 48 * 60 * 60 * 1000);
                        processingTime = (completedDate.getTime() - submittedDate.getTime()) / (60 * 60 * 1000); // hours
                    }

                    const providerChoice = Math.random() > 0.6 ? 'Onfido' : 'SumSub';

                    return {
                        id: `ver-${i + 1000}`,
                        userId: `user-${i + 1000}`,
                        email: `user${i}@example.com`,
                        provider: providerChoice,
                        status: isApproved ? 'APPROVED' : (isRejected ? 'REJECTED' : 'PENDING_VERIFICATION'),
                        verificationLevel: Math.random() > 0.7 ? 'ENHANCED' : (Math.random() > 0.4 ? 'STANDARD' : 'BASIC'),
                        isHighRisk: Math.random() > 0.9,
                        isPep: Math.random() > 0.95,
                        country: ['US', 'UK', 'CA', 'AU', 'DE', 'FR', 'JP', 'BR'][Math.floor(Math.random() * 8)],
                        submittedAt: submittedDate.toISOString(),
                        completedAt: completedDate ? completedDate.toISOString() : undefined,
                        processingTime
                    };
                });

                // Filter data based on selected filters
                let filteredData = [...mockData];

                if (provider !== 'all') {
                    filteredData = filteredData.filter(item => item.provider === provider);
                }

                if (status !== 'all') {
                    filteredData = filteredData.filter(item => item.status === status);
                }

                // Sort by submitted date descending
                filteredData.sort((a, b) => new Date(b.submittedAt).getTime() - new Date(a.submittedAt).getTime());

                // Calculate statistics
                const totalVerifications = filteredData.length;
                const approvedVerifications = filteredData.filter(item => item.status === 'APPROVED').length;
                const rejectedVerifications = filteredData.filter(item => item.status === 'REJECTED').length;
                const pendingVerifications = filteredData.filter(item => item.status === 'PENDING_VERIFICATION').length;

                const completedVerifications = filteredData.filter(item => item.processingTime !== undefined);
                const totalProcessingTime = completedVerifications.reduce((sum, item) => sum + (item.processingTime || 0), 0);
                const averageProcessingTime = completedVerifications.length > 0
                    ? totalProcessingTime / completedVerifications.length
                    : 0;

                const approvalRate = totalVerifications > 0
                    ? (approvedVerifications / totalVerifications) * 100
                    : 0;

                const highRiskCount = filteredData.filter(item => item.isHighRisk).length;
                const highRiskRate = totalVerifications > 0
                    ? (highRiskCount / totalVerifications) * 100
                    : 0;

                const pepCount = filteredData.filter(item => item.isPep).length;
                const pepRate = totalVerifications > 0
                    ? (pepCount / totalVerifications) * 100
                    : 0;

                // Calculate statistics by provider
                const providers = ['Onfido', 'SumSub'];
                const providerStatsData = providers.map(provName => {
                    const providerData = filteredData.filter(item => item.provider === provName);
                    const provTotal = providerData.length;
                    const provApproved = providerData.filter(item => item.status === 'APPROVED').length;
                    const provRejected = providerData.filter(item => item.status === 'REJECTED').length;

                    const provCompleted = providerData.filter(item => item.processingTime !== undefined);
                    const provTotalTime = provCompleted.reduce((sum, item) => sum + (item.processingTime || 0), 0);
                    const provAvgTime = provCompleted.length > 0 ? provTotalTime / provCompleted.length : 0;

                    return {
                        provider: provName,
                        totalVerifications: provTotal,
                        approvalRate: provTotal > 0 ? (provApproved / provTotal) * 100 : 0,
                        rejectionRate: provTotal > 0 ? (provRejected / provTotal) * 100 : 0,
                        averageProcessingTime: provAvgTime
                    };
                });

                // Update state with filtered data and statistics
                setVerifications(filteredData.slice((pagination.current - 1) * pagination.pageSize, pagination.current * pagination.pageSize));
                setPagination({ ...pagination, total: filteredData.length });

                setStats({
                    totalVerifications,
                    approvedVerifications,
                    rejectedVerifications,
                    pendingVerifications,
                    averageProcessingTime,
                    approvalRate,
                    highRiskRate,
                    pepRate
                });

                setProviderStats(providerStatsData);
                setLoading(false);
            }, 1000);
        } catch (error) {
            console.error('Failed to fetch verification data:', error);
            message.error('Failed to load verification data');
            setLoading(false);
        }
    };

    // Handle date range change
    const handleDateRangeChange = (dates: any) => {
        if (dates && dates[0] && dates[1]) {
            setDateRange([dates[0], dates[1]]);
        }
    };

    // Handle provider filter change
    const handleProviderChange = (value: string) => {
        setProvider(value);
    };

    // Handle status filter change
    const handleStatusChange = (value: string) => {
        setStatus(value);
    };

    // Handle pagination change
    const handleTableChange = (pagination: any) => {
        setPagination(pagination);
    };

    // Handle export to CSV
    const handleExportCsv = () => {
        setExportLoading(true);
        setTimeout(() => {
            message.success('Verification data exported successfully');
            setExportLoading(false);
        }, 1500);
    };

    // Get status tag
    const getStatusTag = (status: string) => {
        switch (status) {
            case 'APPROVED':
                return <Tag icon={<CheckCircleOutlined />} color="success">Approved</Tag>;
            case 'REJECTED':
                return <Tag icon={<CloseCircleOutlined />} color="error">Rejected</Tag>;
            case 'PENDING_VERIFICATION':
                return <Tag icon={<SyncOutlined spin />} color="processing">Pending</Tag>;
            default:
                return <Tag>{status}</Tag>;
        }
    };

    // Get provider tag
    const getProviderTag = (provider: string) => {
        switch (provider) {
            case 'Onfido':
                return <Tag color="blue">Onfido</Tag>;
            case 'SumSub':
                return <Tag color="green">SumSub</Tag>;
            default:
                return <Tag>{provider}</Tag>;
        }
    };

    // Table columns configuration
    const columns = [
        {
            title: 'ID',
            dataIndex: 'id',
            key: 'id',
            width: 100,
            ellipsis: true,
        },
        {
            title: 'User',
            dataIndex: 'email',
            key: 'email',
            ellipsis: true,
        },
        {
            title: 'Provider',
            dataIndex: 'provider',
            key: 'provider',
            render: (provider: string) => getProviderTag(provider),
        },
        {
            title: 'Status',
            dataIndex: 'status',
            key: 'status',
            render: (status: string) => getStatusTag(status),
        },
        {
            title: 'Risk Flags',
            key: 'riskFlags',
            render: (record: VerificationData) => (
                <>
                    {record.isHighRisk && (
                        <Tooltip title="High Risk">
                            <Badge status="error" text="High Risk" className="mr-2" />
                        </Tooltip>
                    )}
                    {record.isPep && (
                        <Tooltip title="Politically Exposed Person">
                            <Badge status="warning" text="PEP" />
                        </Tooltip>
                    )}
                    {!record.isHighRisk && !record.isPep && (
                        <span className="text-green-500">None</span>
                    )}
                </>
            ),
        },
        {
            title: 'Country',
            dataIndex: 'country',
            key: 'country',
        },
        {
            title: 'Submitted',
            dataIndex: 'submittedAt',
            key: 'submittedAt',
            render: (date: string) => new Date(date).toLocaleString(),
        },
        {
            title: 'Completed',
            dataIndex: 'completedAt',
            key: 'completedAt',
            render: (date: string) => date ? new Date(date).toLocaleString() : '-',
        },
        {
            title: 'Processing Time',
            dataIndex: 'processingTime',
            key: 'processingTime',
            render: (time: number) => time ? `${time.toFixed(1)} hrs` : '-',
        },
    ];

    return (
        <div className="space-y-6">
            {/* Filters */}
            <Card title="Verification Reports">
                <div className="flex flex-wrap items-center gap-4 mb-4">
                    <div>
                        <span className="mr-2">Date Range:</span>
                        <RangePicker
                            onChange={handleDateRangeChange}
                            defaultValue={dateRange}
                        />
                    </div>

                    <div>
                        <span className="mr-2">Provider:</span>
                        <Select
                            style={{ width: 120 }}
                            value={provider}
                            onChange={handleProviderChange}
                        >
                            <Option value="all">All</Option>
                            <Option value="Onfido">Onfido</Option>
                            <Option value="SumSub">SumSub</Option>
                        </Select>
                    </div>

                    <div>
                        <span className="mr-2">Status:</span>
                        <Select
                            style={{ width: 120 }}
                            value={status}
                            onChange={handleStatusChange}
                        >
                            <Option value="all">All</Option>
                            <Option value="APPROVED">Approved</Option>
                            <Option value="REJECTED">Rejected</Option>
                            <Option value="PENDING_VERIFICATION">Pending</Option>
                        </Select>
                    </div>

                    <Button
                        type="primary"
                        icon={<SyncOutlined />}
                        onClick={fetchVerificationData}
                        loading={loading}
                    >
                        Refresh
                    </Button>

                    <Button
                        icon={<DownloadOutlined />}
                        onClick={handleExportCsv}
                        loading={exportLoading}
                    >
                        Export CSV
                    </Button>
                </div>

                {/* Stats Overview */}
                {stats && (
                    <Row gutter={16} className="mb-6">
                        <Col span={6}>
                            <Card>
                                <Statistic
                                    title="Total Verifications"
                                    value={stats.totalVerifications}
                                    prefix={<UserOutlined />}
                                />
                            </Card>
                        </Col>
                        <Col span={6}>
                            <Card>
                                <Statistic
                                    title="Approval Rate"
                                    value={stats.approvalRate}
                                    precision={1}
                                    suffix="%"
                                    valueStyle={{ color: stats.approvalRate > 95 ? '#3f8600' : (stats.approvalRate > 85 ? '#faad14' : '#cf1322') }}
                                    prefix={stats.approvalRate > 90 ? <RiseOutlined /> : <FallOutlined />}
                                />
                            </Card>
                        </Col>
                        <Col span={6}>
                            <Card>
                                <Statistic
                                    title="Avg. Processing Time"
                                    value={stats.averageProcessingTime}
                                    precision={1}
                                    suffix="hrs"
                                />
                            </Card>
                        </Col>
                        <Col span={6}>
                            <Card>
                                <Statistic
                                    title="High Risk Rate"
                                    value={stats.highRiskRate}
                                    precision={1}
                                    suffix="%"
                                    valueStyle={{ color: stats.highRiskRate < 5 ? '#3f8600' : (stats.highRiskRate < 10 ? '#faad14' : '#cf1322') }}
                                />
                            </Card>
                        </Col>
                    </Row>
                )}

                {/* Provider Comparison */}
                {providerStats.length > 0 && (
                    <div className="mb-6">
                        <h3 className="text-lg font-medium mb-4">Provider Comparison</h3>
                        <Row gutter={[16, 16]}>
                            {providerStats.map(provStat => (
                                <Col span={12} key={provStat.provider}>
                                    <Card
                                        title={
                                            <div className="flex items-center">
                                                <img
                                                    src={`/images/${provStat.provider.toLowerCase()}-logo.svg`}
                                                    alt={provStat.provider}
                                                    style={{ height: 20, marginRight: 8 }}
                                                />
                                                {provStat.provider}
                                            </div>
                                        }
                                    >
                                        <Row gutter={16}>
                                            <Col span={8}>
                                                <Statistic
                                                    title="Total"
                                                    value={provStat.totalVerifications}
                                                />
                                            </Col>
                                            <Col span={8}>
                                                <Statistic
                                                    title="Approval Rate"
                                                    value={provStat.approvalRate}
                                                    precision={1}
                                                    suffix="%"
                                                    valueStyle={{ color: provStat.approvalRate > 95 ? '#3f8600' : (provStat.approvalRate > 85 ? '#faad14' : '#cf1322') }}
                                                />
                                            </Col>
                                            <Col span={8}>
                                                <Statistic
                                                    title="Avg Time"
                                                    value={provStat.averageProcessingTime}
                                                    precision={1}
                                                    suffix="hrs"
                                                />
                                            </Col>
                                        </Row>
                                    </Card>
                                </Col>
                            ))}
                        </Row>
                    </div>
                )}

                {/* Verification Data Table */}
                <Table
                    columns={columns}
                    dataSource={verifications}
                    rowKey="id"
                    pagination={pagination}
                    loading={loading}
                    onChange={handleTableChange}
                />
            </Card>
        </div>
    );
};

export default KycVerificationReports;