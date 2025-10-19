import {
    DollarOutlined,
    DownloadOutlined,
    InfoCircleOutlined,
    ReloadOutlined,
    RiseOutlined,
    WalletOutlined
} from '@ant-design/icons';
import { Line, Pie } from '@ant-design/plots';
import {
    Alert,
    Button,
    Card,
    Col,
    DatePicker,
    Row,
    Space,
    Spin,
    Statistic,
    Table,
    Tooltip,
    Typography
} from 'antd';
import dayjs from 'dayjs';
import React, { useEffect, useState } from 'react';
import api from '../../services/api';

const { Title, Text } = Typography;
const { RangePicker } = DatePicker;

interface TreasurySummary {
    totalUsdValue: number;
    totalPlatformFees: number;
    totalDustCollected: number;
    totalRounding: number;
    totalOther: number;
    totalTransactions: number;
    startDate: string;
    endDate: string;
    assetBalances: AssetBalance[];
    dailyBreakdown: DailyRevenue[];
}

interface AssetBalance {
    assetTicker: string;
    balance: number;
    usdValue: number;
    platformFeeBalance: number;
    dustBalance: number;
    roundingBalance: number;
    otherBalance: number;
}

interface DailyRevenue {
    date: string;
    totalUsd: number;
    platformFees: number;
    dust: number;
    rounding: number;
    transactionCount: number;
}

