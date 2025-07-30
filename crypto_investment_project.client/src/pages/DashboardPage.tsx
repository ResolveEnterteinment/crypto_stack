import React, { useEffect, useState, useCallback } from 'react';
import { useAuth } from "../context/AuthContext";
import { useNavigate } from "react-router-dom";
import {
    Layout,
    Typography,
    Card,
    Row,
    Col,
    Button,
    Spin,
    Space,
    Modal,
    Empty,
    Badge,
    notification
} from 'antd';
import {
    ReloadOutlined,
    PlusOutlined,
    DollarOutlined,
    RiseOutlined,
    FallOutlined,
    BarChartOutlined,
    HistoryOutlined
} from '@ant-design/icons';
import Navbar from "../components/Navbar";
import PortfolioChart from "../components/Dashboard/portfolio-chart";
import AssetBalanceCard from "../components/Dashboard/asset-balance-card";
import SubscriptionCard from "../components/Dashboard/subscription-card";
import { getDashboardData } from "../services/dashboard";
import { getSubscriptions, getTransactions, cancelSubscription } from "../services/subscription";
import { Subscription } from "../types/subscription";
import ITransaction from "../interfaces/ITransaction";
import ApiTestPanel from '../components/DevTools/ApiTestPanel';
import { Dashboard } from '../types/dashboardTypes';
import SuccessNotification from '../components/Subscription/PaymentSyncSuccessNotification';

const { Content } = Layout;
const { Title, Text, Paragraph } = Typography;

