import React, { useState, useEffect, useCallback } from 'react';
import {
    Card, Button, Alert, Spin, Badge,
    List, Space, Typography, notification,
    Tooltip, Result
} from 'antd';
import {
    CheckCircleOutlined, CloseCircleOutlined, ClockCircleOutlined,
    ExclamationCircleOutlined, CreditCardOutlined, ReloadOutlined,
    InfoCircleOutlined, WarningOutlined, SyncOutlined
} from '@ant-design/icons';
import { Subscription, SubscriptionStatus } from '../../types/subscription';
import {
    Payment, PaymentStatus, getSubscriptionPayments,
    syncPayments, updatePaymentMethod, retryPayment
} from '../../services/payment';
import { reactivatecSubscription } from '../../services/subscription';

const { Title, Text } = Typography;

interface PaymentStatusCardProps {
    subscription: Subscription;
    onDataUpdated?: () => void;
    onUpdatePaymentMethod: (subscriptionId: string) => void;
}

// Payment status component for displaying subscription payment status
const PaymentStatusCard: React.FC<PaymentStatusCardProps> = ({
    subscription,
    onUpdatePaymentMethod,
    onDataUpdated
}) => {
    const [isLoading, setIsLoading] = useState<boolean>(false);
    const [isRetrying, setIsRetrying] = useState<boolean>(false);
    const [payments, setPayments] = useState<Payment[]>([]);
    const [error, setError] = useState<string | null>(null);
    const [successMessage, setSuccessMessage] = useState<string | null>(null);

    const fetchPaymentData = useCallback(async () => {
        // Early return if no subscription ID
        if (!subscription?.id) {
            setError('No subscription ID provided');
            return;
        }

        setIsLoading(true);
        setError(null);

        try {
            // Pass the subscription ID to the function
            const payments = await getSubscriptionPayments(subscription.id);

            setPayments(payments || []);
        } catch (err: any) {
            const errorMessage = err.response?.data?.message || err.message || 'Failed to fetch payment data';
            setError(errorMessage);

            notification.error({
                message: 'Payment Data Error',
                description: errorMessage,
                placement: 'topRight'
            });

            // Handle authentication errors
            if (err.response?.status === 401) {
                setError('Authentication failed. Please login again.');
            }
        } finally {
            setIsLoading(false);
        }
    }, [subscription?.id]);

    useEffect(() => {
        if (subscription?.id) {
            fetchPaymentData();
        }
    }, [subscription?.id, fetchPaymentData]);

    const handleSync = useCallback(async () => {
        if (!subscription?.id || isRetrying) return;

        setIsRetrying(true);
        setError(null);
        setSuccessMessage(null);

        try {
            const { processedCount, totalCount } = await syncPayments(subscription.id);

            const message = processedCount > 0
                ? `Successfully synced ${processedCount} payment record${processedCount !== 1 ? 's' : ''} out of ${totalCount}`
                : 'Payment records are up to date';

            setSuccessMessage(message);

            notification.success({
                message: 'Sync Complete',
                description: message,
                placement: 'topRight'
            });

            // Refresh data after a brief delay
            setTimeout(async () => {
                await fetchPaymentData();
                if (onDataUpdated) {
                    onDataUpdated();
                }
            }, 1500);

        } catch (err: any) {
            const errorMessage = err.response?.data?.message || err.message || 'Failed to update payment records';
            setError(errorMessage);

            notification.error({
                message: 'Sync Failed',
                description: errorMessage,
                placement: 'topRight'
            });
        } finally {
            setIsRetrying(false);
        }
    }, [subscription?.id, isRetrying, fetchPaymentData, onDataUpdated]);

    const handleUpdatePaymentMethod = useCallback(async () => {
        if (onUpdatePaymentMethod && subscription) {
            onUpdatePaymentMethod(subscription.id);
        }
    }, [onUpdatePaymentMethod, subscription]);

    const handleRetryPayment = useCallback(async (paymentId: string) => {
        if (isRetrying) return;

        setIsRetrying(true);
        try {
            await retryPayment(paymentId);

            notification.success({
                message: 'Payment Retry',
                description: 'Payment retry initiated successfully',
                placement: 'topRight'
            });

            // Refresh payment data
            setTimeout(() => {
                fetchPaymentData();
            }, 1000);

        } catch (error: any) {
            const errorMessage = error.message || 'Failed to retry payment';
            setError(errorMessage);

            notification.error({
                message: 'Retry Failed',
                description: errorMessage,
                placement: 'topRight'
            });
        } finally {
            setIsRetrying(false);
        }
    }, [isRetrying, fetchPaymentData]);

    const getStatusIcon = (status: string) => {
        switch (status.toUpperCase()) {
            case PaymentStatus.Filled:
                return <CheckCircleOutlined style={{ color: '#52c41a' }} />;
            case PaymentStatus.Pending:
                return <ClockCircleOutlined style={{ color: '#faad14' }} />;
            case PaymentStatus.Failed:
                return <CloseCircleOutlined style={{ color: '#ff4d4f' }} />;
            default:
                return <ExclamationCircleOutlined style={{ color: '#8c8c8c' }} />;
        }
    };

    const getSubscriptionStatusBadge = () => {
        const status = subscription?.status || 'UNKNOWN';

        const statusConfig = {
            'ACTIVE': { color: 'success', text: 'Active' },
            'PENDING': { color: 'processing', text: 'Pending' },
            'CANCELED': { color: 'default', text: 'Canceled' },
            'SUSPENDED': { color: 'error', text: 'Suspended' },
            'PAID': { color: 'success', text: 'Paid' },
            'UNKNOWN': { color: 'default', text: 'Unknown' }
        };

        const config = statusConfig[status as keyof typeof statusConfig] || statusConfig.UNKNOWN;

        return (
            <Badge
                status={config.color as any}
                text={config.text}
            />
        );
    };

    // Check subscription status conditions
    const isSuspended = subscription?.status === SubscriptionStatus.Suspended;
    const hasFailedPayments = payments.some(payment => payment.status === PaymentStatus.Failed);

    // Render no payment records info section
    const renderNoPaymentRecordsInfo = () => (
        <Space direction="vertical" size="middle" style={{ width: '100%' }}>
            {successMessage && (
                <Alert
                    message="Sync Successful"
                    description={successMessage}
                    type="success"
                    icon={<CheckCircleOutlined />}
                    showIcon
                    closable
                    onClose={() => setSuccessMessage(null)}
                />
            )}

            <Alert
                message="No Payment Records Found"
                description={
                    <div>
                        <Text>This could happen for several reasons:</Text>
                        <ul style={{ marginTop: 8, marginBottom: 16 }}>
                            <li>Your subscription was recently created and the first payment is still being processed</li>
                            <li>Payment webhook events from Stripe haven't been received yet</li>
                            <li>There may be a temporary synchronization delay</li>
                            <li>Your subscription is in a pending state waiting for payment confirmation</li>
                        </ul>
                        <Space>
                            <Button
                                type="primary"
                                icon={isRetrying ? <SyncOutlined spin /> : <ReloadOutlined />}
                                onClick={handleSync}
                                loading={isRetrying}
                                size="small"
                            >
                                {isRetrying ? 'Updating...' : 'Retry Update'}
                            </Button>
                            <Text type="secondary" style={{ fontSize: '12px' }}>
                                This will fetch the latest payment records from Stripe
                            </Text>
                        </Space>
                    </div>
                }
                type="info"
                icon={<InfoCircleOutlined />}
                showIcon
            />
        </Space>
    );

    // Early return if no subscription
    if (!subscription) {
        return (
            <Card title="Subscription Payment Status">
                <Alert
                    message="No Subscription"
                    description="No subscription data provided"
                    type="warning"
                    showIcon
                />
            </Card>
        );
    }

    return (
        <Card style={{ marginBottom: 16 }}
        >
            {/* Alert for suspended subscription or failed payments */}
            {(isSuspended || hasFailedPayments) && (
                <Alert
                    message={
                        isSuspended
                            ? 'Your subscription is suspended due to payment issues.'
                            : 'We had trouble processing your last payment.'
                    }
                    description={
                        <div>
                            <Text>Please update your payment method to continue your subscription.</Text>
                            <div style={{ marginTop: 12 }}>
                                <Button
                                    type="primary"
                                    danger
                                    icon={<CreditCardOutlined />}
                                    onClick={handleUpdatePaymentMethod}
                                >
                                    Update Payment Method
                                </Button>
                            </div>
                        </div>
                    }
                    type="error"
                    icon={<WarningOutlined />}
                    showIcon
                    style={{ marginBottom: 16 }}
                />
            )}

            {/* Error Alert */}
            {error && (
                <Alert
                    message="Error"
                    description={error}
                    type="error"
                    closable
                    onClose={() => setError(null)}
                    style={{ marginBottom: 16 }}
                />
            )}

            {/* Loading state */}
            {isLoading ? (
                <div style={{ textAlign: 'center', padding: '20px 0' }}>
                    <Spin size="large" />
                    <div style={{ marginTop: 8 }}>
                        <Text>Loading payment data...</Text>
                    </div>
                </div>
            ) : error && !payments.length ? (
                <Result
                    status="error"
                    title="Failed to load payment data"
                    subTitle={error}
                    extra={
                        <Button type="primary" onClick={fetchPaymentData}>
                            Try Again
                        </Button>
                    }
                />
            ) : payments.length === 0 ? (
                renderNoPaymentRecordsInfo()
            ) : (
                <List
                    dataSource={payments}
                    renderItem={(payment) => (
                        <List.Item
                            actions={[
                                payment.status === PaymentStatus.Failed && (
                                    <Space key="actions">
                                        {payment.nextRetryAt && (
                                            <Tooltip title={`Next retry: ${new Date(payment.nextRetryAt).toLocaleDateString()}`}>
                                                <Text type="secondary" style={{ fontSize: '12px' }}>
                                                    Next retry: {new Date(payment.nextRetryAt).toLocaleDateString()}
                                                </Text>
                                            </Tooltip>
                                        )}
                                        <Button
                                            type="primary"
                                            size="small"
                                            onClick={() => handleRetryPayment(payment.id)}
                                            loading={isRetrying}
                                            icon={<ReloadOutlined />}
                                        >
                                            Retry Now
                                        </Button>
                                    </Space>
                                )
                            ].filter(Boolean)}
                        >
                            <List.Item.Meta
                                avatar={getStatusIcon(payment.status)}
                                title={
                                    <Space>
                                        <Text strong>
                                            {new Date(payment.createdAt).toLocaleDateString()} - {payment.currency} {payment.totalAmount}
                                        </Text>
                                        <Badge
                                            status={payment.status === PaymentStatus.Failed ? 'error' :
                                                payment.status === PaymentStatus.Pending ? 'processing' : 'success'}
                                            text={payment.status}
                                        />
                                    </Space>
                                }
                                description={
                                    payment.status === PaymentStatus.Failed && payment.failureReason && (
                                        <Text type="danger">
                                            {payment.failureReason}
                                        </Text>
                                    )
                                }
                            />
                        </List.Item>
                    )}
                />
            )}

            {/* Footer actions */}
            <div style={{
                marginTop: 16,
                padding: '12px 0',
                borderTop: '1px solid #f0f0f0',
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center'
            }}>
                <Button
                    type="link"
                    icon={<CreditCardOutlined />}
                    onClick={handleUpdatePaymentMethod}
                >
                    Update Payment Method
                </Button>
                <Button
                    type="text"
                    icon={<ReloadOutlined />}
                    onClick={fetchPaymentData}
                >
                    Refresh
                </Button>
            </div>
        </Card>
    );
};

