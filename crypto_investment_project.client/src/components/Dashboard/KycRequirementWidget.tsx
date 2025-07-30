import React, { useEffect, useState, useMemo } from 'react';
import { motion } from 'framer-motion';
import {
    LineChart,
    Line,
    XAxis,
    YAxis,
    CartesianGrid,
    Tooltip,
    Legend,
    ResponsiveContainer,
    Area,
    AreaChart,
    ReferenceLine
} from 'recharts';
import {
    Card,
    Typography,
    Space,
    Switch,
    Row,
    Col,
    Statistic,
    Tag,
    Empty,
    Spin,
    Button,
    Tooltip as AntTooltip
} from 'antd';
import {
    TrendingUpOutlined,
    TrendingDownOutlined,
    BarChartOutlined,
    LineChartOutlined,
    AreaChartOutlined,
    ReloadOutlined,
    InfoCircleOutlined
} from '@ant-design/icons';

const { Title, Text } = Typography;

interface PortfolioChartProps {
    investmentData: number;
    portfolioData: number;
}

interface ChartDataPoint {
    name: string;
    investment: number;
    portfolio: number;
    profit: number;
    profitPercentage: number;
    date: string;
}

interface ChartConfig {
    showArea: boolean;
    showGrid: boolean;
    animated: boolean;
}