const DashboardPageContent: React.FC = () => {
    // Get authenticated user and navigation
    const { user } = useAuth();
    const navigate = useNavigate();

    // State management
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [isHistoryModalOpen, setHistoryModalOpen] = useState(false);
    const [currentSubscriptionId, setCurrentSubscriptionId] = useState<string | null>(null);
    const [dashboardData, setDashboardData] = useState<Dashboard | null>(null);
    const [subscriptions, setSubscriptions] = useState<Subscription[]>([]);
    const [transactions, setTransactions] = useState<ITransaction[]>([]);
    const [transactionsLoading, setTransactionsLoading] = useState(false);
    const [refreshing, setRefreshing] = useState(false);
    const [successNotification, setSuccessNotification] = useState<{
        show: boolean;
        message: string;
    }>({ show: false, message: '' });

    // Memoized fetch functions to prevent unnecessary re-renders
    const fetchDashboardData = useCallback(async () => {
        try {
            if (!user?.id) return;
            const data = await getDashboardData(user.id);
            setDashboardData(data);
        } catch (err) {
            console.error('Error fetching dashboard data:', err);
            throw err;
        }
    }, [user?.id]);

    const fetchSubscriptions = useCallback(async () => {
        try {
            if (!user?.id) throw new Error("User ID is missing.");
            const trimmedUserId = user.id.trim();
            const data = await getSubscriptions(trimmedUserId);
            setSubscriptions(data);
        } catch (err) {
            console.error('Error fetching subscriptions:', err);
            throw err;
        }
    }, [user?.id]);

    // Enhanced refresh function with better error handling
    const refreshDashboardData = useCallback(async () => {
        try {
            setRefreshing(true);
            setError(null);

            // Refresh both dashboard data and subscriptions in parallel
            await Promise.all([
                fetchDashboardData(),
                fetchSubscriptions()
            ]);

            console.log('Dashboard data refreshed successfully');
        } catch (err) {
            console.error('Error refreshing dashboard data:', err);
            setError('Failed to refresh dashboard data. Please try again.');

            // Show error notification
            notification.error({
                message: 'Refresh Failed',
                description: 'Failed to refresh dashboard data',
                placement: 'topRight'
            });

            throw err; // Re-throw to allow caller to handle
        } finally {
            setRefreshing(false);
        }
    }, [fetchDashboardData, fetchSubscriptions]);

    // Enhanced data update handler with different success messages
    const handleDataUpdated = useCallback(async (customMessage?: string) => {
        try {
            await refreshDashboardData();

            // Show success notification with custom or default message
            notification.success({
                message: 'Dashboard Updated',
                description: customMessage || 'Dashboard updated with latest information',
                placement: 'topRight'
            });
        } catch (err) {
            // Error is already handled in refreshDashboardData
            console.error('Failed to handle data update:', err);
        }
    }, [refreshDashboardData]);

    // Initial data fetch
    useEffect(() => {
        if (!user || !user.id) {
            // Clear state explicitly upon logout
            setSubscriptions([]);
            setDashboardData(null);
            navigate('/auth');
            return;
        }

        setLoading(true);
        setError(null);

        // Clear previous user's data immediately on user change
        setSubscriptions([]);
        setDashboardData(null);

        Promise.all([
            fetchDashboardData(),
            fetchSubscriptions()
        ])
            .then(() => setLoading(false))
            .catch(err => {
                console.error('Error loading dashboard data:', err);
                setError('Failed to load dashboard data. Please try again.');
                setLoading(false);
            });
    }, [user, fetchDashboardData, fetchSubscriptions, navigate]);

    // Fetch subscription transactions
    const fetchTransactionHistory = useCallback(async (subscriptionId: string) => {
        if (!subscriptionId) {
            console.error('Cannot fetch transaction history: Subscription ID is undefined');
            return;
        }

        try {
            setTransactionsLoading(true);
            const data = await getTransactions(subscriptionId);
            setTransactions(data);
        } catch (err) {
            console.error('Error fetching transactions:', err);
            setTransactions([]);
        } finally {
            setTransactionsLoading(false);
        }
    }, []);

    // Handle edit completion (inline editing via modal)
    const handleEditComplete = useCallback(async (subscriptionId: string) => {
        console.log('Edit completed for subscription:', subscriptionId);

        // Refresh data after successful edit
        await handleDataUpdated('Subscription updated successfully');
    }, [handleDataUpdated]);

    const handleCancelSubscription = useCallback(async (id: string) => {
        try {
            await cancelSubscription(id);

            // Refresh all data with custom success message
            await handleDataUpdated('Subscription cancelled successfully');

        } catch (err) {
            console.error('Error cancelling subscription:', err);
            notification.error({
                message: 'Cancellation Failed',
                description: 'Failed to cancel subscription. Please try again.',
                placement: 'topRight'
            });
        }
    }, [handleDataUpdated]);

    const handleViewHistory = useCallback(async (id: string) => {
        if (!id) {
            console.error("Cannot view history: Subscription ID is missing");
            return;
        }

        setCurrentSubscriptionId(id);
        setHistoryModalOpen(true);
        await fetchTransactionHistory(id);
    }, [fetchTransactionHistory]);

    // Hide notification handler
    const handleHideNotification = useCallback(() => {
        setSuccessNotification({ show: false, message: '' });
    }, []);

    // Calculate profit/loss percentage
    const calculateProfitPercentage = useCallback(() => {
        if (!dashboardData) return 0;

        const { totalInvestments, portfolioValue } = dashboardData;
        if (!totalInvestments || totalInvestments === 0) return 0;

        return ((portfolioValue - totalInvestments) / totalInvestments) * 100;
    }, [dashboardData]);

    const profitPercentage = calculateProfitPercentage();
    const isProfitable = profitPercentage >= 0;

    // Find current subscription details for the modal
    const currentSubscription = subscriptions.find(sub => sub.id === currentSubscriptionId);

    // Close transaction modal handler
    const handleCloseTransactionModal = useCallback(() => {
        setHistoryModalOpen(false);
        setTransactions([]);
        setCurrentSubscriptionId(null);
    }, []);

    // Manual refresh handler for retry button
    const handleManualRefresh = useCallback(async () => {
        await handleDataUpdated('Dashboard data refreshed successfully');
    }, [handleDataUpdated]);

    if (loading) {
        return (
            <div style={{
                minHeight: '100vh',
                background: 'linear-gradient(135deg, #f8fafc 0%, #e1f5fe 50%, #e8eaf6 100%)',
                display: 'flex',
                justifyContent: 'center',
                alignItems: 'center'
            }}>
                <Card
                    style={{
                        textAlign: 'center',
                        background: 'rgba(255, 255, 255, 0.9)',
                        backdropFilter: 'blur(10px)',
                        border: '1px solid rgba(255, 255, 255, 0.2)',
                        borderRadius: '16px',
                        boxShadow: '0 20px 40px rgba(0, 0, 0, 0.1)'
                    }}
                >
                    <Spin size="large" />
                    <Title level={4} style={{ marginTop: 16, marginBottom: 8 }}>
                        Loading Dashboard
                    </Title>
                    <Text type="secondary">Preparing your investment overview...</Text>
                </Card>
            </div>
        );
    }

    if (error) {
        return (
            <div style={{
                minHeight: '100vh',
                background: 'linear-gradient(135deg, #f8fafc 0%, #ffebee 50%, #fce4ec 100%)',
                display: 'flex',
                justifyContent: 'center',
                alignItems: 'center',
                padding: '16px'
            }}>
                <Card
                    style={{
                        maxWidth: '512px',
                        width: '100%',
                        textAlign: 'center',
                        background: 'rgba(255, 255, 255, 0.95)',
                        backdropFilter: 'blur(10px)',
                        border: '1px solid rgba(244, 67, 54, 0.1)',
                        borderRadius: '16px',
                        boxShadow: '0 20px 40px rgba(244, 67, 54, 0.1)'
                    }}
                >
                    <div style={{ fontSize: '48px', color: '#f44336', marginBottom: '24px' }}>
                        ⚠️
                    </div>
                    <Title level={3} style={{ marginBottom: '16px' }}>
                        Unable to Load Dashboard
                    </Title>
                    <Paragraph style={{ marginBottom: '32px', lineHeight: 1.6 }}>
                        {error}
                    </Paragraph>
                    <Space>
                        <Button
                            type="primary"
                            size="large"
                            loading={refreshing}
                            onClick={handleManualRefresh}
                            icon={<ReloadOutlined />}
                        >
                            {refreshing ? 'Retrying...' : 'Try Again'}
                        </Button>
                        <Button
                            size="large"
                            onClick={() => window.location.reload()}
                        >
                            Reload Page
                        </Button>
                    </Space>
                </Card>
            </div>
        );
    }

    return (
        <div style={{
            minHeight: '100vh',
            background: 'linear-gradient(135deg, #f8fafc 0%, #e1f5fe 50%, #e8eaf6 100%)',
            position: 'relative',
            paddingTop: '40px'
        }}>
            {/* Background Elements */}
            <div style={{
                position: 'absolute',
                inset: 0,
                overflow: 'hidden',
                pointerEvents: 'none',
                zIndex: 0
            }}>
                <div style={{
                    position: 'absolute',
                    top: '-160px',
                    right: '-160px',
                    width: '320px',
                    height: '320px',
                    background: 'linear-gradient(135deg, rgba(33, 150, 243, 0.2) 0%, rgba(63, 81, 181, 0.2) 100%)',
                    borderRadius: '50%',
                    filter: 'blur(40px)'
                }} />
                <div style={{
                    position: 'absolute',
                    bottom: '-160px',
                    left: '-160px',
                    width: '320px',
                    height: '320px',
                    background: 'linear-gradient(135deg, rgba(156, 39, 176, 0.2) 0%, rgba(233, 30, 99, 0.2) 100%)',
                    borderRadius: '50%',
                    filter: 'blur(40px)'
                }} />
            </div>

            <Layout style={{ background: 'transparent', position: 'relative', zIndex: 1 }}>
                <Content style={{ padding: '32px 40px 50px' }}>
                    {/* Refreshing Indicator */}
                    {refreshing && (
                        <div style={{
                            position: 'fixed',
                            top: '80px',
                            right: '16px',
                            zIndex: 1000
                        }}>
                            <Card
                                size="small"
                                style={{
                                    background: 'rgba(255, 255, 255, 0.95)',
                                    backdropFilter: 'blur(10px)',
                                    border: '1px solid rgba(33, 150, 243, 0.2)',
                                    borderRadius: '12px',
                                    boxShadow: '0 8px 32px rgba(0, 0, 0, 0.1)'
                                }}
                            >
                                <Space>
                                    <Spin size="small" />
                                    <Text style={{ fontSize: '14px', fontWeight: 500, color: '#1976d2' }}>
                                        Refreshing dashboard...
                                    </Text>
                                </Space>
                            </Card>
                        </div>
                    )}

                    {/* Header Section */}
                    <div style={{ marginBottom: '32px' }}>
                        <Row justify="space-between" align="middle">
                            <Col>
                                <Title
                                    level={1}
                                    style={{
                                        background: 'linear-gradient(135deg, #424242 0%, #616161 100%)',
                                        WebkitBackgroundClip: 'text',
                                        WebkitTextFillColor: 'transparent',
                                        marginBottom: '8px'
                                    }}
                                >
                                    Investment Dashboard
                                </Title>
                                <Text type="secondary" style={{ fontSize: '16px' }}>
                                    Welcome back, {user?.email}. Here's your portfolio overview.
                                </Text>
                            </Col>
                            <Col>
                                <div style={{ textAlign: 'right' }}>
                                    <Text type="secondary" style={{ fontSize: '12px', display: 'block' }}>
                                        Last updated
                                    </Text>
                                    <Text style={{ fontSize: '14px', fontWeight: 500 }}>
                                        {new Date().toLocaleDateString()}
                                    </Text>
                                </div>
                            </Col>
                        </Row>
                    </div>

                    {/* Enhanced Summary Cards */}
                    <Row gutter={[24, 24]} style={{ marginBottom: '32px' }}>
                        {/* Total Investment Card */}
                        <Col xs={24} md={8}>
                            <Card
                                hoverable
                                style={{
                                    background: 'rgba(255, 255, 255, 0.9)',
                                    backdropFilter: 'blur(10px)',
                                    border: '1px solid rgba(255, 255, 255, 0.2)',
                                    borderRadius: '16px',
                                    boxShadow: '0 8px 32px rgba(0, 0, 0, 0.1)',
                                    transition: 'all 0.3s ease'
                                }}
                                bodyStyle={{ padding: '24px' }}
                            >
                                <Row justify="space-between" align="top">
                                    <Col flex="1">
                                        <Text type="secondary" style={{ fontWeight: 500 }}>
                                            Total Investment
                                        </Text>
                                        <Title level={2} style={{ margin: '8px 0 4px', color: '#424242' }}>
                                            ${dashboardData?.totalInvestments.toFixed(2) || '0.00'}
                                        </Title>
                                        <Space size="small">
                                            <RiseOutlined style={{ color: '#666' }} />
                                            <Text type="secondary" style={{ fontSize: '14px' }}>
                                                Your total investments
                                            </Text>
                                        </Space>
                                    </Col>
                                    <Col>
                                        <div style={{
                                            background: 'linear-gradient(135deg, #2196f3 0%, #3f51b5 100%)',
                                            padding: '12px',
                                            borderRadius: '12px',
                                            boxShadow: '0 4px 12px rgba(33, 150, 243, 0.3)'
                                        }}>
                                            <DollarOutlined style={{ fontSize: '24px', color: 'white' }} />
                                        </div>
                                    </Col>
                                </Row>
                            </Card>
                        </Col>

                        {/* Portfolio Value Card */}
                        <Col xs={24} md={8}>
                            <Card
                                hoverable
                                style={{
                                    background: 'rgba(255, 255, 255, 0.9)',
                                    backdropFilter: 'blur(10px)',
                                    border: '1px solid rgba(255, 255, 255, 0.2)',
                                    borderRadius: '16px',
                                    boxShadow: '0 8px 32px rgba(0, 0, 0, 0.1)',
                                    transition: 'all 0.3s ease'
                                }}
                                bodyStyle={{ padding: '24px' }}
                            >
                                <Row justify="space-between" align="top">
                                    <Col flex="1">
                                        <Text type="secondary" style={{ fontWeight: 500 }}>
                                            Portfolio Value
                                        </Text>
                                        <Title level={2} style={{ margin: '8px 0 4px', color: '#424242' }}>
                                            ${dashboardData?.portfolioValue.toFixed(2) || '0.00'}
                                        </Title>
                                        <Space size="small">
                                            <BarChartOutlined style={{ color: '#666' }} />
                                            <Text type="secondary" style={{ fontSize: '14px' }}>
                                                Current market value
                                            </Text>
                                        </Space>
                                    </Col>
                                    <Col>
                                        <div style={{
                                            background: 'linear-gradient(135deg, #4caf50 0%, #00bcd4 100%)',
                                            padding: '12px',
                                            borderRadius: '12px',
                                            boxShadow: '0 4px 12px rgba(76, 175, 80, 0.3)'
                                        }}>
                                            <RiseOutlined style={{ fontSize: '24px', color: 'white' }} />
                                        </div>
                                    </Col>
                                </Row>
                            </Card>
                        </Col>

                        {/* Profit/Loss Card */}
                        <Col xs={24} md={8}>
                            <Card
                                hoverable
                                style={{
                                    background: 'rgba(255, 255, 255, 0.9)',
                                    backdropFilter: 'blur(10px)',
                                    border: '1px solid rgba(255, 255, 255, 0.2)',
                                    borderRadius: '16px',
                                    boxShadow: '0 8px 32px rgba(0, 0, 0, 0.1)',
                                    transition: 'all 0.3s ease'
                                }}
                                bodyStyle={{ padding: '24px' }}
                            >
                                <Row justify="space-between" align="top">
                                    <Col flex="1">
                                        <Text type="secondary" style={{ fontWeight: 500 }}>
                                            Profit/Loss
                                        </Text>
                                        <Title
                                            level={2}
                                            style={{
                                                margin: '8px 0 4px',
                                                color: isProfitable ? '#4caf50' : '#f44336'
                                            }}
                                        >
                                            {isProfitable ? '+' : ''}{profitPercentage.toFixed(2)}%
                                        </Title>
                                        <Space size="small">
                                            {isProfitable ? (
                                                <RiseOutlined style={{ color: '#4caf50' }} />
                                            ) : (
                                                <FallOutlined style={{ color: '#f44336' }} />
                                            )}
                                            <Text type="secondary" style={{ fontSize: '14px' }}>
                                                ${dashboardData ? Math.abs(dashboardData.portfolioValue - dashboardData.totalInvestments).toFixed(2) : '0.00'}
                                            </Text>
                                        </Space>
                                    </Col>
                                    <Col>
                                        <div style={{
                                            background: isProfitable ?
                                                'linear-gradient(135deg, #4caf50 0%, #00bcd4 100%)' :
                                                'linear-gradient(135deg, #f44336 0%, #e91e63 100%)',
                                            padding: '12px',
                                            borderRadius: '12px',
                                            boxShadow: `0 4px 12px ${isProfitable ? 'rgba(76, 175, 80, 0.3)' : 'rgba(244, 67, 54, 0.3)'}`
                                        }}>
                                            {isProfitable ? (
                                                <RiseOutlined style={{ fontSize: '24px', color: 'white' }} />
                                            ) : (
                                                <FallOutlined style={{ fontSize: '24px', color: 'white' }} />
                                            )}
                                        </div>
                                    </Col>
                                </Row>
                            </Card>
                        </Col>
                    </Row>

                    {/* Enhanced Chart & Assets Section */}
                    <Row gutter={[24, 24]} style={{ marginBottom: '32px' }}>
                        {/* Chart */}
                        <Col xs={24} lg={16}>
                            <Card
                                hoverable
                                style={{
                                    background: 'rgba(255, 255, 255, 0.9)',
                                    backdropFilter: 'blur(10px)',
                                    border: '1px solid rgba(255, 255, 255, 0.2)',
                                    borderRadius: '16px',
                                    boxShadow: '0 8px 32px rgba(0, 0, 0, 0.1)',
                                    transition: 'all 0.3s ease'
                                }}
                            >
                                <Row justify="space-between" align="middle" style={{ marginBottom: '24px' }}>
                                    <Col>
                                        <Title level={4} style={{ margin: 0 }}>
                                            Investment vs Portfolio Value
                                        </Title>
                                    </Col>
                                    <Col>
                                        <Space size="small">
                                            <Badge status="processing" />
                                            <Text type="secondary" style={{ fontSize: '14px' }}>
                                                Live Data
                                            </Text>
                                        </Space>
                                    </Col>
                                </Row>
                                <PortfolioChart
                                    investmentData={dashboardData?.totalInvestments || 0}
                                    portfolioData={dashboardData?.portfolioValue || 0}
                                />
                            </Card>
                        </Col>

                        {/* Asset Balance */}
                        <Col xs={24} lg={8}>
                            <AssetBalanceCard assetHoldings={dashboardData?.assetHoldings!} />
                        </Col>
                    </Row>

                    {/* Enhanced Subscriptions Section */}
                    <Card
                        style={{
                            background: 'rgba(255, 255, 255, 0.7)',
                            backdropFilter: 'blur(10px)',
                            border: '1px solid rgba(255, 255, 255, 0.2)',
                            borderRadius: '16px',
                            marginBottom: '32px'
                        }}
                    >
                        <Row justify="space-between" align="middle" style={{ marginBottom: '24px' }}>
                            <Col>
                                <Title level={3} style={{ marginBottom: '8px' }}>
                                    Active Subscriptions
                                </Title>
                                <Text type="secondary" style={{ fontSize: '16px' }}>
                                    Manage your investment subscriptions
                                </Text>
                            </Col>
                            <Col>
                                <Space>
                                    <Button
                                        loading={refreshing}
                                        onClick={handleManualRefresh}
                                        icon={<ReloadOutlined />}
                                    >
                                        {refreshing ? 'Refreshing' : 'Refresh'}
                                    </Button>
                                    <Button
                                        type="primary"
                                        onClick={() => navigate('/subscription/new')}
                                        icon={<PlusOutlined />}
                                    >
                                        New Subscription
                                    </Button>
                                </Space>
                            </Col>
                        </Row>

                        {subscriptions.length > 0 ? (
                            <Row gutter={[24, 24]}>
                                {subscriptions.map((subscription) => (
                                    <Col xs={24} md={12} lg={8} key={subscription.id}>
                                        <SubscriptionCard
                                            subscription={subscription}
                                            onEdit={handleEditComplete}
                                            onCancel={handleCancelSubscription}
                                            onViewHistory={handleViewHistory}
                                            onDataUpdated={() => handleDataUpdated()}
                                        />
                                    </Col>
                                ))}
                            </Row>
                        ) : (
                            <Empty
                                image={Empty.PRESENTED_IMAGE_SIMPLE}
                                description={
                                    <div>
                                        <Title level={4} style={{ marginBottom: '12px' }}>
                                            Start Your Investment Journey
                                        </Title>
                                        <Paragraph style={{ maxWidth: '400px', margin: '0 auto 32px' }}>
                                            You haven't created any subscriptions yet. Create your first
                                            subscription to begin building your investment portfolio.
                                        </Paragraph>
                                        <Button
                                            type="primary"
                                            size="large"
                                            onClick={() => navigate('/subscription/new')}
                                            icon={<PlusOutlined />}
                                        >
                                            Create Your First Subscription
                                        </Button>
                                    </div>
                                }
                            />
                        )}
                    </Card>

                    {/* Enhanced Transaction History Modal */}
                    <Modal
                        title={
                            <Space>
                                <HistoryOutlined />
                                <span>Transaction History</span>
                                {currentSubscription && (
                                    <Text type="secondary" style={{ fontSize: '14px' }}>
                                        - ${currentSubscription.amount} {currentSubscription.interval.toLowerCase()} plan
                                    </Text>
                                )}
                            </Space>
                        }
                        open={isHistoryModalOpen}
                        onCancel={handleCloseTransactionModal}
                        width={800}
                        footer={[
                            <Button key="close" type="primary" onClick={handleCloseTransactionModal}>
                                Close
                            </Button>
                        ]}
                        styles={{
                            body: { maxHeight: '60vh', overflowY: 'auto' }
                        }}
                    >
                        {transactionsLoading ? (
                            <div style={{ textAlign: 'center', padding: '40px' }}>
                                <Spin size="large" />
                            </div>
                        ) : transactions.length > 0 ? (
                            <Space direction="vertical" size="middle" style={{ width: '100%' }}>
                                {transactions.map((item, index) => (
                                    <Card
                                        key={index}
                                        size="small"
                                        style={{
                                            background: 'linear-gradient(135deg, #f8f9fa 0%, #f1f3f4 100%)',
                                            border: '1px solid #e0e0e0',
                                            borderRadius: '8px'
                                        }}
                                    >
                                        <Row justify="space-between" align="middle">
                                            <Col>
                                                <Space>
                                                    <div style={{
                                                        background: 'linear-gradient(135deg, #4caf50 0%, #00bcd4 100%)',
                                                        width: '40px',
                                                        height: '40px',
                                                        borderRadius: '12px',
                                                        display: 'flex',
                                                        alignItems: 'center',
                                                        justifyContent: 'center'
                                                    }}>
                                                        <RiseOutlined style={{ fontSize: '20px', color: 'white' }} />
                                                    </div>
                                                    <div>
                                                        <Text strong style={{ display: 'block' }}>
                                                            {item.assetName} {item.action}
                                                        </Text>
                                                        <Text type="secondary" style={{ fontSize: '12px' }}>
                                                            {new Date(item.createdAt).toLocaleDateString('en-US', {
                                                                year: 'numeric',
                                                                month: 'short',
                                                                day: 'numeric',
                                                                hour: '2-digit',
                                                                minute: '2-digit'
                                                            })}
                                                        </Text>
                                                    </div>
                                                </Space>
                                            </Col>
                                            <Col style={{ textAlign: 'right' }}>
                                                <Text strong style={{ display: 'block' }}>
                                                    +{item.quantity.toFixed(6)} {item.assetTicker}
                                                </Text>
                                                <Text type="secondary" style={{ fontSize: '12px' }}>
                                                    {item.quoteCurrency} {item.quoteQuantity.toFixed(2)}
                                                </Text>
                                            </Col>
                                        </Row>
                                    </Card>
                                ))}
                            </Space>
                        ) : (
                            <Empty
                                description={
                                    <div>
                                        <Title level={5}>No Transactions Found</Title>
                                        <Text type="secondary">
                                            No transaction history found for this subscription
                                        </Text>
                                    </div>
                                }
                            />
                        )}
                    </Modal>

                    {/* Success/Error Notification */}
                    <SuccessNotification
                        show={successNotification.show}
                        message={successNotification.message}
                        onClose={handleHideNotification}
                    />
                </Content>
            </Layout>
        </div>
    );
};

const DashboardPage: React.FC = () => {
    return (
        <>
            <Navbar />
            <div className="dashboard-page">
                <DashboardPageContent />
            </div>
            <ApiTestPanel />
        </>
    );
}

export default DashboardPage;