export const TreasuryDashboard: React.FC = () => {
    const [loading, setLoading] = useState(false);
    const [summary, setSummary] = useState<TreasurySummary | null>(null);
    const [dateRange, setDateRange] = useState<[dayjs.Dayjs, dayjs.Dayjs]>([
        dayjs().subtract(30, 'days'),
        dayjs()
    ]);

    useEffect(() => {
        fetchTreasurySummary();
    }, [dateRange]);

    const fetchTreasurySummary = async () => {
        setLoading(true);
        try {
            const response = await api.get<TreasurySummary>(
                `treasury/summary?startDate=${dateRange[0].format('YYYY-MM-DD')}&endDate=${dateRange[1].format('YYYY-MM-DD')}`
            );
            const data = response.data;
            setSummary(data);
        } catch (error) {
            console.error('Error fetching treasury summary:', error);
        } finally {
            setLoading(false);
        }
    };

    const handleExport = async (format: string) => {
        try {
            const response = await api.getBlob(
                `treasury/export?startDate=${dateRange[0].format('YYYY-MM-DD')}&endDate=${dateRange[1].format('YYYY-MM-DD')}&format=${format}`
            );
            const blob = await response.data;
            const url = window.URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = `treasury-report-${dayjs().format('YYYY-MM-DD')}.${format}`;
            document.body.appendChild(a);
            a.click();
            window.URL.revokeObjectURL(url);
            document.body.removeChild(a);
        } catch (error) {
            console.error('Error exporting data:', error);
        }
    };

    const assetColumns = [
        {
            title: 'Asset',
            dataIndex: 'assetTicker',
            key: 'assetTicker',
            render: (ticker: string) => (
                <Text strong>{ticker}</Text>
            )
        },
        {
            title: 'Total Balance',
            dataIndex: 'balance',
            key: 'balance',
            render: (balance: number, record: AssetBalance) => (
                <Space direction="vertical" size={0}>
                    <Text>{balance.toFixed(8)}</Text>
                    <Text type="secondary" style={{ fontSize: '12px' }}>
                        ${record.usdValue.toFixed(2)} USD
                    </Text>
                </Space>
            )
        },
        {
            title: (
                <Tooltip title="Platform fees (1% from transactions)">
                    Platform Fees <InfoCircleOutlined />
                </Tooltip>
            ),
            dataIndex: 'platformFeeBalance',
            key: 'platformFeeBalance',
            render: (balance: number) => balance.toFixed(8)
        },
        {
            title: (
                <Tooltip title="Dust collected from orders">
                    Dust <InfoCircleOutlined />
                </Tooltip>
            ),
            dataIndex: 'dustBalance',
            key: 'dustBalance',
            render: (balance: number) => balance.toFixed(8)
        },
        {
            title: (
                <Tooltip title="Rounding differences">
                    Rounding <InfoCircleOutlined />
                </Tooltip>
            ),
            dataIndex: 'roundingBalance',
            key: 'roundingBalance',
            render: (balance: number) => balance.toFixed(8)
        },
        {
            title: 'Other',
            dataIndex: 'otherBalance',
            key: 'otherBalance',
            render: (balance: number) => balance.toFixed(8)
        }
    ];

    // Prepare chart data
    const revenueLineData = summary?.dailyBreakdown.map(day => [
        { date: day.date, value: day.platformFees, category: 'Platform Fees' },
        { date: day.date, value: day.dust, category: 'Dust' },
        { date: day.date, value: day.rounding, category: 'Rounding' }
    ]).flat() || [];

    const revenuePieData = summary ? [
        { type: 'Platform Fees', value: summary.totalPlatformFees },
        { type: 'Dust Collected', value: summary.totalDustCollected },
        { type: 'Rounding', value: summary.totalRounding },
        { type: 'Other', value: summary.totalOther }
    ].filter(item => item.value > 0) : [];

    if (loading && !summary) {
        return (
            <div style={{ textAlign: 'center', padding: '100px' }}>
                <Spin size="large" />
            </div>
        );
    }

    return (
        <div style={{ padding: '24px' }}>
            <Row gutter={[16, 16]} align="middle" style={{ marginBottom: '24px' }}>
                <Col flex="auto">
                    <Title level={2}>
                        <WalletOutlined /> Corporate Treasury Dashboard
                    </Title>
                    <Text type="secondary">
                        Track platform revenue from fees, dust, and other sources
                    </Text>
                </Col>
                <Col>
                    <Space>
                        <RangePicker
                            value={dateRange}
                            onChange={(dates) => dates && setDateRange(dates as [dayjs.Dayjs, dayjs.Dayjs])}
                            format="YYYY-MM-DD"
                        />
                        <Button
                            icon={<ReloadOutlined />}
                            onClick={fetchTreasurySummary}
                            loading={loading}
                        >
                            Refresh
                        </Button>
                        <Button
                            icon={<DownloadOutlined />}
                            onClick={() => handleExport('csv')}
                        >
                            Export CSV
                        </Button>
                    </Space>
                </Col>
            </Row>

            <Alert
                message="Revenue Collection Notice"
                description="Platform fees (1%) are collected on all transactions. Dust amounts below minimum trade sizes and rounding differences are retained as part of transaction processing, as disclosed in Terms of Service."
                type="info"
                showIcon
                style={{ marginBottom: '24px' }}
            />

            {/* Summary Statistics */}
            <Row gutter={[16, 16]} style={{ marginBottom: '24px' }}>
                <Col xs={24} sm={12} lg={6}>
                    <Card>
                        <Statistic
                            title="Total Treasury Value"
                            value={summary?.totalUsdValue || 0}
                            prefix={<DollarOutlined />}
                            precision={2}
                            valueStyle={{ color: '#3f8600' }}
                        />
                    </Card>
                </Col>
                <Col xs={24} sm={12} lg={6}>
                    <Card>
                        <Statistic
                            title="Platform Fees"
                            value={summary?.totalPlatformFees || 0}
                            prefix={<DollarOutlined />}
                            precision={2}
                            suffix={
                                <Text type="secondary" style={{ fontSize: '14px' }}>
                                    ({((summary?.totalPlatformFees || 0) / (summary?.totalUsdValue || 1) * 100).toFixed(1)}%)
                                </Text>
                            }
                        />
                    </Card>
                </Col>
                <Col xs={24} sm={12} lg={6}>
                    <Card>
                        <Statistic
                            title="Dust Collected"
                            value={summary?.totalDustCollected || 0}
                            prefix={<DollarOutlined />}
                            precision={2}
                            suffix={
                                <Text type="secondary" style={{ fontSize: '14px' }}>
                                    ({((summary?.totalDustCollected || 0) / (summary?.totalUsdValue || 1) * 100).toFixed(1)}%)
                                </Text>
                            }
                        />
                    </Card>
                </Col>
                <Col xs={24} sm={12} lg={6}>
                    <Card>
                        <Statistic
                            title="Total Transactions"
                            value={summary?.totalTransactions || 0}
                            prefix={<RiseOutlined />}
                        />
                    </Card>
                </Col>
            </Row>

            {/* Charts */}
            <Row gutter={[16, 16]} style={{ marginBottom: '24px' }}>
                <Col xs={24} lg={16}>
                    <Card title="Revenue Over Time" loading={loading}>
                        <Line
                            data={revenueLineData}
                            xField="date"
                            yField="value"
                            seriesField="category"
                            smooth={true}
                            animation={{
                                appear: {
                                    animation: 'path-in',
                                    duration: 1000,
                                },
                            }}
                            tooltip={{
                                formatter: (datum) => ({
                                    name: datum.category,
                                    value: `$${datum.value.toFixed(2)}`
                                })
                            }}
                            xAxis={{
                                type: 'time',
                                tickCount: 5
                            }}
                            yAxis={{
                                label: {
                                    formatter: (v) => `$${parseFloat(v).toFixed(0)}`
                                }
                            }}
                        />
                    </Card>
                </Col>
                <Col xs={24} lg={8}>
                    <Card title="Revenue Breakdown" loading={loading}>
                        <Pie
                            data={revenuePieData}
                            angleField="value"
                            colorField="type"
                            radius={0.8}
                            innerRadius={0.6}
                            label={{
                                type: 'spider',
                                content: '{name}\n${value}'
                            }}
                            statistic={{
                                title: {
                                    content: 'Total',
                                },
                                content: {
                                    content: `$${(summary?.totalUsdValue || 0).toFixed(2)}`,
                                },
                            }}
                        />
                    </Card>
                </Col>
            </Row>

            {/* Asset Balances Table */}
            <Card title="Treasury Balances by Asset" loading={loading}>
                <Table
                    dataSource={summary?.assetBalances || []}
                    columns={assetColumns}
                    rowKey="assetTicker"
                    pagination={false}
                    scroll={{ x: true }}
                />
            </Card>
        </div>
    );
};

export default TreasuryDashboard;
