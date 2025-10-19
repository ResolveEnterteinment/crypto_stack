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
    Row,
    Tooltip,
    Typography
} from 'antd';
import React from 'react';
import { useNavigate } from "react-router-dom";
import { AssetColors } from '../../types/assetTypes';
import { AssetHolding } from '../../types/dashboardTypes';
import styles from '../../styles/Dashboard/AssetBalanceCard.module.css';

const { Title, Text } = Typography;

interface AssetBalanceCardProps {
    assetHoldings: AssetHolding[];
}

const AssetBalanceCard: React.FC<AssetBalanceCardProps> = ({ assetHoldings }) => {
    const navigate = useNavigate();

    // Fallback color for assets not in the list
    const getAssetColor = (ticker: string | undefined): string => {
        if (!ticker) return '#6B7280';
        return AssetColors[ticker] || '#6B7280';
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
        if (amount < 0.1) {
            return amount.toFixed(6);
        } else if (amount < 1000) {
            return amount.toFixed(2);
        } else {
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
        <Card className={styles.card}>
            {/* Header */}
            <div className={styles.header}>
                <Row justify="space-between" align="middle">
                    <Col>
                        <div className={styles.headerLeft}>
                            <div className={styles.icon}>
                                <WalletOutlined />
                            </div>
                            <Title level={4} className={styles.title}>
                                Asset Holdings
                            </Title>
                        </div>
                    </Col>
                    <Col>
                        <Button
                            onClick={handleWithdraw}
                            icon={<TransactionOutlined />}
                            className={"btn-ghost"}
                        >
                            Withdraw
                        </Button>
                    </Col>
                </Row>
            </div>

            {assetHoldings && assetHoldings.length > 0 ? (
                <>
                    {/* Portfolio Distribution Visualization */}
                    <div className={styles.distribution}>
                        <Text className={styles.distributionLabel}>
                            Portfolio Distribution
                        </Text>
                        <div className={styles.distributionBar}>
                            {assetHoldings.map((holding) => {
                                const percentage = (holding.value / totalValue) * 100;
                                return (
                                    <Tooltip
                                        key={holding.id}
                                        title={`${holding.ticker}: ${percentage.toFixed(1)}% ($${holding.value.toFixed(2)})`}
                                    >
                                        <div
                                            className={styles.distributionSegment}
                                            style={{
                                                width: `${percentage}%`,
                                                backgroundColor: getAssetColor(holding.ticker)
                                            }}
                                        />
                                    </Tooltip>
                                );
                            })}
                        </div>
                    </div>

                    {/* Asset List */}
                    <div className={styles.assetList}>
                        {assetHoldings.map((holding) => {
                            const percentage = formatPercentage(holding.value, totalValue);

                            return (
                                <Card
                                    key={holding.id}
                                    size="small"
                                    className={styles.assetCard}
                                >
                                    <div className={styles.assetRow}>
                                        {/* Asset Info */}
                                        <div className={styles.assetInfo}>
                                            <div
                                                className={styles.assetDot}
                                                style={{
                                                    backgroundColor: getAssetColor(holding.ticker)
                                                }}
                                            />
                                            <div className={styles.assetMetric}>
                                                <Text>{holding.name || 'Unknown'}</Text>
                                                <Text type="secondary">{holding.ticker || 'N/A'}</Text>
                                            </div>
                                        </div>

                                        {/* Percentage */}
                                        <div className={styles.assetMetric}>
                                            <Text className={styles.metricLabel}>
                                                Percent
                                            </Text>
                                            <Text className={`${styles.metricValue} ${styles.metricValueSuccess}`}>
                                                {percentage}%
                                            </Text>
                                        </div>

                                        {/* Amount */}
                                        <div className={styles.assetMetric}>
                                            <Text className={styles.metricLabel}>
                                                Balance
                                            </Text>
                                            <Text className={styles.metricValue}>
                                                {formatAmount(holding.total)}
                                            </Text>
                                        </div>

                                        {/* Value */}
                                        <Col className={styles.assetMetric}>
                                            <Text className={styles.metricLabel}>
                                                Value
                                            </Text>
                                            <Text className={`${styles.metricValue} ${styles.metricValueSuccess}`}>
                                                {formatCurrency(holding.value)}
                                            </Text>
                                        </Col>
                                    </div>
                                </Card>
                            );
                        })}
                    </div>

                    <Divider className={styles.divider} />

                    {/* Summary Footer */}
                    <div className={styles.summary}>
                        <div className={styles.summaryLeft}>
                            <BarChartOutlined className={styles.summaryIcon} />
                            <Text className={styles.summaryLabel}>Total Portfolio</Text>
                            <Text className={styles.summaryCount}>
                                {assetHoldings.length} {assetHoldings.length === 1 ? 'asset' : 'assets'}
                            </Text>
                        </div>
                        <div className={styles.summaryRight}>
                            <Text className={styles.totalValue}>
                                {formatCurrency(totalValue)}
                            </Text>
                        </div>
                    </div>
                </>
            ) : (
                <div className={styles.empty}>
                    <Empty
                        image={Empty.PRESENTED_IMAGE_SIMPLE}
                        description={
                            <div>
                                <Title level={5} className={styles.emptyTitle}>
                                    No Assets Found
                                </Title>
                                <Text className={styles.emptyDescription}>
                                    Start investing to see your asset holdings here
                                </Text>
                            </div>
                        }
                    />
                </div>
            )}
        </Card>
    );
};

export default AssetBalanceCard;