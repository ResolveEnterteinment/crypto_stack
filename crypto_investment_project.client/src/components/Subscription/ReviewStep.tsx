// src/components/Subscription/ReviewStep.tsx - Refactored with Global Styling & Ant Design
import React from 'react';
import { Card, Typography, Space, Divider, Tag, Progress, Alert } from 'antd';
import {
    CheckCircleOutlined,
    CalendarOutlined,
    DollarOutlined,
    InfoCircleOutlined,
    CreditCardOutlined
} from '@ant-design/icons';
import { AssetColors } from "../../types/assetTypes";
import { Allocation } from "../../types/subscription";

const { Title, Text, Paragraph } = Typography;

// Define interval display mapping
const INTERVAL_DISPLAY: Record<string, string> = {
    ONCE: 'One-time payment',
    DAILY: 'Daily',
    WEEKLY: 'Weekly',
    MONTHLY: 'Monthly',
    YEARLY: 'Yearly'
};

interface ReviewStepProps {
    formData: {
        interval: string;
        amount: number;
        currency: string;
        endDate: Date | null;
        allocations: Omit<Allocation, 'id'>[];
    };
}

const ReviewStep: React.FC<ReviewStepProps> = ({ formData }) => {
    const { interval, amount, currency, endDate, allocations } = formData;

    /**
     * Format date for display
     */
    const formatDate = (date: Date | null): string => {
        if (!date) return 'Ongoing (until canceled)';
        return new Date(date).toLocaleDateString('en-US', {
            year: 'numeric',
            month: 'long',
            day: 'numeric'
        });
    };

    /**
     * Format currency
     */
    const formatCurrency = (amount: number): string => {
        return new Intl.NumberFormat('en-US', {
            style: 'currency',
            currency: 'USD',
            minimumFractionDigits: 2,
            maximumFractionDigits: 2,
        }).format(amount);
    };

    /**
     * Calculate estimated annual investment
     */
    const calculateAnnualAmount = (): number => {
        if (interval === 'ONCE') return amount;

        const multipliers: Record<string, number> = {
            "DAILY": 365,
            "WEEKLY": 52,
            "MONTHLY": 12,
            "YEARLY": 1
        };

        return amount * (multipliers[interval] || 0);
    };

    // Get estimated annual investment
    const annualAmount = calculateAnnualAmount();

    /**
     * Get color for asset based on ticker
     */
    const getAssetColor = (ticker: string): string => {
        return AssetColors[ticker] || AssetColors.DEFAULT;
    };

    // Calculate fees
    const platformFee = amount * 0.01; // 1% platform fee
    const stripeFee = (amount * 0.029) + 0.30; // 2.9% + $0.30 Stripe fee
    const totalFees = platformFee + stripeFee;
    const netInvestment = amount - totalFees;

    return (
        <div className="stack-lg">
            {/* Header */}
            <div className="text-center">
                <Title level={2} className="mb-sm">
                    Review Your Investment Plan
                </Title>
                <Paragraph className="text-body text-secondary">
                    Please review your investment plan details before confirming.
                </Paragraph>
            </div>

            {/* Plan Details Summary */}
            <Card
                title={
                    <Space>
                        <CalendarOutlined style={{ color: 'var(--color-primary)' }} />
                        <Text strong>Plan Details</Text>
                    </Space>
                }
                className="elevation-2"
            >
                <div className="grid-2 gap-lg">
                    <div>
                        <Text type="secondary" className="text-body-sm">Plan Type</Text>
                        <div className="font-semibold">{INTERVAL_DISPLAY[interval]}</div>
                    </div>
                    <div>
                        <Text type="secondary" className="text-body-sm">Investment Amount</Text>
                        <div className="font-semibold">{formatCurrency(amount)} {currency}</div>
                    </div>

                    {interval !== 'ONCE' && (
                        <>
                            <div>
                                <Text type="secondary" className="text-body-sm">End Date</Text>
                                <div className="font-semibold">{endDate ? formatDate(endDate) : "Until Canceled"}</div>
                            </div>
                            <div>
                                <Text type="secondary" className="text-body-sm">Est. Annual Investment</Text>
                                <div className="font-semibold text-primary">{formatCurrency(annualAmount)} {currency}</div>
                            </div>
                        </>
                    )}

                    <div>
                        <Text type="secondary" className="text-body-sm">Payment Method</Text>
                        <div className="font-semibold">
                            <Space>
                                <CreditCardOutlined />
                                Credit/Debit Card
                            </Space>
                        </div>
                    </div>
                </div>
            </Card>

            {/* Asset Allocation Summary */}
            <Card
                title={
                    <Space>
                        <DollarOutlined style={{ color: 'var(--color-primary)' }} />
                        <Text strong>Asset Allocation</Text>
                    </Space>
                }
                className="elevation-2"
            >
                {/* Visual representation of allocation */}
                <div className="mb-lg">
                    <div style={{
                        height: '32px',
                        borderRadius: 'var(--radius-lg)',
                        overflow: 'hidden',
                        display: 'flex',
                        boxShadow: 'var(--shadow-sm)'
                    }}>
                        {allocations.map((allocation) => (
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
                </div>

                {/* Allocation details */}
                <Space direction="vertical" size="middle" style={{ width: '100%' }}>
                    {allocations.map((allocation) => (
                        <Card
                            key={allocation.assetId}
                            size="small"
                            style={{ backgroundColor: 'var(--color-bg-container)' }}
                        >
                            <div className="flex-between">
                                <Space>
                                    <div
                                        style={{
                                            width: '16px',
                                            height: '16px',
                                            borderRadius: '50%',
                                            backgroundColor: getAssetColor(allocation.assetTicker)
                                        }}
                                    />
                                    <Text strong>
                                        {allocation.assetName} ({allocation.assetTicker})
                                    </Text>
                                </Space>
                                <Space size="large">
                                    <Tag color="blue" style={{ margin: 0 }}>
                                        {allocation.percentAmount}%
                                    </Tag>
                                    <Text type="secondary">
                                        {formatCurrency((amount * allocation.percentAmount) / 100)}
                                    </Text>
                                </Space>
                            </div>
                        </Card>
                    ))}
                </Space>
            </Card>

            {/* Payment & Fee Summary */}
            <Card
                title={
                    <Space>
                        <CheckCircleOutlined style={{ color: 'var(--color-success)' }} />
                        <Text strong>Payment Summary</Text>
                    </Space>
                }
                className="elevation-2"
            >
                <Space direction="vertical" size="small" style={{ width: '100%' }}>
                    <div className="flex-between">
                        <Text>Investment Amount</Text>
                        <Text strong>{formatCurrency(amount)}</Text>
                    </div>
                    <div className="flex-between">
                        <Text type="secondary" className="text-body-sm">Platform Fee (1%)</Text>
                        <Text type="secondary">-{formatCurrency(platformFee)}</Text>
                    </div>
                    <div className="flex-between">
                        <Text type="secondary" className="text-body-sm">Payment Processing Fee (2.9% + $0.30)</Text>
                        <Text type="secondary">-{formatCurrency(stripeFee)}</Text>
                    </div>

                    <Divider style={{ margin: '12px 0' }} />

                    <div className="flex-between">
                        <Text strong style={{ fontSize: '16px' }}>Net Investment Amount</Text>
                        <Text strong className="text-primary" style={{ fontSize: '18px' }}>
                            {formatCurrency(netInvestment)}
                        </Text>
                    </div>
                </Space>
            </Card>

            {/* Terms & Conditions */}
            <Alert
                message={
                    <Paragraph className="m-0 text-body-sm">
                        By proceeding, you agree to our{' '}
                        <a href="/terms" target="_blank" rel="noopener noreferrer">
                            Terms of Service
                        </a>
                        {' '}and{' '}
                        <a href="/privacy" target="_blank" rel="noopener noreferrer">
                            Privacy Policy
                        </a>
                        . You authorize regular charges to your payment method
                        {interval !== 'ONCE' ? ' according to your selected plan' : ''}.
                        {' '}You can cancel anytime.
                    </Paragraph>
                }
                type="info"
                showIcon
                icon={<InfoCircleOutlined />}
            />
        </div>
    );
};

export default ReviewStep;