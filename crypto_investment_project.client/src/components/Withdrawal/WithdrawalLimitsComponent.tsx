import React, { useState, useEffect } from 'react';
import { AnimatePresence, motion } from 'framer-motion';
import {
    Card, Typography, Row, Col, Progress, Space, Tag,
    Alert, Spin, Button, Tooltip, Divider
} from 'antd';
import {
    WalletOutlined, ClockCircleOutlined,
    InfoCircleOutlined, ReloadOutlined,
    CalendarOutlined, TrophyOutlined,
    SafetyOutlined, CrownOutlined
} from '@ant-design/icons';
import { WithdrawalLimits } from '../../types/withdrawal';
import withdrawalService from '../../services/withdrawalService';
import { useAuth } from '../../context/AuthContext';

const { Title, Text } = Typography;

interface WithdrawalLimitsProps {
    userId: string;
    onSuccess: (data: any) => void;
    onError: (error: string) => void;
}

// Animation variants
const cardVariants = {
    hidden: { opacity: 0, y: 20, scale: 0.95 },
    visible: {
        opacity: 1,
        y: 0,
        scale: 1,
        transition: {
            duration: 0.5,
            ease: [0.4, 0.0, 0.2, 1]
        }
    }
};

const staggerChildren = {
    hidden: { opacity: 0 },
    visible: {
        opacity: 1,
        transition: {
            staggerChildren: 0.1,
            delayChildren: 0.2
        }
    }
};

