import {
    BarChartOutlined,
    TransactionOutlined,
    WalletOutlined
} from '@ant-design/icons';
import {
    Button,
    Card,
    Col,
    Divider,
    Empty,
    Flex,
    Row,
    Space,
    Tooltip,
    Typography
} from 'antd';
import React from 'react';
import { useNavigate } from "react-router-dom";
import { AssetColors } from '../../types/assetTypes';
import { AssetHolding } from '../../types/dashboardTypes';

const { Title, Text } = Typography;

interface AssetBalanceCardProps {
    assetHoldings: AssetHolding[];
}

const AssetBalanceCard: React.FC<AssetBalanceCardProps> = ({ assetHoldings }) => {
    const navigate = useNavigate();

    // Fallback color for assets not in the list
    const getAssetColor = (ticker: string | undefined): string => {
        if (!ticker) return '#6B7280'; // Gray fallback if ticker is undefined
        return AssetColors[ticker] || '#6B7280'; // Gray fallback
    };

    const formatCurrency = (amount: number): string => {
        return new Intl.NumberFormat('en-US', {
            style: 'currency',
            currency: 'USD',
            minimumFractionDigits: 2,
            maximumFractionDigits: 2,
        }).format(amount);
    };

    // Calculate total value to determine proportions
    const calculateTotal = (): number => {
        if (!assetHoldings || !assetHoldings.length) return 0;
        return assetHoldings.reduce((sum, holding) => sum + holding.value, 0);
    };

    const totalValue = calculateTotal();

    // Handle withdraw click
    const handleWithdraw = () => {
        navigate(`/withdraw`);
    };

    // Format number with appropriate precision
    const formatAmount = (amount: number): string => {
        // For small amounts (like Bitcoin), show more decimals
        if (amount < 0.1) {
            return amount.toFixed(6);
        }
        // For medium amounts
        else if (amount < 1000) {
            return amount.toFixed(2);
        }
        // For large amounts, format with commas
        else {
            return amount.toLocaleString('en-US', {
                minimumFractionDigits: 2,
                maximumFractionDigits: 2
            });
        }
    };

    // Format percentage for progress bar
    const formatPercentage = (value: number, total: number): number => {
        if (total === 0) return 0;
        return Math.round((value / total) * 100);
    };

    return (
        <Card
            style={{
                height: '100%',
                background: 'rgba(255, 255, 255, 0.9)',
                backdropFilter: 'blur(10px)',
                border: '1px solid rgba(255, 255, 255, 0.2)',
                borderRadius: '16px',
                boxShadow: '0 8px 32px rgba(0, 0, 0, 0.1)',
            }}
            hoverable
        >
            {/* Header */}
            <div style={{ marginBottom: '20px' }}>
                <Row justify="space-between" align="middle">
                    <Col>
                        <Space align="center">
                            <div style={{
                                background: 'linear-gradient(135deg, #4caf50 0%, #00bcd4 100%)',
                                padding: '8px',
                                borderRadius: '8px',
                                display: 'flex',
                                alignItems: 'center',
                                justifyContent: 'center'
                            }}>
                                <WalletOutlined style={{color: 'white' }} />
                                
                            </div>
                            <Title level={4} style={{ margin: 0 }}>
                                Asset Holdings
                            </Title>
                        </Space>
                    </Col>
                    {/* Action */}
                    <Col>
                        <Button
                            size="large"
                            onClick={handleWithdraw}
                            icon={<TransactionOutlined />}
                        >
                            Withdraw
                        </Button>
                    </Col>
                </Row>
            </div>

            {assetHoldings && assetHoldings.length > 0 ? (
                <>
                    {/* Portfolio Distribution Visualization */}
                    <div style={{ marginBottom: '24px' }}>
                        <Text type="secondary" style={{ fontSize: '12px', marginBottom: '8px', display: 'block' }}>
                            Portfolio Distribution
                        </Text>
                        <div style={{
                            height: '8px',
                            background: '#f5f5f5',
                            borderRadius: '4px',
                            overflow: 'hidden',
                            display: 'flex',
                            marginBottom: '8px'
                        }}>
                            {assetHoldings.map((holding) => {
                                const percentage = (holding.value / totalValue) * 100;
                                return (
                                    <Tooltip
                                        key={holding.id}
                                        title={`${holding.ticker}: ${percentage.toFixed(1)}% ($${holding.value.toFixed(2)})`}
                                    >
                                        <div
                                            style={{
                                                width: `${percentage}%`,
                                                backgroundColor: getAssetColor(holding.ticker),
                                                height: '100%',
                                                cursor: 'pointer'
                                            }}
                                        />
                                    </Tooltip>
                                );
                            })}
                        </div>
                    </div>

                    {/* Asset List */}
                    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
                        {assetHoldings.map((holding, index) => {
                            const percentage = formatPercentage(holding.value, totalValue);

                            return (
                                <Card
                                    key={holding.id}
                                    size="small"
                                >
                                    <Flex justify="space-between">
                                        {/* Asset Info */}
                                        <Space>
                                            <div
                                                style={{
                                                    width: '14px',
                                                    height: '14px',
                                                    backgroundColor: getAssetColor(holding.ticker),
                                                    borderRadius: '50%',
                                                    flexShrink: 0
                                                }}
                                            />
                                            <Text
                                                type="secondary"
                                                style={{ fontSize: '12px' }}
                                            >
                                                {holding.name || 'Unknown'} ({holding.ticker || 'N/A'})
                                            </Text>
                                        </Space>

                                        {/* Percentage */}
                                        <Space direction="vertical">
                                            <Text type="secondary" style={{ fontSize: '11px' }}>
                                                Percent
                                            </Text>
                                            <Text strong style={{ fontSize: '13px', display: 'block', color: '#52c41a' }}>
                                                {percentage}%
                                            </Text>
                                        </Space>

                                        {/* Amount */}
                                        <Space direction="vertical">
                                            <Text type="secondary" style={{ fontSize: '11px' }}>
                                                Balance
                                            </Text>
                                            <Text strong style={{ fontSize: '13px', display: 'block' }}>
                                                {formatAmount(holding.total)}
                                            </Text>
                                        </Space>

                                        {/* Value */}
                                        <Space direction="vertical">
                                            <Text type="secondary" style={{ fontSize: '11px' }}>
                                                Value
                                            </Text>
                                            <Text strong style={{ fontSize: '13px', display: 'block', color: '#52c41a' }}>
                                                {formatCurrency(holding.value)}
                                            </Text>
                                        </Space>
                                    </Flex>
                                </Card>
                            );
                        })}
                    </Space>

                    <Divider style={{ margin: '20px 0' }} />

                    {/* Summary Footer */}
                    <Row justify="space-between" align="middle">
                        <Col>
                            <Space align="center">
                                <BarChartOutlined style={{ color: '#1890ff' }} />
                                <Text strong>Total Portfolio</Text>
                                <Text type="secondary" style={{ fontSize: '12px' }}>
                                    {assetHoldings.length} {assetHoldings.length === 1 ? 'asset' : 'assets'}
                                </Text>
                            </Space>
                        </Col>
                        <Col>
                            <Space direction="vertical" size={0} style={{ textAlign: 'right' }}>
                                <Text strong style={{ fontSize: '16px', color: '#52c41a' }}>
                                    {formatCurrency(totalValue)}
                                </Text>
                                
                            </Space>
                        </Col>
                    </Row>
                </>
            ) : (
                <Empty
                    image={Empty.PRESENTED_IMAGE_SIMPLE}
                    description={
                        <div style={{ textAlign: 'center' }}>
                            <Title level={5} style={{ color: '#8c8c8c', marginBottom: '8px' }}>
                                No Assets Found
                            </Title>
                            <Text type="secondary" style={{ fontSize: '14px' }}>
                                Start investing to see your asset holdings here
                            </Text>
                        </div>
                    }
                    style={{ padding: '40px 20px' }}
                />
            )}
        </Card>
    );
};

export default AssetBalanceCard;