const PortfolioChart: React.FC<PortfolioChartProps> = ({ investmentData, portfolioData }) => {
    const [chartData, setChartData] = useState<ChartDataPoint[]>([]);
    const [loading, setLoading] = useState(true);
    const [config, setConfig] = useState<ChartConfig>({
        showArea: true,
        showGrid: true,
        animated: true
    });

    // Calculate key metrics
    const metrics = useMemo(() => {
        if (!chartData.length) return null;

        const latestData = chartData[chartData.length - 1];
        const firstData = chartData[0];

        const totalProfit = latestData.portfolio - latestData.investment;
        const profitPercentage = latestData.investment > 0
            ? ((latestData.portfolio - latestData.investment) / latestData.investment) * 100
            : 0;

        const growthFromStart = firstData.investment > 0
            ? ((latestData.portfolio - firstData.portfolio) / firstData.portfolio) * 100
            : 0;

        return {
            totalProfit,
            profitPercentage,
            growthFromStart,
            isProfitable: totalProfit >= 0,
            isGrowing: growthFromStart >= 0
        };
    }, [chartData]);

    useEffect(() => {
        if (investmentData > 0 || portfolioData > 0) {
            generateSampleChartData();
        } else {
            setLoading(false);
        }
    }, [investmentData, portfolioData]);

    const generateSampleChartData = () => {
        setLoading(true);

        // Simulate loading delay for smooth animation
        setTimeout(() => {
            const data: ChartDataPoint[] = [];
            const now = new Date();

            // Generate data for the last 6 months
            for (let i = 5; i >= 0; i--) {
                const date = new Date(now);
                date.setMonth(now.getMonth() - i);

                const monthName = date.toLocaleString('default', { month: 'short' });
                const fullDate = date.toLocaleDateString();

                // Create a realistic growth pattern
                const monthlyInvestment = investmentData / 6;
                const currentInvestment = monthlyInvestment * (6 - i);

                // Portfolio value has more variance with realistic market fluctuations
                const growthFactors = [0.98, 1.02, 0.99, 1.05, 1.03, 1.01];
                const volatilityFactor = 0.95 + (Math.random() * 0.1);
                const portfolioValue = currentInvestment * growthFactors[5 - i] * volatilityFactor;

                const profit = portfolioValue - currentInvestment;
                const profitPercentage = currentInvestment > 0 ? (profit / currentInvestment) * 100 : 0;

                data.push({
                    name: monthName,
                    investment: parseFloat(currentInvestment.toFixed(2)),
                    portfolio: parseFloat(portfolioValue.toFixed(2)),
                    profit: parseFloat(profit.toFixed(2)),
                    profitPercentage: parseFloat(profitPercentage.toFixed(2)),
                    date: fullDate
                });
            }

            // Ensure the final point matches exactly with the current data
            if (data.length > 0) {
                const lastPoint = data[data.length - 1];
                lastPoint.investment = parseFloat(investmentData.toFixed(2));
                lastPoint.portfolio = parseFloat(portfolioData.toFixed(2));
                lastPoint.profit = parseFloat((portfolioData - investmentData).toFixed(2));
                lastPoint.profitPercentage = investmentData > 0
                    ? parseFloat(((portfolioData - investmentData) / investmentData * 100).toFixed(2))
                    : 0;
            }

            setChartData(data);
            setLoading(false);
        }, 800);
    };

    // Custom tooltip component
    const CustomTooltip = ({ active, payload, label }: any) => {
        if (active && payload && payload.length) {
            const data = payload[0].payload;

            return (
                <Card
                    size="small"
                    style={{
                        background: 'rgba(255, 255, 255, 0.95)',
                        backdropFilter: 'blur(10px)',
                        border: '1px solid rgba(0, 0, 0, 0.1)',
                        borderRadius: '12px',
                        boxShadow: '0 8px 32px rgba(0, 0, 0, 0.1)',
                        minWidth: '200px'
                    }}
                    bodyStyle={{ padding: '12px' }}
                >
                    <div style={{ marginBottom: '8px' }}>
                        <Text strong style={{ fontSize: '14px' }}>{label}</Text>
                        <br />
                        <Text type="secondary" style={{ fontSize: '12px' }}>{data.date}</Text>
                    </div>

                    <Space direction="vertical" size="small" style={{ width: '100%' }}>
                        <Row justify="space-between">
                            <Col>
                                <Space align="center">
                                    <div style={{
                                        width: '8px',
                                        height: '8px',
                                        background: '#3B82F6',
                                        borderRadius: '50%'
                                    }} />
                                    <Text style={{ fontSize: '13px' }}>Investment:</Text>
                                </Space>
                            </Col>
                            <Col>
                                <Text strong style={{ fontSize: '13px' }}>
                                    ${data.investment.toLocaleString()}
                                </Text>
                            </Col>
                        </Row>

                        <Row justify="space-between">
                            <Col>
                                <Space align="center">
                                    <div style={{
                                        width: '8px',
                                        height: '8px',
                                        background: '#10B981',
                                        borderRadius: '50%'
                                    }} />
                                    <Text style={{ fontSize: '13px' }}>Portfolio:</Text>
                                </Space>
                            </Col>
                            <Col>
                                <Text strong style={{ fontSize: '13px' }}>
                                    ${data.portfolio.toLocaleString()}
                                </Text>
                            </Col>
                        </Row>

                        <div style={{
                            background: data.profit >= 0 ? '#f6ffed' : '#fff2f0',
                            padding: '6px 8px',
                            borderRadius: '6px',
                            marginTop: '4px'
                        }}>
                            <Row justify="space-between" align="middle">
                                <Col>
                                    <Text style={{
                                        fontSize: '12px',
                                        color: data.profit >= 0 ? '#52c41a' : '#ff4d4f'
                                    }}>
                                        P&L:
                                    </Text>
                                </Col>
                                <Col>
                                    <Space align="center">
                                        {data.profit >= 0 ? (
                                            <TrendingUpOutlined style={{
                                                fontSize: '12px',
                                                color: '#52c41a'
                                            }} />
                                        ) : (
                                            <TrendingDownOutlined style={{
                                                fontSize: '12px',
                                                color: '#ff4d4f'
                                            }} />
                                        )}
                                        <Text strong style={{
                                            fontSize: '12px',
                                            color: data.profit >= 0 ? '#52c41a' : '#ff4d4f'
                                        }}>
                                            ${Math.abs(data.profit).toLocaleString()} ({data.profitPercentage.toFixed(2)}%)
                                        </Text>
                                    </Space>
                                </Col>
                            </Row>
                        </div>
                    </Space>
                </Card>
            );
        }
        return null;
    };

    // Chart controls
    const renderControls = () => (
        <Row justify="space-between" align="middle" style={{ marginBottom: '16px' }}>
            <Col>
                <Space wrap>
                    <AntTooltip title="Toggle area chart">
                        <Switch
                            checkedChildren={<AreaChartOutlined />}
                            unCheckedChildren={<LineChartOutlined />}
                            checked={config.showArea}
                            onChange={(checked) => setConfig(prev => ({ ...prev, showArea: checked }))}
                            size="small"
                        />
                    </AntTooltip>

                    <AntTooltip title="Toggle grid lines">
                        <Switch
                            checkedChildren="Grid"
                            unCheckedChildren="Grid"
                            checked={config.showGrid}
                            onChange={(checked) => setConfig(prev => ({ ...prev, showGrid: checked }))}
                            size="small"
                        />
                    </AntTooltip>
                </Space>
            </Col>

            <Col>
                <Button
                    type="text"
                    size="small"
                    icon={<ReloadOutlined />}
                    onClick={generateSampleChartData}
                    loading={loading}
                >
                    Refresh
                </Button>
            </Col>
        </Row>
    );

    // Performance metrics display
    const renderMetrics = () => {
        if (!metrics) return null;

        return (
            <Row gutter={[16, 8]} style={{ marginBottom: '16px' }}>
                <Col xs={12} sm={8}>
                    <Card
                        size="small"
                        style={{
                            background: metrics.isProfitable
                                ? 'linear-gradient(135deg, #f6ffed 0%, #d9f7be 100%)'
                                : 'linear-gradient(135deg, #fff2f0 0%, #ffccc7 100%)',
                            border: `1px solid ${metrics.isProfitable ? '#b7eb8f' : '#ffccc7'}`,
                            borderRadius: '8px'
                        }}
                        bodyStyle={{ padding: '8px 12px', textAlign: 'center' }}
                    >
                        <Statistic
                            title="Total P&L"
                            value={Math.abs(metrics.totalProfit)}
                            precision={2}
                            prefix={metrics.isProfitable ? '+$' : '-$'}
                            valueStyle={{
                                color: metrics.isProfitable ? '#52c41a' : '#ff4d4f',
                                fontSize: '16px'
                            }}
                        />
                    </Card>
                </Col>

                <Col xs={12} sm={8}>
                    <Card
                        size="small"
                        style={{
                            background: 'linear-gradient(135deg, #f0f5ff 0%, #d6e4ff 100%)',
                            border: '1px solid #adc6ff',
                            borderRadius: '8px'
                        }}
                        bodyStyle={{ padding: '8px 12px', textAlign: 'center' }}
                    >
                        <Statistic
                            title="ROI"
                            value={Math.abs(metrics.profitPercentage)}
                            precision={2}
                            suffix="%"
                            prefix={metrics.isProfitable ? '+' : '-'}
                            valueStyle={{
                                color: '#1890ff',
                                fontSize: '16px'
                            }}
                        />
                    </Card>
                </Col>

                <Col xs={24} sm={8}>
                    <Card
                        size="small"
                        style={{
                            background: metrics.isGrowing
                                ? 'linear-gradient(135deg, #fff7e6 0%, #ffd591 100%)'
                                : 'linear-gradient(135deg, #f9f0ff 0%, #d3adf7 100%)',
                            border: `1px solid ${metrics.isGrowing ? '#ffd591' : '#d3adf7'}`,
                            borderRadius: '8px'
                        }}
                        bodyStyle={{ padding: '8px 12px', textAlign: 'center' }}
                    >
                        <Statistic
                            title="6M Growth"
                            value={Math.abs(metrics.growthFromStart)}
                            precision={2}
                            suffix="%"
                            prefix={metrics.isGrowing ? '+' : '-'}
                            valueStyle={{
                                color: metrics.isGrowing ? '#faad14' : '#722ed1',
                                fontSize: '16px'
                            }}
                        />
                    </Card>
                </Col>
            </Row>
        );
    };

    // Loading state
    if (loading) {
        return (
            <motion.div
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                style={{
                    height: '400px',
                    display: 'flex',
                    flexDirection: 'column',
                    justifyContent: 'center',
                    alignItems: 'center',
                    background: 'linear-gradient(135deg, #f8fafc 0%, #f1f5f9 100%)',
                    borderRadius: '12px'
                }}
            >
                <Spin size="large" />
                <Text style={{ marginTop: '16px', color: '#64748b' }}>
                    Loading chart data...
                </Text>
            </motion.div>
        );
    }

    // Empty state
    if (!chartData.length) {
        return (
            <motion.div
                initial={{ opacity: 0, scale: 0.95 }}
                animate={{ opacity: 1, scale: 1 }}
                transition={{ duration: 0.3 }}
            >
                <Card
                    style={{
                        background: 'linear-gradient(135deg, #f8fafc 0%, #f1f5f9 100%)',
                        border: '1px solid #e2e8f0',
                        borderRadius: '12px',
                        textAlign: 'center',
                        minHeight: '320px',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center'
                    }}
                >
                    <Empty
                        image={Empty.PRESENTED_IMAGE_SIMPLE}
                        description={
                            <div>
                                <Text strong style={{ fontSize: '16px', color: '#64748b' }}>
                                    No Investment Data
                                </Text>
                                <br />
                                <Text type="secondary" style={{ fontSize: '14px' }}>
                                    Start investing to see your portfolio performance
                                </Text>
                            </div>
                        }
                    />
                </Card>
            </motion.div>
        );
    }

    return (
        <motion.div
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ duration: 0.5 }}
            style={{ width: '100%' }}
        >
            {/* Performance Metrics */}
            {renderMetrics()}

            {/* Chart Controls */}
            {renderControls()}

            {/* Main Chart */}
            <Card
                style={{
                    background: 'rgba(255, 255, 255, 0.9)',
                    backdropFilter: 'blur(10px)',
                    border: '1px solid rgba(255, 255, 255, 0.2)',
                    borderRadius: '12px',
                    boxShadow: '0 4px 20px rgba(0, 0, 0, 0.05)',
                }}
                bodyStyle={{ padding: '16px' }}
            >
                <ResponsiveContainer width="100%" height={320}>
                    {config.showArea ? (
                        <AreaChart
                            data={chartData}
                            margin={{
                                top: 10,
                                right: 30,
                                left: 20,
                                bottom: 5,
                            }}
                        >
                            {config.showGrid && (
                                <CartesianGrid
                                    strokeDasharray="3 3"
                                    stroke="#e2e8f0"
                                    strokeOpacity={0.5}
                                />
                            )}
                            <XAxis
                                dataKey="name"
                                axisLine={false}
                                tickLine={false}
                                tick={{ fontSize: 12, fill: '#64748b' }}
                            />
                            <YAxis
                                tickFormatter={(value) => `$${value.toLocaleString()}`}
                                axisLine={false}
                                tickLine={false}
                                tick={{ fontSize: 12, fill: '#64748b' }}
                                width={60}
                            />
                            <Tooltip content={<CustomTooltip />} />
                            <Legend
                                wrapperStyle={{ paddingTop: '20px' }}
                                iconType="circle"
                            />

                            {/* Investment area */}
                            <Area
                                type="monotone"
                                dataKey="investment"
                                stackId="1"
                                stroke="#3B82F6"
                                fill="url(#investmentGradient)"
                                strokeWidth={2}
                                name="Investment"
                                dot={false}
                                activeDot={{
                                    r: 6,
                                    stroke: '#3B82F6',
                                    strokeWidth: 2,
                                    fill: '#fff'
                                }}
                            />

                            {/* Portfolio area */}
                            <Area
                                type="monotone"
                                dataKey="portfolio"
                                stroke="#10B981"
                                fill="url(#portfolioGradient)"
                                strokeWidth={2}
                                name="Portfolio Value"
                                dot={false}
                                activeDot={{
                                    r: 6,
                                    stroke: '#10B981',
                                    strokeWidth: 2,
                                    fill: '#fff'
                                }}
                            />

                            {/* Gradient definitions */}
                            <defs>
                                <linearGradient id="investmentGradient" x1="0" y1="0" x2="0" y2="1">
                                    <stop offset="5%" stopColor="#3B82F6" stopOpacity={0.3} />
                                    <stop offset="95%" stopColor="#3B82F6" stopOpacity={0.05} />
                                </linearGradient>
                                <linearGradient id="portfolioGradient" x1="0" y1="0" x2="0" y2="1">
                                    <stop offset="5%" stopColor="#10B981" stopOpacity={0.3} />
                                    <stop offset="95%" stopColor="#10B981" stopOpacity={0.05} />
                                </linearGradient>
                            </defs>

                            {/* Break-even line */}
                            <ReferenceLine
                                stroke="#94a3b8"
                                strokeDasharray="2 2"
                                strokeOpacity={0.5}
                            />
                        </AreaChart>
                    ) : (
                        <LineChart
                            data={chartData}
                            margin={{
                                top: 10,
                                right: 30,
                                left: 20,
                                bottom: 5,
                            }}
                        >
                            {config.showGrid && (
                                <CartesianGrid
                                    strokeDasharray="3 3"
                                    stroke="#e2e8f0"
                                    strokeOpacity={0.5}
                                />
                            )}
                            <XAxis
                                dataKey="name"
                                axisLine={false}
                                tickLine={false}
                                tick={{ fontSize: 12, fill: '#64748b' }}
                            />
                            <YAxis
                                tickFormatter={(value) => `$${value.toLocaleString()}`}
                                axisLine={false}
                                tickLine={false}
                                tick={{ fontSize: 12, fill: '#64748b' }}
                                width={60}
                            />
                            <Tooltip content={<CustomTooltip />} />
                            <Legend
                                wrapperStyle={{ paddingTop: '20px' }}
                                iconType="circle"
                            />

                            <Line
                                type="monotone"
                                dataKey="investment"
                                stroke="#3B82F6"
                                strokeWidth={3}
                                name="Investment"
                                dot={{ strokeWidth: 2, r: 4 }}
                                activeDot={{
                                    r: 8,
                                    stroke: '#3B82F6',
                                    strokeWidth: 2,
                                    fill: '#fff'
                                }}
                            />

                            <Line
                                type="monotone"
                                dataKey="portfolio"
                                stroke="#10B981"
                                strokeWidth={3}
                                name="Portfolio Value"
                                dot={{ strokeWidth: 2, r: 4 }}
                                activeDot={{
                                    r: 8,
                                    stroke: '#10B981',
                                    strokeWidth: 2,
                                    fill: '#fff'
                                }}
                            />
                        </LineChart>
                    )}
                </ResponsiveContainer>
            </Card>

            {/* Chart Footer */}
            <div style={{
                marginTop: '12px',
                textAlign: 'center',
                padding: '8px'
            }}>
                <Space align="center">
                    <InfoCircleOutlined style={{ color: '#64748b', fontSize: '12px' }} />
                    <Text type="secondary" style={{ fontSize: '12px' }}>
                        Historical data is simulated for demonstration purposes
                    </Text>
                </Space>
            </div>
        </motion.div>
    );
};

export default PortfolioChart;