const WithdrawalLimitsComponent: React.FC<WithdrawalLimitsProps> = ({
    onSuccess, onError
}) => {
    // State management
    const [withdrawalLimits, setWithdrawalLimits] = useState<WithdrawalLimits | null>(null);
    const [loading, setLoading] = useState<boolean>(true);
    const [refreshing, setRefreshing] = useState<boolean>(false);
    const { user } = useAuth();

    const fetchWithdrawalLimits = async (isRefresh = false): Promise<void> => {
        try {
            setWithdrawalLimits(null);
            if (isRefresh) {
                setRefreshing(true);
            } else {
                setLoading(true);
            }

            const userLimits = await withdrawalService.getLevels();
            setWithdrawalLimits(userLimits);
            console.log("WithdrawalLimitsComponent::fetchWithdrawalLimits => userLimits: ", userLimits);

            if (onSuccess) {
                onSuccess(userLimits);
            }
        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : 'An unknown error occurred';
            console.error('Error fetching withdrawal limits:', errorMessage);
            if (onError) {
                onError(errorMessage);
            }
        } finally {
            setLoading(false);
            setRefreshing(false);
        }
    };

    // Fetch user withdrawal limits
    useEffect(() => {
        if (user?.id) {
            fetchWithdrawalLimits();
        }
    }, [user]);

    // Helper functions
    const calculateUsagePercentage = (used: number, limit: number): number => {
        return limit > 0 ? Math.round((used / limit) * 100) : 0;
    };

    const getUsageStatus = (percentage: number) => {
        if (percentage >= 90) return { color: '#ff4d4f', status: 'exception' as const };
        if (percentage >= 75) return { color: '#faad14', status: 'active' as const };
        return { color: '#52c41a', status: 'success' as const };
    };

    const getKycLevelInfo = (level: string) => {
        const levelMap: Record<string, { icon: React.ReactNode; color: string; name: string }> = {
            'BASIC': {
                icon: <SafetyOutlined />,
                color: '#52c41a',
                name: 'Basic'
            },
            'STANDARD': {
                icon: <TrophyOutlined />,
                color: '#1890ff',
                name: 'Standard'
            },
            'ADVANCED': {
                icon: <CrownOutlined />,
                color: '#722ed1',
                name: 'Advanced'
            },
            'ENHANCED': {
                icon: <CrownOutlined />,
                color: '#faad14',
                name: 'Enhanced'
            }
        };

        return levelMap[level?.toUpperCase()] || {
            icon: <InfoCircleOutlined />,
            color: '#d9d9d9',
            name: level || 'Unknown'
        };
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

    // Loading state
    if (loading || refreshing) {
        return (
            <motion.div
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                style={{
                    minHeight: '200px',
                    display: 'flex',
                    flexDirection: 'column',
                    justifyContent: 'center',
                    alignItems: 'center'
                }}
            >
                <Spin size="large" />
                <Text style={{ marginTop: '16px', color: '#64748b' }}>
                    Loading withdrawal limits...
                </Text>
            </motion.div>
        );
    }

    // No data state
    if (!withdrawalLimits || refreshing) {
        return (
            <AnimatePresence mode="wait">
            <motion.div
                initial={{ opacity: 0, scale: 0.95 }}
                animate={{ opacity: 1, scale: 1 }}
                transition={{ duration: 0.3 }}
            >
                <Alert
                    message="No Withdrawal Limits Available"
                    description="Unable to load your withdrawal limits. Please try refreshing or contact support."
                    type="warning"
                    showIcon
                    style={{ borderRadius: '12px' }}
                    action={
                        <Button
                            size="small"
                            type="primary"
                            onClick={() => fetchWithdrawalLimits()}
                            icon={<ReloadOutlined />}
                        >
                            Retry
                        </Button>
                    }
                />
                </motion.div>
            </AnimatePresence>
        );
    }

    const dailyPercentage = calculateUsagePercentage(withdrawalLimits.dailyUsed, withdrawalLimits.dailyLimit);
    const monthlyPercentage = calculateUsagePercentage(withdrawalLimits.monthlyUsed, withdrawalLimits.monthlyLimit);
    const dailyStatus = getUsageStatus(dailyPercentage);
    const monthlyStatus = getUsageStatus(monthlyPercentage);
    const kycInfo = getKycLevelInfo(withdrawalLimits.kycLevel);

    return (
        <AnimatePresence mode="wait">
        <motion.div
            initial="hidden"
            animate="visible"
            variants={staggerChildren}
        >
            <motion.div variants={cardVariants}>
                <Card
                    style={{
                        background: 'rgba(255, 255, 255, 0.9)',
                        backdropFilter: 'blur(10px)',
                        border: '1px solid rgba(255, 255, 255, 0.2)',
                        borderRadius: '16px',
                        boxShadow: '0 8px 32px rgba(0, 0, 0, 0.1)',
                        marginBottom: '16px'
                    }}
                    bodyStyle={{ padding: '20px' }}
                >
                    {/* Header */}
                    <Row justify="space-between" align="middle" style={{ marginBottom: '20px' }}>
                        <Col>
                            <Space align="center">
                                <div style={{
                                    background: 'linear-gradient(135deg, #1890ff 0%, #722ed1 100%)',
                                    padding: '12px',
                                    borderRadius: '12px',
                                    display: 'flex',
                                    alignItems: 'center',
                                    justifyContent: 'center'
                                }}>
                                    <WalletOutlined style={{ fontSize: '20px', color: 'white' }} />
                                </div>
                                <div>
                                    <Title level={4} style={{ margin: 0 }}>
                                        Withdrawal Limits
                                    </Title>
                                    <Space align="center" style={{ marginTop: '4px' }}>
                                        <Tag
                                            color={kycInfo.color}
                                            icon={kycInfo.icon}
                                            style={{ borderRadius: '12px', padding: '2px 8px' }}
                                        >
                                            {kycInfo.name} Level
                                        </Tag>
                                        {withdrawalLimits.periodResetDate && (
                                            <Tooltip title={`Limits reset on ${formatDate(withdrawalLimits.periodResetDate)}`}>
                                                <CalendarOutlined style={{ color: '#8c8c8c', fontSize: '14px' }} />
                                            </Tooltip>
                                        )}
                                    </Space>
                                </div>
                            </Space>
                        </Col>
                        <Col>
                            <Button
                                type="text"
                                size="small"
                                icon={<ReloadOutlined />}
                                onClick={() => fetchWithdrawalLimits(true)}
                                loading={refreshing}
                                style={{ borderRadius: '8px' }}
                            >
                                Refresh
                            </Button>
                        </Col>
                    </Row>

                    {/* Limits Overview */}
                    <Row gutter={[16, 16]}>
                        {/* Daily Limits */}
                        <Col xs={24} md={12}>
                            <motion.div variants={cardVariants}>
                                <Card
                                    size="small"
                                    style={{
                                        background: 'linear-gradient(135deg, #f0f9ff 0%, #e0f2fe 100%)',
                                        border: '1px solid #bae6fd',
                                        borderRadius: '12px'
                                    }}
                                    bodyStyle={{ padding: '16px' }}
                                >
                                    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
                                        <div style={{ textAlign: 'center' }}>
                                            <Title level={5} style={{ margin: 0, color: '#0369a1' }}>
                                                Daily Limits
                                            </Title>
                                            <Text type="secondary" style={{ fontSize: '12px' }}>
                                                Resets daily at midnight UTC
                                            </Text>
                                        </div>

                                        <div style={{ textAlign: 'center' }}>
                                            <Progress
                                                type="circle"
                                                percent={dailyPercentage}
                                                status={dailyStatus.status}
                                                strokeColor={dailyStatus.color}
                                                size={80}
                                                format={() => `${dailyPercentage}%`}
                                                strokeWidth={8}
                                            />
                                        </div>

                                        <Space direction="vertical" size="small" style={{ width: '100%' }}>
                                            <Row justify="space-between">
                                                <Col>
                                                    <Text type="secondary" style={{ fontSize: '12px' }}>
                                                        Limit:
                                                    </Text>
                                                </Col>
                                                <Col>
                                                    <Text strong style={{ fontSize: '13px' }}>
                                                        {formatCurrency(withdrawalLimits.dailyLimit)}
                                                    </Text>
                                                </Col>
                                            </Row>

                                            <Row justify="space-between">
                                                <Col>
                                                    <Text type="secondary" style={{ fontSize: '12px' }}>
                                                        Used:
                                                    </Text>
                                                </Col>
                                                <Col>
                                                    <Text
                                                        style={{
                                                            fontSize: '13px',
                                                            color: dailyPercentage > 75 ? '#faad14' : '#52c41a',
                                                            fontWeight: 500
                                                        }}
                                                    >
                                                        {formatCurrency(withdrawalLimits.dailyUsed)}
                                                    </Text>
                                                </Col>
                                            </Row>

                                            <Row justify="space-between">
                                                <Col>
                                                    <Text type="secondary" style={{ fontSize: '12px' }}>
                                                        Available:
                                                    </Text>
                                                </Col>
                                                <Col>
                                                    <Text
                                                        strong
                                                        style={{
                                                            fontSize: '14px',
                                                            color: withdrawalLimits.dailyRemaining < withdrawalLimits.dailyLimit * 0.2
                                                                ? '#ff4d4f' : '#52c41a'
                                                        }}
                                                    >
                                                        {formatCurrency(withdrawalLimits.dailyRemaining)}
                                                    </Text>
                                                </Col>
                                            </Row>
                                        </Space>
                                    </Space>
                                </Card>
                            </motion.div>
                        </Col>

                        {/* Monthly Limits */}
                        <Col xs={24} md={12}>
                            <motion.div variants={cardVariants}>
                                <Card
                                    size="small"
                                    style={{
                                        background: 'linear-gradient(135deg, #f0fdf4 0%, #dcfce7 100%)',
                                        border: '1px solid #bbf7d0',
                                        borderRadius: '12px'
                                    }}
                                    bodyStyle={{ padding: '16px' }}
                                >
                                    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
                                        <div style={{ textAlign: 'center' }}>
                                            <Title level={5} style={{ margin: 0, color: '#166534' }}>
                                                Monthly Limits
                                            </Title>
                                            <Text type="secondary" style={{ fontSize: '12px' }}>
                                                Resets on the 1st of each month
                                            </Text>
                                        </div>

                                        <div style={{ textAlign: 'center' }}>
                                            <Progress
                                                type="circle"
                                                percent={monthlyPercentage}
                                                status={monthlyStatus.status}
                                                strokeColor={monthlyStatus.color}
                                                size={80}
                                                format={() => `${monthlyPercentage}%`}
                                                strokeWidth={8}
                                            />
                                        </div>

                                        <Space direction="vertical" size="small" style={{ width: '100%' }}>
                                            <Row justify="space-between">
                                                <Col>
                                                    <Text type="secondary" style={{ fontSize: '12px' }}>
                                                        Limit:
                                                    </Text>
                                                </Col>
                                                <Col>
                                                    <Text strong style={{ fontSize: '13px' }}>
                                                        {formatCurrency(withdrawalLimits.monthlyLimit)}
                                                    </Text>
                                                </Col>
                                            </Row>

                                            <Row justify="space-between">
                                                <Col>
                                                    <Text type="secondary" style={{ fontSize: '12px' }}>
                                                        Used:
                                                    </Text>
                                                </Col>
                                                <Col>
                                                    <Text
                                                        style={{
                                                            fontSize: '13px',
                                                            color: monthlyPercentage > 75 ? '#faad14' : '#52c41a',
                                                            fontWeight: 500
                                                        }}
                                                    >
                                                        {formatCurrency(withdrawalLimits.monthlyUsed)}
                                                    </Text>
                                                </Col>
                                            </Row>

                                            <Row justify="space-between">
                                                <Col>
                                                    <Text type="secondary" style={{ fontSize: '12px' }}>
                                                        Available:
                                                    </Text>
                                                </Col>
                                                <Col>
                                                    <Text
                                                        strong
                                                        style={{
                                                            fontSize: '14px',
                                                            color: withdrawalLimits.monthlyRemaining < withdrawalLimits.monthlyLimit * 0.2
                                                                ? '#ff4d4f' : '#52c41a'
                                                        }}
                                                    >
                                                        {formatCurrency(withdrawalLimits.monthlyRemaining)}
                                                    </Text>
                                                </Col>
                                            </Row>
                                        </Space>
                                    </Space>
                                </Card>
                            </motion.div>
                        </Col>
                    </Row>

                    {/* Usage Warnings */}
                    {(dailyPercentage >= 80 || monthlyPercentage >= 80) && (
                        <motion.div
                            initial={{ opacity: 0, y: 10 }}
                            animate={{ opacity: 1, y: 0 }}
                            transition={{ duration: 0.3, delay: 0.5 }}
                            style={{ marginTop: '16px' }}
                        >
                            <Alert
                                message="High Usage Warning"
                                description={
                                    <Space direction="vertical" size="small">
                                        {dailyPercentage >= 80 && (
                                            <Text>
                                                You've used {dailyPercentage}% of your daily withdrawal limit.
                                            </Text>
                                        )}
                                        {monthlyPercentage >= 80 && (
                                            <Text>
                                                You've used {monthlyPercentage}% of your monthly withdrawal limit.
                                            </Text>
                                        )}
                                        <Text type="secondary" style={{ fontSize: '12px' }}>
                                            Consider upgrading your KYC level for higher limits.
                                        </Text>
                                    </Space>
                                }
                                type="warning"
                                showIcon
                                style={{ borderRadius: '8px' }}
                            />
                        </motion.div>
                    )}

                    {/* Reset Information */}
                    {withdrawalLimits.periodResetDate && (
                        <motion.div
                            initial={{ opacity: 0 }}
                            animate={{ opacity: 1 }}
                            transition={{ duration: 0.3, delay: 0.6 }}
                        >
                            <Divider style={{ margin: '16px 0' }} />

                            <Row justify="center">
                                <Col>
                                    <Space align="center">
                                        <ClockCircleOutlined style={{ color: '#8c8c8c' }} />
                                        <Text type="secondary" style={{ fontSize: '12px' }}>
                                            Limits reset on {formatDate(withdrawalLimits.periodResetDate)}
                                        </Text>
                                    </Space>
                                </Col>
                            </Row>
                        </motion.div>
                    )}
                </Card>
            </motion.div>
            </motion.div>
        </AnimatePresence>
    );
};

export default WithdrawalLimitsComponent;