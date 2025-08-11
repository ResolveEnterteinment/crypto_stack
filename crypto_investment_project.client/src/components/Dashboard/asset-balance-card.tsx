import React from 'react';
import { useNavigate } from "react-router-dom";
import {
    Card,
    Typography,
    Space,
    Button,
    Progress,
    Row,
    Col,
    Divider,
    Empty,
    Tooltip,
    Badge
} from 'antd';
import {
    WalletOutlined,
    DollarOutlined,
    BarChartOutlined,
    ArrowRightOutlined,
    TransactionOutlined
} from '@ant-design/icons';
import { AssetHolding } from '../../types/dashboardTypes';
import { AssetColors } from '../../types/assetTypes';

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
                                <div key={holding.id}>
                                    <Card
                                        size="small"
                                        style={{
                                            background: 'linear-gradient(135deg, #fafafa 0%, #f0f0f0 100%)',
                                            border: '1px solid #e8e8e8',
                                            borderRadius: '8px'
                                        }}
                                        bodyStyle={{ padding: '12px' }}
                                    >
                                        <Row align="middle" gutter={[16, 8]}>
                                            {/* Asset Info */}
                                            <Col xs={24} sm={8}>
                                                <Space align="center">
                                                    <div
                                                        style={{
                                                            width: '12px',
                                                            height: '12px',
                                                            backgroundColor: getAssetColor(holding.ticker),
                                                            borderRadius: '50%',
                                                            flexShrink: 0
                                                        }}
                                                    />
                                                    <div>
                                                        <Text strong style={{ fontSize: '14px' }}>
                                                            {holding.ticker || 'N/A'}
                                                        </Text>
                                                        <br />
                                                        <Text
                                                            type="secondary"
                                                            style={{ fontSize: '12px' }}
                                                        >
                                                            {holding.name || 'Unknown'}
                                                        </Text>
                                                    </div>
                                                </Space>
                                            </Col>

                                            {/* Amount */}
                                            <Col xs={12} sm={6}>
                                                <div style={{ textAlign: 'center' }}>
                                                    <Text strong style={{ fontSize: '13px', display: 'block' }}>
                                                        {formatAmount(holding.total)}
                                                    </Text>
                                                    <Text type="secondary" style={{ fontSize: '11px' }}>
                                                        Balance
                                                    </Text>
                                                </div>
                                            </Col>

                                            {/* Value */}
                                            <Col xs={12} sm={6}>
                                                <div style={{ textAlign: 'center' }}>
                                                    <Text strong style={{ fontSize: '13px', display: 'block', color: '#52c41a' }}>
                                                        {formatCurrency(holding.value)}
                                                    </Text>
                                                    <Text type="secondary" style={{ fontSize: '11px' }}>
                                                        {percentage}%
                                                    </Text>
                                                </div>
                                            </Col>
                                        </Row>
                                    </Card>

                                    {index < assetHoldings.length - 1 && (
                                        <div style={{ margin: '4px 0' }} />
                                    )}
                                </div>
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
                            </Space>
                        </Col>
                        <Col>
                            <Space direction="vertical" size={0} style={{ textAlign: 'right' }}>
                                <Text strong style={{ fontSize: '16px', color: '#52c41a' }}>
                                    {formatCurrency(totalValue)}
                                </Text>
                                <Text type="secondary" style={{ fontSize: '12px' }}>
                                    {assetHoldings.length} {assetHoldings.length === 1 ? 'asset' : 'assets'}
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