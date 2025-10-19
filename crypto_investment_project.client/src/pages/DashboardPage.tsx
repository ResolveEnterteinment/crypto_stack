import {
    BarChartOutlined,
    DollarOutlined,
    DollarCircleOutlined,
    FallOutlined,
    HistoryOutlined,
    PlusOutlined,
    ReloadOutlined,
    RiseOutlined
} from '@ant-design/icons';
import {
    Badge,
    Button,
    Card,
    Empty,
    Layout,
    Modal,
    notification,
    Space,
    Spin,
    Typography
} from 'antd';
import React, { useCallback, useEffect, useState } from 'react';
import { useNavigate } from "react-router-dom";
import AssetBalanceCard from "../components/Dashboard/asset-balance-card";
import PortfolioChart from "../components/Dashboard/portfolio-chart";
import SubscriptionCard from "../components/Dashboard/subscription-card";
import ErrorBoundary from '../components/ErrorBoundary';
import Navbar from "../components/Navbar";
import SuccessNotification from '../components/Subscription/PaymentSyncSuccessNotification';
import { useAuth } from "../context/AuthContext";
import { useDashboardSignalR } from "../hooks/useDashboardSignalR";
import ITransaction from "../interfaces/ITransaction";
import { getDashboardData } from "../services/dashboard";
import { cancelSubscription, getSubscriptions } from "../services/subscription";
import { getBySubscription as getTransactionsBySubscription } from "../services/transactionService";
import styles from '../styles/Dashboard/DashboardPage.module.css';
import { Dashboard } from '../types/dashboardTypes';
import { Subscription } from "../types/subscription";

const { Content } = Layout;
const { Title, Text, Paragraph } = Typography;

