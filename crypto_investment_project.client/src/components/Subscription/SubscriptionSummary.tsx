// src/components/Subscription/SubscriptionSummary.tsx - Refactored with Global Styling & Ant Design
import React from 'react';
import { Card, Typography, Space, Divider, Tag } from 'antd';
import { CalendarOutlined, DollarOutlined, ClockCircleOutlined } from '@ant-design/icons';
import { Allocation } from '../../types/subscription';
import { AssetColors } from '../../types/assetTypes';

const { Text, Title } = Typography;

interface SubscriptionSummaryProps {
    interval: string;
    amount: number;
    currency: string;
    endDate: Date | null;
    allocations: Omit<Allocation, 'id'>[];
}

const SubscriptionSummary: React.FC<SubscriptionSummaryProps> = ({
    interval,
    amount,
    currency,
    endDate,
    allocations
}) => {
    /**
     * Format the interval for display
     */
    const formatInterval = (intervalCode: string): string => {
        const intervals: Record<string, string> = {
            'ONCE': 'One-time payment',
            'DAILY': 'Daily',
            'WEEKLY': 'Weekly',
            'MONTHLY': 'Monthly',
            'YEARLY': 'Yearly'
        };
        return intervals[intervalCode] || intervalCode;
    };

    /**
     * Format date for display
     */
    const formatDate = (date: Date | null): string => {
        if (!date) return 'Ongoing';
        return date.toLocaleDateString('en-US', {
            year: 'numeric',
            month: 'long',
            day: 'numeric'
        });
    };

    /**
     * Format currency
     */
    const formatCurrency = (value: number): string => {
        return new Intl.NumberFormat('en-US', {
            style: 'currency',
            currency: 'USD',
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        }).format(value);
    };

    /**
     * Calculate estimated annual investment
     */
    const calculateAnnualAmount = (): number => {
        if (interval === 'ONCE') return amount;

        const multipliers: Record<string, number> = {
            'DAILY': 365,
            'WEEKLY': 52,
            'MONTHLY': 12,
            'YEARLY': 1
        };

        return amount * (multipliers[interval] || 0);
    };

    /**
     * Get color for asset
     */
    const getAssetColor = (ticker: string): string => {
        return AssetColors[ticker] || AssetColors.DEFAULT;
    };

    const annualAmount = calculateAnnualAmount();

    return (
        <Card className="elevation-2" bordered={false}>
            <Space direction="vertical" size="large" style={{ width: '100%' }}>
                {/* Header */}
                <div className="text-center">
                    <Title level={4} className="mb-xs">Subscription Summary</Title>
                    <Text type="secondary">Your investment plan overview</Text>
                </div>

                <Divider style={{ margin: 0 }} />

                {/* Basic Plan Details */}
                <div className="grid-2 gap-md">
                    <div>
                        <Space direction="vertical" size="small">
                            <Text type="secondary" className="text-body-sm">
                                <ClockCircleOutlined /> Plan Type
                            </Text>
                            <Text strong>{formatInterval(interval)}</Text>
                        </Space>
                    </div>
                    <div>
                        <Space direction="vertical" size="small">
                            <Text type="secondary" className="text-body-sm">
                                <DollarOutlined /> Investment Amount
                            </Text>
                            <Text strong className="text-primary">
                                {formatCurrency(amount)} {currency}
                            </Text>
                        </Space>
                    </div>

                    {interval !== 'ONCE' && (
                        <>
                            <div>
                                <Space direction="vertical" size="small">
                                    <Text type="secondary" className="text-body-sm">
                                        <CalendarOutlined /> End Date
                                    </Text>
                                    <Text strong>{formatDate(endDate)}</Text>
                                </Space>
                            </div>
                            <div>
                                <Space direction="vertical" size="small">
                                    <Text type="secondary" className="text-body-sm">
                                        Est. Annual Investment
                                    </Text>
                                    <Text strong className="text-success">
                                        {formatCurrency(annualAmount)} {currency}
                                    </Text>
                                </Space>
                            </div>
                        </>
                    )}
                </div>

                <Divider style={{ margin: 0 }} />

                {/* Asset Allocation */}
                <div>
                    <Text type="secondary" className="text-body-sm mb-sm" style={{ display: 'block' }}>
                        Asset Allocation
                    </Text>

                    {/* Visual Bar */}
                    <div style={{
                        height: '24px',
                        borderRadius: 'var(--radius-lg)',
                        overflow: 'hidden',
                        display: 'flex',
                        marginBottom: 'var(--spacing-md)',
                        boxShadow: 'var(--shadow-sm)'
                    }}>
                        {allocations.map(allocation => (
                            <div
                                key={allocation.assetId}
                                style={{
                                    width: `${allocation.percentAmount}%`,
                                    backgroundColor: getAssetColor(allocation.assetTicker),
                                    transition: 'all 0.3s ease'
                                }}
                                title={`${allocation.assetTicker}: ${allocation.percentAmount}%`}
                            />
                        ))}
                    </div>

                    {/* Allocation List */}
                    <Space direction="vertical" size="small" style={{ width: '100%' }}>
                        {allocations.map(allocation => (
                            <div key={allocation.assetId} className="flex-between">
                                <Space size="small">
                                    <div
                                        style={{
                                            width: '12px',
                                            height: '12px',
                                            borderRadius: '50%',
                                            backgroundColor: getAssetColor(allocation.assetTicker)
                                        }}
                                    />
                                    <Text strong className="text-body-sm">
                                        {allocation.assetName} ({allocation.assetTicker})
                                    </Text>
                                </Space>
                                <Space size="small">
                                    <Tag color="blue" style={{ margin: 0 }}>
                                        {allocation.percentAmount}%
                                    </Tag>
                                    <Text type="secondary" className="text-body-sm">
                                        {formatCurrency((amount * allocation.percentAmount) / 100)}
                                    </Text>
                                </Space>
                            </div>
                        ))}
                    </Space>
                </div>

                <Divider style={{ margin: 0 }} />

                {/* Fee Disclosure */}
                <div>
                    <Text type="secondary" className="text-body-sm mb-xs" style={{ display: 'block' }}>
                        Transaction Fees
                    </Text>
                    <Space direction="vertical" size="small" style={{ width: '100%' }}>
                        <div className="flex-between">
                            <Text className="text-body-sm">Payment processing fee:</Text>
                            <Text strong className="text-body-sm">2.9% + $0.30</Text>
                        </div>
                        <div className="flex-between">
                            <Text className="text-body-sm">Platform fee:</Text>
                            <Text strong className="text-body-sm">1.0%</Text>
                        </div>
                    </Space>
                </div>
            </Space>
        </Card>
    );
};

export default SubscriptionSummary;