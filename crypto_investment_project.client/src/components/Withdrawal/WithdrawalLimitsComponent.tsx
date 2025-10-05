import React, { useState, useEffect } from 'react';
import {
    Card, Typography, Row, Col, Progress, Space,
    Spin, Button, Divider
} from 'antd';
import {
    WalletOutlined, ReloadOutlined, CalendarOutlined,
    CheckCircleOutlined, WarningOutlined, TrophyOutlined
} from '@ant-design/icons';
import { WithdrawalLimits } from '../../types/withdrawal';
import withdrawalService from '../../services/withdrawalService';
import { useAuth } from '../../context/AuthContext';
import './WithdrawalLimitsComponent.css';

const { Title, Text } = Typography;

interface WithdrawalLimitsProps {
    userId: string;
    onSuccess: (data: any) => void;
    onError: (error: string) => void;
}

const WithdrawalLimitsComponent: React.FC<WithdrawalLimitsProps> = ({
    onSuccess, onError
}) => {
    const [withdrawalLimits, setWithdrawalLimits] = useState<WithdrawalLimits | null>(null);
    const [loading, setLoading] = useState<boolean>(true);
    const [refreshing, setRefreshing] = useState<boolean>(false);
    const { user } = useAuth();

    const fetchWithdrawalLimits = async (isRefresh = false): Promise<void> => {
        try {
            if (isRefresh) {
                setRefreshing(true);
            } else {
                setLoading(true);
            }

            const userLimits = await withdrawalService.getCurrentUserLimits();
            setWithdrawalLimits(userLimits);

            if (onSuccess) {
                onSuccess(userLimits);
            }
        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : 'Failed to load limits';
            console.error('Error fetching withdrawal limits:', errorMessage);
            if (onError) {
                onError(errorMessage);
            }
        } finally {
            setLoading(false);
            setRefreshing(false);
        }
    };

    useEffect(() => {
        if (user?.id) {
            fetchWithdrawalLimits();
        }
    }, [user]);

    const calculateUsagePercentage = (used: number, limit: number): number => {
        return limit > 0 ? Math.min(Math.round((used / limit) * 100), 100) : 0;
    };

    const getProgressColor = (percentage: number): string => {
        if (percentage >= 90) return '#ff4d4f';
        if (percentage >= 75) return '#faad14';
        return '#52c41a';
    };

    const formatCurrency = (amount: number): string => {
        return new Intl.NumberFormat('en-US', {
            style: 'currency',
            currency: 'USD',
            minimumFractionDigits: 0,
            maximumFractionDigits: 0,
        }).format(amount);
    };

    const formatDate = (dateString: string): string => {
        return new Date(dateString).toLocaleDateString('en-US', {
            month: 'short',
            day: 'numeric',
            year: 'numeric'
        });
    };

    const getKycLevelDisplay = (level: string) => {
        const levels: Record<string, { name: string; color: string }> = {
            'BASIC': { name: 'Basic', color: '#52c41a' },
            'STANDARD': { name: 'Standard', color: '#1890ff' },
            'ADVANCED': { name: 'Advanced', color: '#722ed1' },
            'ENHANCED': { name: 'Enhanced', color: '#faad14' }
        };
        return levels[level?.toUpperCase()] || { name: level || 'Unknown', color: '#d9d9d9' };
    };

    if (loading) {
        return (
            <div className="limits-loading">
                <Spin size="large" />
                <Text type="secondary" style={{ marginTop: 16 }}>Loading limits...</Text>
            </div>
        );
    }

    if (!withdrawalLimits) {
        return (
            <div className="limits-error">
                <WarningOutlined className="error-icon" />
                <Title level={4}>Unable to Load Limits</Title>
                <Text type="secondary">Please try refreshing or contact support.</Text>
                <Button
                    type="primary"
                    onClick={() => fetchWithdrawalLimits()}
                    icon={<ReloadOutlined />}
                    style={{ marginTop: 16 }}
                >
                    Retry
                </Button>
            </div>
        );
    }

    const dailyPercentage = calculateUsagePercentage(withdrawalLimits.dailyUsed, withdrawalLimits.dailyLimit);
    const monthlyPercentage = calculateUsagePercentage(withdrawalLimits.monthlyUsed, withdrawalLimits.monthlyLimit);
    const dailyColor = getProgressColor(dailyPercentage);
    const monthlyColor = getProgressColor(monthlyPercentage);
    const kycLevel = getKycLevelDisplay(withdrawalLimits.kycLevel);

    return (
        <div className="limits-wrapper">
            <Card className="limits-card" bordered={false}>
                {/* Header */}
                <div className="limits-header">
                    <div className="header-content">
                        <div className="header-icon">
                            <TrophyOutlined />
                        </div>
                        <div className="header-text">
                            <div className="header-title-row">
                                <Title level={4}>Your Limits</Title>
                                <Button
                                    type="text"
                                    size="small"
                                    icon={<ReloadOutlined spin={refreshing} />}
                                    onClick={() => fetchWithdrawalLimits(true)}
                                    disabled={refreshing}
                                    className="refresh-button"
                                />
                            </div>
                            <div className="kyc-badge" style={{ borderColor: kycLevel.color }}>
                                <div className="badge-dot" style={{ background: kycLevel.color }} />
                                <Text>{kycLevel.name} Verification</Text>
                            </div>
                        </div>
                    </div>
                </div>

                <Divider style={{ margin: '20px 0' }} />

                {/* Limits Grid */}
                <Row gutter={16}>
                    {/* Daily Limit */}
                    <Col xs={24} md={12}>
                        <div className="limit-card daily-limit">
                            <div className="limit-header">
                                <Text strong className="limit-title">Daily Limit</Text>
                                <Text type="secondary" className="limit-subtitle">
                                    Resets at midnight UTC
                                </Text>
                            </div>

                            <div className="progress-container">
                                <Progress
                                    type="circle"
                                    percent={dailyPercentage}
                                    strokeColor={dailyColor}
                                    strokeWidth={8}
                                    size={100}
                                    format={() => (
                                        <div className="progress-content">
                                            <Text strong className="progress-percent">{dailyPercentage}%</Text>
                                            <Text type="secondary" className="progress-label">used</Text>
                                        </div>
                                    )}
                                />
                            </div>

                            <div className="limit-stats">
                                <div className="stat-row">
                                    <Text type="secondary">Limit</Text>
                                    <Text strong>{formatCurrency(withdrawalLimits.dailyLimit)}</Text>
                                </div>
                                <div className="stat-row">
                                    <Text type="secondary">Used</Text>
                                    <Text style={{ color: dailyPercentage > 75 ? '#faad14' : '#52c41a' }}>
                                        {formatCurrency(withdrawalLimits.dailyUsed)}
                                    </Text>
                                </div>
                                <div className="stat-row highlight">
                                    <Text type="secondary">Available</Text>
                                    <Text strong style={{
                                        color: withdrawalLimits.dailyRemaining < withdrawalLimits.dailyLimit * 0.2
                                            ? '#ff4d4f' : '#52c41a',
                                        fontSize: '16px'
                                    }}>
                                        {formatCurrency(withdrawalLimits.dailyRemaining)}
                                    </Text>
                                </div>
                            </div>
                        </div>
                    </Col>

                    {/* Monthly Limit */}
                    <Col xs={24} md={12}>
                        <div className="limit-card monthly-limit">
                            <div className="limit-header">
                                <Text strong className="limit-title">Monthly Limit</Text>
                                <Text type="secondary" className="limit-subtitle">
                                    Resets on the 1st of each month
                                </Text>
                            </div>

                            <div className="progress-container">
                                <Progress
                                    type="circle"
                                    percent={monthlyPercentage}
                                    strokeColor={monthlyColor}
                                    strokeWidth={8}
                                    size={100}
                                    format={() => (
                                        <div className="progress-content">
                                            <Text strong className="progress-percent">{monthlyPercentage}%</Text>
                                            <Text type="secondary" className="progress-label">used</Text>
                                        </div>
                                    )}
                                />
                            </div>

                            <div className="limit-stats">
                                <div className="stat-row">
                                    <Text type="secondary">Limit</Text>
                                    <Text strong>{formatCurrency(withdrawalLimits.monthlyLimit)}</Text>
                                </div>
                                <div className="stat-row">
                                    <Text type="secondary">Used</Text>
                                    <Text style={{ color: monthlyPercentage > 75 ? '#faad14' : '#52c41a' }}>
                                        {formatCurrency(withdrawalLimits.monthlyUsed)}
                                    </Text>
                                </div>
                                <div className="stat-row highlight">
                                    <Text type="secondary">Available</Text>
                                    <Text strong style={{
                                        color: withdrawalLimits.monthlyRemaining < withdrawalLimits.monthlyLimit * 0.2
                                            ? '#ff4d4f' : '#52c41a',
                                        fontSize: '16px'
                                    }}>
                                        {formatCurrency(withdrawalLimits.monthlyRemaining)}
                                    </Text>
                                </div>
                            </div>
                        </div>
                    </Col>
                </Row>

                {/* Usage Warning */}
                {(dailyPercentage >= 80 || monthlyPercentage >= 80) && (
                    <div className="usage-warning">
                        <WarningOutlined className="warning-icon" />
                        <div className="warning-content">
                            <Text strong>High Usage Warning</Text>
                            <Text type="secondary">
                                {dailyPercentage >= 80 && `You've used ${dailyPercentage}% of your daily limit. `}
                                {monthlyPercentage >= 80 && `You've used ${monthlyPercentage}% of your monthly limit. `}
                                Consider upgrading your verification level for higher limits.
                            </Text>
                        </div>
                    </div>
                )}

                {/* Footer Info */}
                {withdrawalLimits.periodResetDate && (
                    <>
                        <Divider style={{ margin: '20px 0' }} />
                        <div className="limits-footer">
                            <Space align="center">
                                <CalendarOutlined style={{ color: '#8c8c8c' }} />
                                <Text type="secondary">
                                    Limits reset on {formatDate(withdrawalLimits.periodResetDate)}
                                </Text>
                            </Space>
                        </div>
                    </>
                )}
            </Card>
        </div>
    );
};

export default WithdrawalLimitsComponent;