const DashboardPageContent: React.FC = () => {
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

    // SignalR integration
    const handleSignalRDashboardUpdate = useCallback(async (newDashboardData: Dashboard) => {
        console.log('Received real-time dashboard update:', newDashboardData);
        setDashboardData(newDashboardData);
        await fetchSubscriptions();

        notification.info({
            message: 'Dashboard Updated',
            description: 'Your dashboard has been updated',
            placement: 'topRight',
            duration: 3
        });
    }, []);

    const handleSignalRError = useCallback((error: string) => {
        console.error('SignalR dashboard error:', error);
        notification.error({
            message: 'Real-time Connection Error',
            description: error,
            placement: 'topRight'
        });
    }, []);

    const { refreshDashboard: signalRRefresh, isConnected } = useDashboardSignalR(
        handleSignalRDashboardUpdate,
        handleSignalRError
    );

    // Fetch functions
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

    const formatCurrency = (amount: number): string => {
        return new Intl.NumberFormat('en-US', {
            style: 'currency',
            currency: 'USD',
            minimumFractionDigits: 2,
            maximumFractionDigits: 2,
        }).format(amount);
    };

    const formatPercent = (amount: number): string => {
        return new Intl.NumberFormat('en-US', {
            style: 'percent',
            minimumFractionDigits: 2,
            maximumFractionDigits: 2,
        }).format(amount);
    };

    const refreshDashboardData = useCallback(async () => {
        try {
            setRefreshing(true);
            setError(null);
            await Promise.all([fetchDashboardData(), fetchSubscriptions()]);
        } catch (err) {
            console.error('Error refreshing dashboard data:', err);
            setError('Failed to refresh dashboard data. Please try again.');
            notification.error({
                message: 'Refresh Failed',
                description: 'Failed to refresh dashboard data',
                placement: 'topRight'
            });
            throw err;
        } finally {
            setRefreshing(false);
        }
    }, [fetchDashboardData, fetchSubscriptions]);

    const handleDataUpdated = useCallback(async (customMessage?: string) => {
        try {
            await refreshDashboardData();
            notification.success({
                message: 'Dashboard Updated',
                description: customMessage || 'Dashboard updated with latest information',
                placement: 'topRight'
            });
        } catch (err) {
            console.error('Failed to handle data update:', err);
        }
    }, [refreshDashboardData]);

    useEffect(() => {
        if (!user || !user.id) {
            setSubscriptions([]);
            setDashboardData(null);
            navigate('/auth');
            return;
        }

        setLoading(true);
        setError(null);
        setSubscriptions([]);
        setDashboardData(null);

        Promise.all([fetchDashboardData(), fetchSubscriptions()])
            .then(() => setLoading(false))
            .catch(err => {
                console.error('Error loading dashboard data:', err);
                setError('Failed to load dashboard data. Please try again.');
                setLoading(false);
            });
    }, [user, fetchDashboardData, fetchSubscriptions, navigate]);

    const fetchTransactionHistory = useCallback(async (subscriptionId: string) => {
        if (!subscriptionId) {
            console.error('Cannot fetch transaction history: Subscription ID is undefined');
            return;
        }

        try {
            setTransactionsLoading(true);
            const data = await getTransactionsBySubscription(subscriptionId);
            setTransactions(data);
        } catch (err) {
            console.error('Error fetching transactions:', err);
            setTransactions([]);
        } finally {
            setTransactionsLoading(false);
        }
    }, []);

    const handleEditComplete = useCallback(async (subscriptionId: string) => {
        console.log('Edit completed for subscription:', subscriptionId);
        await handleDataUpdated('Subscription updated successfully');
    }, [handleDataUpdated]);

    const handleCancelSubscription = useCallback(async (id: string) => {
        try {
            await cancelSubscription(id);
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

    const handleHideNotification = useCallback(() => {
        setSuccessNotification({ show: false, message: '' });
    }, []);

    const calculateProfitPercentage = useCallback(() => {
        if (!dashboardData) return 0;
        const { totalInvestments, portfolioValue } = dashboardData;
        if (!totalInvestments || totalInvestments === 0) return 0;
        return ((portfolioValue - totalInvestments) / totalInvestments);
    }, [dashboardData]);

    const profitPercentage = calculateProfitPercentage();
    const isProfitable = profitPercentage >= 0;
    const currentSubscription = subscriptions.find(sub => sub.id === currentSubscriptionId);

    const handleCloseTransactionModal = useCallback(() => {
        setHistoryModalOpen(false);
        setTransactions([]);
        setCurrentSubscriptionId(null);
    }, []);

    const handleManualRefresh = useCallback(async () => {
        try {
            await Promise.all([
                handleDataUpdated('Dashboard data refreshed successfully'),
                signalRRefresh()
            ]);
        } catch (error) {
            console.error('Error during manual refresh:', error);
        }
    }, [handleDataUpdated, signalRRefresh]);

    if (loading) {
        return (
            <div className={styles.loadingContainer}>
                <Card className={styles.loadingCard}>
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
            <div className={styles.errorContainer}>
                <Card className={styles.errorCard}>
                    <div className={styles.errorIcon}>⚠️</div>
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
                        <Button size="large" onClick={() => window.location.reload()}>
                            Reload Page
                        </Button>
                    </Space>
                </Card>
            </div>
        );
    }

    return (
        <div className={styles.dashboardPage}>
            <div className={styles.backgroundElements}>
                <div className={`${styles.backgroundBlob} ${styles.backgroundBlobTop}`} />
                <div className={`${styles.backgroundBlob} ${styles.backgroundBlobBottom}`} />
            </div>

            <Layout style={{ background: 'transparent', position: 'relative', zIndex: 1 }}>
                <Content className={styles.content}>
                    {refreshing && (
                        <div className={styles.refreshingIndicator}>
                            <Card size="small" className={styles.refreshingCard}>
                                <Space>
                                    <Spin size="small" />
                                    <Text style={{ fontSize: '14px', fontWeight: 500, color: '#1976d2' }}>
                                        Refreshing dashboard...
                                    </Text>
                                </Space>
                            </Card>
                        </div>
                    )}

                    <div className={styles.header}>
                        <div className={styles.headerRow}>
                            <div>
                                <Text className={styles.headerTitle}>Dashboard</Text>
                            </div>
                            <div className={styles.headerInfo}>
                                <Text className={styles.lastUpdatedLabel}>Last updated</Text>
                                <Text className={styles.lastUpdatedTime}>{new Date().toLocaleDateString()}</Text>
                            </div>
                        </div>
                    </div>

                    <div className={`${styles.gridRow} ${styles.summaryGrid}`}>
                        <Card hoverable className={styles.summaryCard}>
                            <div className={styles.summaryCardHeader}>
                                <div className={styles.summaryCardContent}>
                                    <Text className={styles.summaryCardLabel}>Total Investment</Text>
                                    <Title level={2} className={styles.summaryCardValue}>
                                        {dashboardData && formatCurrency(dashboardData.totalInvestments) || '0.00'}
                                    </Title>
                                    <div className={styles.summaryCardSubtext}>
                                        <RiseOutlined />
                                        <Text>Your total investments</Text>
                                    </div>
                                </div>
                                <div className={styles.summaryCardIcon}>
                                    <DollarOutlined />
                                </div>
                            </div>
                        </Card>

                        <Card hoverable className={styles.summaryCard}>
                            <div className={styles.summaryCardHeader}>
                                <div className={styles.summaryCardContent}>
                                    <Text className={styles.summaryCardLabel}>Portfolio Value</Text>
                                    <Title level={2} className={styles.summaryCardValue}>
                                        {dashboardData && formatCurrency(dashboardData.portfolioValue)}
                                    </Title>
                                    <div className={styles.summaryCardSubtext}>
                                        <BarChartOutlined />
                                        <Text>Current market value</Text>
                                    </div>
                                </div>
                                <div className={`${styles.summaryCardIcon} ${styles.summaryCardIconGreen}`}>
                                    <RiseOutlined />
                                </div>
                            </div>
                        </Card>

                        <Card hoverable className={styles.summaryCard}>
                            <div className={styles.summaryCardHeader}>
                                <div className={styles.summaryCardContent}>
                                    <Text className={styles.summaryCardLabel}>Profit/Loss</Text>
                                    <Title
                                        level={2}
                                        className={`${styles.summaryCardValue} ${isProfitable ? styles.profitValue : styles.lossValue}`}
                                    >
                                        <div className={isProfitable ? styles.profitValue : styles.lossValue}>{isProfitable ? '+' : ''}{formatPercent(profitPercentage)}</div>
                                    </Title>
                                    <div className={styles.summaryCardSubtext}>
                                        {isProfitable ? <RiseOutlined className={styles.summaryCardIconProfit} /> : <FallOutlined className={styles.summaryCardIconLoss} />}
                                        <Text>
                                            {dashboardData && formatCurrency(Math.abs(dashboardData.portfolioValue - dashboardData.totalInvestments))}
                                        </Text>
                                    </div>
                                </div>
                                <div className={`${styles.summaryCardIcon} ${isProfitable ? styles.summaryCardIconGreen : styles.summaryCardIconRed}`}>
                                    {isProfitable ? <RiseOutlined /> : <FallOutlined />}
                                </div>
                            </div>
                        </Card>
                    </div>

                    <div className={`${styles.gridRow} ${styles.chartAssetsGrid}`}>
                        <Card hoverable className={styles.chartCard}>
                            <div className={styles.chartHeader}>
                                <Title level={4} className={styles.chartTitle}>
                                    Investment vs Portfolio Value
                                </Title>
                                <div className={styles.liveDataBadge}>
                                    <Badge status="processing" />
                                    <Text>Live Data</Text>
                                </div>
                            </div>
                            <PortfolioChart
                                investmentData={dashboardData?.totalInvestments || 0}
                                portfolioData={dashboardData?.portfolioValue || 0}
                            />
                        </Card>
                        <AssetBalanceCard assetHoldings={dashboardData?.assetHoldings!} />
                    </div>

                    <Card className={styles.subscriptionsCard}>
                        <div className={styles.subscriptionsHeader}>
                            <div className={styles.subscriptionsHeaderLeft}>
                                <div className={styles.subscriptionsCardIcon}>
                                    <DollarOutlined />
                                </div>
                                <div>
                                    <Title level={3} className={styles.subscriptionsTitle}>Subscriptions</Title>
                                </div>
                            </div>
                            <div className={styles.subscriptionsActions}>
                                <Button
                                    loading={refreshing}
                                    onClick={handleManualRefresh}
                                    icon={<ReloadOutlined />}
                                    className={"btn-ghost" }
                                >
                                    {refreshing ? 'Refreshing' : 'Refresh'}
                                </Button>
                                <Button
                                    className={"btn-primary"}
                                    onClick={() => navigate('/subscription/new')}
                                    icon={<PlusOutlined />}
                                >
                                    New Subscription
                                </Button>
                            </div>
                        </div>

                        {subscriptions.length > 0 ? (
                            <div className={`${styles.gridRow} ${styles.subscriptionsGrid}`}>
                                {subscriptions.map((subscription) => (
                                    <SubscriptionCard
                                        key={subscription.id}
                                        subscription={subscription}
                                        onEdit={handleEditComplete}
                                        onCancel={handleCancelSubscription}
                                        onViewHistory={handleViewHistory}
                                        onDataUpdated={() => handleDataUpdated()}
                                    />
                                ))}
                            </div>
                        ) : (
                            <div className={styles.subscriptionsEmpty}>
                                <Empty
                                    image={Empty.PRESENTED_IMAGE_SIMPLE}
                                    description={
                                        <div>
                                            <Title level={4} className={styles.emptyStateTitle}>
                                                Start Your Investment Journey
                                            </Title>
                                            <Paragraph className={styles.emptyStateDescription}>
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
                            </div>
                        )}
                    </Card>

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
                        styles={{ body: { maxHeight: '60vh', overflowY: 'auto' } }}
                    >
                        {transactionsLoading ? (
                            <div style={{ textAlign: 'center', padding: '40px' }}>
                                <Spin size="large" />
                            </div>
                        ) : transactions.length > 0 ? (
                            <Space direction="vertical" size="middle" style={{ width: '100%' }}>
                                {transactions.map((item, index) => (
                                    <Card key={index} size="small" className={styles.transactionCard}>
                                        <div className={styles.transactionRow}>
                                            <Space>
                                                <div className={styles.transactionIcon}>
                                                    <RiseOutlined />
                                                </div>
                                                <div className={styles.transactionInfo}>
                                                    <Text className={styles.transactionName}>
                                                        {item.assetName} {item.action}
                                                    </Text>
                                                    <Text className={styles.transactionDate}>
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
                                            <div className={styles.transactionAmount}>
                                                <Text className={styles.transactionQuantity}>
                                                    +{item.quantity.toFixed(6)} {item.assetTicker}
                                                </Text>
                                                <Text className={styles.transactionValue}>
                                                    {item.quoteCurrency} {item.quoteQuantity.toFixed(2)}
                                                </Text>
                                            </div>
                                        </div>
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
            <ErrorBoundary>
                <DashboardPageContent />
            </ErrorBoundary>
        </>
    );
}

export default DashboardPage;