interface SubscriptionPaymentManagerProps {
    subscription: Subscription;
    onDataUpdated?: () => void;
}

// Main component that handles payment update flow
const SubscriptionPaymentManager: React.FC<SubscriptionPaymentManagerProps> = ({
    subscription,
    onDataUpdated
}) => {
    const [updateUrl, setUpdateUrl] = useState<string | null>(null);
    const [isLoading, setIsLoading] = useState<boolean>(false);
    const [error, setError] = useState<string | null>(null);

    const handleUpdatePaymentMethod = useCallback(async (subscriptionId: string) => {
        if (isLoading) return; // Prevent double submission

        setIsLoading(true);
        setError(null);

        try {
            const data = await updatePaymentMethod(subscriptionId);

            if (data.checkoutUrl) {
                setUpdateUrl(data.checkoutUrl);
                window.location.href = data.checkoutUrl; // Redirect to Stripe
            } else {
                throw new Error('No redirect URL returned');
            }
        } catch (err: any) {
            const errorMessage = err.response?.data?.message || err.message || 'Failed to create payment update session';
            setError(errorMessage);

            notification.error({
                message: 'Payment Method Update Failed',
                description: errorMessage,
                placement: 'topRight'
            });
        } finally {
            setIsLoading(false);
        }
    }, [isLoading]);

    const handleReactivateSubscription = useCallback(async (subscriptionId: string) => {
        if (isLoading) return; // Prevent double submission

        setIsLoading(true);
        setError(null);

        try {
            await reactivatecSubscription(subscriptionId);

            notification.success({
                message: 'Subscription Reactivated',
                description: 'Your subscription has been successfully reactivated',
                placement: 'topRight'
            });

            // Refresh the page to update status
            setTimeout(() => {
                window.location.reload();
            }, 1500);

        } catch (err: any) {
            const errorMessage = err.response?.data?.message || err.message || 'Failed to reactivate subscription';
            setError(errorMessage);

            notification.error({
                message: 'Reactivation Failed',
                description: errorMessage,
                placement: 'topRight'
            });
        } finally {
            setIsLoading(false);
        }
    }, [isLoading]);

    return (
        <div>
            {/* Global error alert */}
            {error && (
                <Alert
                    message="Error"
                    description={error}
                    type="error"
                    closable
                    onClose={() => setError(null)}
                    style={{ marginBottom: 16 }}
                />
            )}

            {/* Loading overlay */}
            {isLoading ? (
                <Card>
                    <div style={{ textAlign: 'center', padding: '40px 0' }}>
                        <Spin size="large" />
                        <div style={{ marginTop: 16 }}>
                            <Text>Processing your request...</Text>
                        </div>
                    </div>
                </Card>
            ) : (
                <PaymentStatusCard
                    subscription={subscription}
                    onUpdatePaymentMethod={handleUpdatePaymentMethod}
                    onDataUpdated={onDataUpdated}
                />
            )}

            {/* Subscription reactivation section */}
            {(subscription?.status === 'SUSPENDED' || subscription?.status === PaymentStatus.Pending) && (
                <Card style={{ marginTop: 16 }}>
                    <Alert
                        message="Subscription Suspended"
                        description={
                            <div>
                                <Text>
                                    After updating your payment method, you can reactivate your subscription.
                                </Text>
                                <div style={{ marginTop: 12 }}>
                                    <Button
                                        type="primary"
                                        icon={<CheckCircleOutlined />}
                                        onClick={() => handleReactivateSubscription(subscription.id)}
                                        loading={isLoading}
                                    >
                                        Reactivate Subscription
                                    </Button>
                                </div>
                            </div>
                        }
                        type="warning"
                        icon={<ExclamationCircleOutlined />}
                        showIcon
                    />
                </Card>
            )}
        </div>
    );
};

export default SubscriptionPaymentManager;