import {
    CheckCircleOutlined,
    ClockCircleOutlined,
    CloseCircleOutlined,
    CloseOutlined,
    CreditCardOutlined,
    DeleteOutlined,
    DollarCircleOutlined,
    DollarOutlined,
    DownOutlined,
    EditOutlined,
    ExclamationCircleOutlined,
    HistoryOutlined,
    PieChartOutlined,
    PlusOutlined,
    QuestionCircleOutlined,
    RedoOutlined,
    ShoppingCartOutlined,
    ShoppingOutlined,
    SyncOutlined,
    UpOutlined
} from '@ant-design/icons';
import {
    Badge,
    Button,
    Card,
    Col,
    DatePicker,
    Divider,
    Form,
    Input,
    InputNumber,
    Modal,
    Popconfirm,
    Progress,
    Row,
    Select,
    Space,
    Tag,
    Tooltip,
    Typography,
    message,
} from 'antd';
import dayjs from 'dayjs';
import { AnimatePresence, motion } from 'framer-motion';
import React, { useState } from 'react';
import { useAuth } from '../../context/AuthContext';
import { getSupportedAssets } from '../../services/asset';
import { PaymentRequestData, initiatePayment, syncPayments } from '../../services/payment';
import { updateSubscription } from '../../services/subscription';
import { Asset, AssetColors } from '../../types/assetTypes';
import ProcessingIndicator from './ProcessingIndicator';
import { Allocation, Subscription, SubscriptionCardProps, SubscriptionState, SubscriptionStateType, SubscriptionStatus } from '../../types/subscription';
import { formatApiError } from '../../utils/apiErrorHandler';
import SubscriptionPaymentManager from '../Subscription/SubscriptionPaymentManager';

const { Option } = Select;
const { Title, Text } = Typography;

// Get status color and theme
const getStatusConfig = (subscription: Subscription) => {
    const status = subscription.status.toUpperCase();
    const state = subscription.state;

    // State takes precedence for visual feedback when there's active processing
    if (state !== SubscriptionState.IDLE) {
        return getProcessingStateConfig(state, status);
    }

    // Default status-based configuration when IDLE
    return getIdleStatusConfig(status);
};

const getProcessingStateConfig = (state: SubscriptionStateType, status: string) => {
    const stateConfigs = {
        PENDING_CHECKOUT: {
            color: '#faad14',
            borderColor: 'rgba(250, 173, 20, 0.3)',
            gradientStart: '#fff7e6',
            gradientEnd: '#ffe7ba',
            text: 'Awaiting Checkout',
            badgeStatus: 'warning',
            showPulse: true,
            showProgress: false,
            icon: <ShoppingCartOutlined />,
            description: 'Complete your checkout to activate this subscription',
            progressPercent: 20
        },
        PENDING_PAYMENT: {
            color: '#1890ff',
            borderColor: 'rgba(24, 144, 255, 0.3)',
            gradientStart: '#e6f7ff',
            gradientEnd: '#bae7ff',
            text: 'Processing Payment',
            badgeStatus: 'processing',
            showPulse: true,
            showProgress: true,
            icon: <SyncOutlined spin />,
            description: 'Your payment is being processed by our payment provider',
            progressPercent: 40
        },
        PROCESSING_INVOICE: {
            color: '#1890ff',
            borderColor: 'rgba(24, 144, 255, 0.3)',
            gradientStart: '#e6f7ff',
            gradientEnd: '#bae7ff',
            text: 'Processing Invoice',
            badgeStatus: 'processing',
            showPulse: true,
            showProgress: true,
            icon: <DollarCircleOutlined />,
            description: 'Recording your payment and preparing asset allocation',
            progressPercent: 60
        },
        ACQUIRING_ASSETS: {
            color: '#13c2c2',
            borderColor: 'rgba(19, 194, 194, 0.3)',
            gradientStart: '#e6fffb',
            gradientEnd: '#b5f5ec',
            text: 'Acquiring Assets',
            badgeStatus: 'processing',
            showPulse: true,
            showProgress: true,
            icon: <ShoppingOutlined />,
            description: 'Purchasing your allocated crypto assets on the exchange',
            progressPercent: 80
        },
        IDLE: {
            color: '#52c41a',
            borderColor: 'rgba(82, 196, 26, 0.3)',
            gradientStart: '#f6ffed',
            gradientEnd: '#d9f7be',
            text: status === 'ACTIVE' ? 'Active' : status,
            badgeStatus: 'success',
            showPulse: false,
            showProgress: false,
            icon: <CheckCircleOutlined />,
            description: null,
            progressPercent: 100
        }
    };

    return stateConfigs[state] || stateConfigs.IDLE;
};

const getIdleStatusConfig = (status: string) => {
    const statusConfigs = {
        ACTIVE: {
            color: '#52c41a',
            borderColor: 'rgba(82, 196, 26, 0.3)',
            gradientStart: '#f6ffed',
            gradientEnd: '#d9f7be',
            text: 'Active',
            badgeStatus: 'success',
            showPulse: false,
            showProgress: false,
            icon: <CheckCircleOutlined />,
            description: null,
            progressPercent: 100
        },
        SUSPENDED: {
            color: '#ff4d4f',
            borderColor: 'rgba(255, 77, 79, 0.3)',
            gradientStart: '#fff1f0',
            gradientEnd: '#ffccc7',
            text: 'Suspended',
            badgeStatus: 'error',
            showPulse: false,
            showProgress: false,
            icon: <ExclamationCircleOutlined />,
            description: 'Subscription suspended due to payment issues',
            progressPercent: 0
        },
        CANCELLED: {
            color: '#8c8c8c',
            borderColor: 'rgba(140, 140, 140, 0.3)',
            gradientStart: '#fafafa',
            gradientEnd: '#e8e8e8',
            text: 'Cancelled',
            badgeStatus: 'default',
            showPulse: false,
            showProgress: false,
            icon: <CloseCircleOutlined />,
            description: null,
            progressPercent: 0
        },
        PENDING: {
            color: '#faad14',
            borderColor: 'rgba(250, 173, 20, 0.3)',
            gradientStart: '#fff7e6',
            gradientEnd: '#ffe7ba',
            text: 'Pending Setup',
            badgeStatus: 'warning',
            showPulse: false,
            showProgress: false,
            icon: <ClockCircleOutlined />,
            description: 'Complete setup to activate',
            progressPercent: 10
        }
    };

    return statusConfigs[status as keyof typeof statusConfigs] || statusConfigs.ACTIVE;
};

interface EditFormData {
    amount: number;
    endDate?: Date | null;
    allocations: Allocation[];
}

const SubscriptionCard: React.FC<SubscriptionCardProps & { onDataUpdated?: () => void }> = ({
    subscription,
    onEdit,
    onCancel,
    onViewHistory,
    onDataUpdated
}) => {
    const [isExpanded, setIsExpanded] = useState(false);
    const [showPaymentModal, setShowPaymentModal] = useState(false);
    const [showEditModal, setShowEditModal] = useState(false);

    const [loading, setLoading] = useState(false);
    const [retrying, setRetrying] = useState(false);
    const [syncing, setSyncing] = useState(false);
    const [assets, setAssets] = useState<Asset[]>([]);
    const [form] = Form.useForm<EditFormData>();
    const { user } = useAuth();
    const statusConfig = getStatusConfig(subscription);

    // Format date
    const formatDate = (dateString: string | Date): string => {
        return new Date(dateString).toLocaleDateString('en-US', {
            year: 'numeric',
            month: 'short',
            day: 'numeric'
        });
    };

    const formatCurrency = (amount: number): string => {
        return new Intl.NumberFormat('en-US', {
            style: 'currency',
            currency: 'USD',
            minimumFractionDigits: 2,
            maximumFractionDigits: 2,
        }).format(amount);
    };

    // Calculate progress to next payment
    const calculateProgress = (): number => {
        const now = new Date();
        const createdDate = new Date(subscription.createdAt);

        const intervalMap: Record<string, number> = {
            DAILY: 1,
            WEEKLY: 7,
            MONTHLY: 30
        };

        const intervalKey = subscription.interval.toUpperCase();
        const intervalDays = intervalMap[intervalKey] || 30;
        const totalDuration = intervalDays * 24 * 60 * 60 * 1000;
        const elapsed = now.getTime() - createdDate.getTime();
        const progress = (elapsed % totalDuration) / totalDuration * 100;

        return Math.min(Math.max(progress, 0), 100);
    };

    // Handle view history click
    const handleViewHistoryClick = () => {
        if (subscription && subscription.id) {
            onViewHistory(subscription.id);
        } else {
            console.error("Cannot view history: Missing subscription ID");
        }
    };

    // Handle payment modal close
    const handlePaymentModalClose = () => {
        setShowPaymentModal(false);
    };

    // Handle payment data update
    const handlePaymentDataUpdated = () => {
        setTimeout(() => {
            setShowPaymentModal(false);
        }, 2000);

        if (onDataUpdated) {
            onDataUpdated();
        }
    };

    // Handle edit modal open
    const handleEditClick = () => {
        const loadAssets = async () => {
            try {
                const assetData = await getSupportedAssets();
                setAssets(assetData);
            } catch (error) {
                console.error('Error loading assets:', error);
                message.error('Failed to load available assets');
            }
        };

        loadAssets();

        const initialValues: EditFormData = {
            amount: subscription.amount,
            endDate: subscription.endDate ? new Date(subscription.endDate) : null,
            allocations: subscription.allocations.map(alloc => ({
                assetId: alloc.assetId,
                assetName: alloc.assetName,
                assetTicker: alloc.assetTicker,
                percentAmount: alloc.percentAmount
            }))
        };

        form.setFieldsValue({
            ...initialValues,
            endDate: initialValues.endDate ? dayjs(initialValues.endDate) : undefined
        });

        setShowEditModal(true);
    };

    const handleRetryPaymentClick = async () => {
        try {
            setRetrying(true);
            const paymentRequest: PaymentRequestData = {
                subscriptionId: subscription.id,
                userId: user?.id!,
                amount: subscription.amount,
                currency: subscription.currency,
                isRecurring: subscription.interval !== 'ONCE',
                interval: subscription.interval,
                returnUrl: window.location.origin + `/payment/checkout/success?subscription_id=${subscription.id}&amount=${subscription.amount}&currency=${subscription.currency}`,
                cancelUrl: window.location.origin + `/payment/checkout/cancel?subscription_id=${subscription.id}`
            };

            const data = await initiatePayment(paymentRequest);

            if (!data.checkoutUrl) {
                throw new Error('Payment initialization returned empty checkout URL');
            }

            sessionStorage.setItem('pendingSubscription', JSON.stringify({
                subscriptionId: subscription.id,
                timestamp: Date.now(),
                amount: subscription.amount,
                currency: subscription.currency
            }));

            window.location.href = data.checkoutUrl;
        } catch (paymentError: any) {
            console.error('Payment initialization error:', paymentError);
            const errorMessage = formatApiError(paymentError);
            throw new Error(`Payment initialization failed: ${errorMessage}`);
        } finally {
            setRetrying(false);
        }
    };

    const handleSyncPaymentsClick = async () => {
        try {
            setSyncing(true);
            
            var result = await syncPayments(subscription.id);

            message
        } catch (syncError: any) {
            console.error('Payment syncronization error:', syncError);
            const errorMessage = formatApiError(syncError);
            throw new Error(`Sync payments failed: ${errorMessage}`);
        } finally {
            setSyncing(false);
        }
    };

    // Handle edit modal close
    const handleEditModalClose = () => {
        setShowEditModal(false);
        form.resetFields();
    };

    // Validate allocations sum to 100%
    const validateAllocations = (allocations: any[]) => {
        if (!allocations || allocations.length === 0) {
            return Promise.reject('At least one allocation is required');
        }

        const total = allocations.reduce((sum, alloc) => {
            return sum + (alloc?.percentAmount || 0);
        }, 0);

        if (Math.abs(total - 100) > 0.01) {
            return Promise.reject(`Allocations must sum to 100%. Current total: ${total.toFixed(2)}%`);
        }

        return Promise.resolve();
    };

    // Handle form submission
    const handleEditSubmit = async () => {
        try {
            setLoading(true);

            const values = await form.validateFields();

            await validateAllocations(values.allocations);

            const updatePayload = {
                amount: values.amount,
                endDate: values.endDate ? dayjs(values.endDate).toDate() : undefined,
                allocations: values.allocations.map(alloc => ({
                    assetId: alloc.assetId,
                    assetName: alloc.assetName,
                    assetTicker: alloc.assetTicker,
                    percentAmount: alloc.percentAmount
                }))
            };

            await updateSubscription(subscription.id, updatePayload);

            message.success('Subscription updated successfully');
            handleEditModalClose();

            if (onDataUpdated) {
                onDataUpdated();
            }

        } catch (error: any) {
            console.error('Error updating subscription:', error);

            if (error.name === 'ValidationError') {
                return;
            }

            const errorMessage = error?.response?.data?.message ||
                error?.message ||
                'Failed to update subscription';
            message.error(errorMessage);
        } finally {
            setLoading(false);
        }
    };

    // Calculate remaining percentage
    const calculateRemainingPercentage = () => {
        const allocations = form.getFieldValue('allocations') || [];
        const total = allocations.reduce((sum: number, alloc: any) => {
            return sum + (alloc?.percentAmount || 0);
        }, 0);
        return Math.max(0, 100 - total);
    };

    // Animation variants for smooth transitions
    const detailsVariants = {
        collapsed: {
            height: 0,
            opacity: 0,
            marginTop: 0,
            transition: {
                duration: 0.3,
                ease: [0.4, 0.0, 0.2, 1],
                opacity: { duration: 0.2 }
            }
        },
        expanded: {
            height: 'auto',
            opacity: 1,
            marginTop: 16,
            transition: {
                duration: 0.4,
                ease: [0.4, 0.0, 0.2, 1],
                opacity: { duration: 0.3, delay: 0.1 }
            }
        }
    };

    const contentVariants = {
        collapsed: {
            y: -10,
            opacity: 0
        },
        expanded: {
            y: 0,
            opacity: 1,
            transition: {
                duration: 0.3,
                delay: 0.1,
                ease: [0.4, 0.0, 0.2, 1]
            }
        }
    };

    return (
        <>
            <motion.div
                layout
                initial={{ opacity: 0, y: 20 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.5, ease: [0.4, 0.0, 0.2, 1] }}
            >
                <Card
                    style={{
                        background: 'rgba(255, 255, 255, 0.9)',
                        backdropFilter: 'blur(10px)',
                        border: `2px solid ${statusConfig.borderColor}`,
                        borderRadius: '16px',
                        boxShadow: statusConfig.showPulse
                            ? `0 0 0 4px ${statusConfig.borderColor}, 0 8px 32px rgba(0, 0, 0, 0.1)`
                            : '0 8px 32px rgba(0, 0, 0, 0.1)',
                        transition: 'all 0.3s ease',
                        opacity: subscription.isCancelled ? 0.75 : 1,
                        animation: statusConfig.showPulse ? 'pulse 2s infinite' : 'none'
                    }}
                    hoverable
                    bodyStyle={{ padding: '24px' }}
                >
                    {/* Header Section */}
                    <Row justify="space-between" align="top" style={{ marginBottom: '20px' }}>
                        <Col flex="1">
                            <Space direction="vertical" size="small">
                                <Space align="center">
                                    <div style={{
                                        background: `linear-gradient(135deg, ${statusConfig.color} 0%, ${statusConfig.color}dd 100%)`,
                                        padding: '8px',
                                        borderRadius: '8px',
                                        display: 'flex',
                                        alignItems: 'center',
                                        justifyContent: 'center'
                                    }}>
                                        <DollarOutlined style={{ fontSize: '16px', color: 'white' }} />
                                    </div>
                                    <div>
                                        <Title level={4} style={{ margin: 0, lineHeight: 1.2 }}>
                                            ${subscription.amount} {subscription.currency}
                                        </Title>
                                        <Text type="secondary" style={{ fontSize: '14px' }}>
                                            {subscription.interval.toLowerCase()} subscription
                                        </Text>
                                    </div>
                                </Space>
                            </Space>
                        </Col>

                        <Col>
                            <Badge
                                status={statusConfig.badgeStatus as any}
                                dot={statusConfig.showPulse}
                                offset={[-5, 5]}
                            >
                                <Tag
                                    icon={statusConfig.icon}
                                    color={statusConfig.color}
                                    style={{
                                        borderRadius: '12px',
                                        padding: '6px 14px',
                                        fontSize: '12px',
                                        fontWeight: 600,
                                        border: 'none',
                                        boxShadow: statusConfig.showPulse
                                            ? `0 2px 8px ${statusConfig.color}40`
                                            : 'none'
                                    }}
                                >
                                    {statusConfig.text}
                                </Tag>
                            </Badge>
                        </Col>
                    </Row>

                    {/* Processing Indicator */}
                    <AnimatePresence>
                        <ProcessingIndicator
                            config={statusConfig}
                            subscription={subscription}
                        />
                    </AnimatePresence>

                    {/* Progress Section */}
                    <div style={{ marginBottom: '20px' }}>
                        <Row justify="space-between" style={{ marginBottom: '8px' }}>
                            <Col>
                                <Text type="secondary" style={{ fontSize: '12px' }}>
                                    Last payment: {subscription.lastPayment ? formatDate(subscription.lastPayment) : "N/A"}
                                </Text>
                            </Col>
                            <Col>
                                <Text type="secondary" style={{ fontSize: '12px' }}>
                                    Next: {formatDate(subscription.nextDueDate)}
                                </Text>
                            </Col>
                        </Row>

                        <Progress
                            percent={calculateProgress()}
                            strokeColor={{
                                '0%': '#108ee9',
                                '100%': '#87d068',
                            }}
                            showInfo={false}
                            strokeWidth={8}
                            style={{ marginBottom: '12px' }}
                        />

                        {/* Toggle Details Button */}
                        <Button
                            type="text"
                            size="small"
                            onClick={() => setIsExpanded(!isExpanded)}
                            style={{
                                padding: '4px 8px',
                                height: 'auto',
                                fontSize: '13px',
                                color: '#1890ff'
                            }}
                            icon={isExpanded ? <UpOutlined /> : <DownOutlined />}
                        >
                            {isExpanded ? 'Hide details' : 'Show details'}
                        </Button>
                    </div>

                    {/* Animated Expanded Details */}
                    <AnimatePresence>
                        {isExpanded && (
                            <motion.div
                                variants={detailsVariants}
                                initial="collapsed"
                                animate="expanded"
                                exit="collapsed"
                                style={{ overflow: 'hidden' }}
                            >
                                <motion.div variants={contentVariants}>
                                    <Card
                                        size="small"
                                        style={{
                                            background: 'linear-gradient(135deg, #fafafa 0%, #f0f0f0 100%)',
                                            border: '1px solid #e8e8e8',
                                            borderRadius: '12px'
                                        }}
                                    >
                                        <Space direction="vertical" size="middle" style={{ width: '100%' }}>
                                            {/* Subscription Info */}
                                            <Row gutter={[16, 8]}>
                                                <Col xs={24} sm={12}>
                                                    <Text type="secondary" style={{ fontSize: '12px' }}>Subscription ID</Text>
                                                    <br />
                                                    <Text style={{ fontSize: '13px', fontFamily: 'monospace' }}>
                                                        {subscription.id.slice(0, 8)}...
                                                    </Text>
                                                </Col>
                                                <Col xs={24} sm={12}>
                                                    <Text type="secondary" style={{ fontSize: '12px' }}>Total Invested</Text>
                                                    <br />
                                                    <Text strong style={{ fontSize: '13px', color: '#52c41a' }}>
                                                        {formatCurrency(subscription.totalInvestments)} {subscription.currency}
                                                    </Text>
                                                </Col>
                                            </Row>

                                            <Divider style={{ margin: '8px 0' }} />

                                            {/* Asset Allocations */}
                                            <div>
                                                <Space align="center" style={{ marginBottom: '12px' }}>
                                                    <PieChartOutlined style={{ color: '#1890ff' }} />
                                                    <Text strong style={{ fontSize: '14px' }}>Asset Allocations</Text>
                                                </Space>

                                                {/* Allocation Bar */}
                                                <div style={{
                                                    height: '8px',
                                                    background: '#f5f5f5',
                                                    borderRadius: '4px',
                                                    overflow: 'hidden',
                                                    display: 'flex',
                                                    marginBottom: '12px'
                                                }}>
                                                    {subscription.allocations?.map((alloc: Allocation) => (
                                                        <Tooltip
                                                            key={alloc.assetId}
                                                            title={`${alloc.assetTicker}: ${alloc.percentAmount.toFixed(1)}%`}
                                                        >
                                                            <div
                                                                style={{
                                                                    width: `${alloc.percentAmount}%`,
                                                                    backgroundColor: AssetColors[alloc.assetTicker] || '#6B7280',
                                                                    height: '100%',
                                                                    cursor: 'pointer'
                                                                }}
                                                            />
                                                        </Tooltip>
                                                    ))}
                                                </div>

                                                {/* Allocation List */}
                                                <Space direction="vertical" size="small" style={{ width: '100%' }}>
                                                    {subscription.allocations.map((alloc: Allocation) => (
                                                        <Row key={alloc.assetId} justify="space-between" align="middle">
                                                            <Col>
                                                                <Space align="center" size="small">
                                                                    <div
                                                                        style={{
                                                                            width: '10px',
                                                                            height: '10px',
                                                                            backgroundColor: AssetColors[alloc.assetTicker] || '#6B7280',
                                                                            borderRadius: '50%'
                                                                        }}
                                                                    />
                                                                    <Text style={{ fontSize: '13px' }}>
                                                                        {alloc.assetName} ({alloc.assetTicker})
                                                                    </Text>
                                                                </Space>
                                                            </Col>
                                                            <Col>
                                                                <Text strong style={{ fontSize: '13px' }}>
                                                                    {alloc.percentAmount}%
                                                                </Text>
                                                            </Col>
                                                        </Row>
                                                    ))}
                                                </Space>
                                            </div>
                                        </Space>
                                    </Card>
                                    {/* Action Buttons */}
                                    <Row justify="end" style={{ marginTop: '20px' }}>
                                        <Col>
                                            <Space wrap size="small">
                                                {subscription.status === SubscriptionStatus.ACTIVE && (
                                                    <Button
                                                        size="small"
                                                        onClick={() => setShowPaymentModal(true)}
                                                        icon={<CreditCardOutlined />}
                                                    >
                                                        Payment Status
                                                    </Button>
                                                )}

                                                {!subscription.isCancelled && (
                                                    <Button
                                                        size="small"
                                                        type="primary"
                                                        onClick={handleEditClick}
                                                        disabled={subscription.isCancelled}
                                                        icon={<EditOutlined />}
                                                    >
                                                        Edit
                                                    </Button>
                                                )}

                                                <Popconfirm
                                                    title="Cancel subscription"
                                                    description={
                                                        subscription.status === SubscriptionStatus.PENDING || subscription.isCancelled
                                                            ? "Are you sure to delete this subscription? This will permanently delete all the data."
                                                            : "Are you sure to cancel this subscription? This action will suspend any pending payments. You can always reactivate later."
                                                    }
                                                    onConfirm={() => onCancel(subscription.id)}
                                                    icon={<QuestionCircleOutlined style={{ color: 'red' }} />}
                                                >
                                                    <Button
                                                        size="small"
                                                        danger
                                                        icon={<CloseOutlined />}
                                                    >
                                                        {subscription.status === SubscriptionStatus.PENDING || subscription.isCancelled ? "Delete" : "Cancel"}
                                                    </Button>
                                                </Popconfirm>

                                                {subscription.status === SubscriptionStatus.PENDING && (
                                                    <Button
                                                        size="small"
                                                        loading={retrying}
                                                        style={{ backgroundColor: '#faad14', borderColor: '#faad14', color: 'white' }}
                                                        onClick={handleRetryPaymentClick}
                                                        icon={<RedoOutlined />}
                                                    >
                                                        {retrying ? 'Retrying...' : 'Retry Payment'}
                                                    </Button>
                                                )}
  
                                                    <Button
                                                        size="small"
                                                        loading={syncing}
                                                        style={{ backgroundColor: 'green', borderColor: 'green', color: 'white' }}
                                                        onClick={handleSyncPaymentsClick}
                                                        icon={<RedoOutlined />}
                                                    >
                                                        {retrying ? 'Retrying...' : 'Sync Payments'}
                                                    </Button>

                                                {subscription.status !== SubscriptionStatus.PENDING && (
                                                    <Button
                                                        size="small"
                                                        onClick={handleViewHistoryClick}
                                                        icon={<HistoryOutlined />}
                                                    >
                                                        History
                                                    </Button>
                                                )}
                                            </Space>
                                        </Col>
                                    </Row>
                                </motion.div>
                            </motion.div>
                        )}
                    </AnimatePresence>
                </Card>
            </motion.div>

            {/* Payment Manager Modal */}
            <Modal
                title="Payment Manager"
                open={showPaymentModal}
                onCancel={handlePaymentModalClose}
                footer={null}
                width={600}
            >
                <SubscriptionPaymentManager
                    subscription={subscription}
                    onDataUpdated={handlePaymentDataUpdated}
                />
            </Modal>

            {/* Edit Subscription Modal */}
            <Modal
                title={
                    <Space>
                        <EditOutlined />
                        <span>Edit Subscription</span>
                    </Space>
                }
                open={showEditModal}
                onCancel={handleEditModalClose}
                width={800}
                footer={[
                    <Button key="cancel" onClick={handleEditModalClose}>
                        Cancel
                    </Button>,
                    <Button
                        key="submit"
                        type="primary"
                        loading={loading}
                        onClick={handleEditSubmit}
                    >
                        Update Subscription
                    </Button>
                ]}
            >
                <Form
                    form={form}
                    layout="vertical"
                    initialValues={{
                        allocations: [{ assetId: '', percentAmount: 0 }]
                    }}
                >
                    <Row gutter={16}>
                        {/* Amount */}
                        <Col xs={24} sm={12}>
                            <Form.Item
                                label="Amount"
                                name="amount"
                                rules={[
                                    { required: true, message: 'Amount is required' },
                                    { type: 'number', min: 1, message: 'Amount must be greater than 0' }
                                ]}
                            >
                                <InputNumber
                                    style={{ width: '100%' }}
                                    prefix="$"
                                    placeholder="Enter amount"
                                    min={1}
                                    precision={2}
                                />
                            </Form.Item>
                        </Col>

                        {/* Current Interval (Read-only) */}
                        <Col xs={24} sm={12}>
                            <Form.Item label="Payment Interval">
                                <Input
                                    value={`${subscription.interval.toLowerCase()} (cannot be changed)`}
                                    disabled
                                    style={{ color: '#666', fontStyle: 'italic' }}
                                />
                                <Text type="secondary" style={{ fontSize: '12px' }}>
                                    To change the payment interval, please create a new subscription
                                </Text>
                            </Form.Item>
                        </Col>
                    </Row>

                    {/* End Date */}
                    <Form.Item
                        label="End Date (Optional)"
                        name="endDate"
                    >
                        <DatePicker
                            style={{ width: '100%' }}
                            placeholder="Select end date"
                            disabledDate={(current) => current && current.isBefore(dayjs(), 'day')}
                        />
                    </Form.Item>

                    <Divider>Asset Allocations</Divider>

                    {/* Allocations */}
                    <Form.Item
                        label="Asset Allocations"
                        required
                    >
                        <Form.List name="allocations">
                            {(fields, { add, remove }) => (
                                <>
                                    {fields.map((field, index) => (
                                        <Card key={field.key} size="small" style={{ marginBottom: '8px' }}>
                                            <Row gutter={8} align="middle">
                                                <Col flex="1">
                                                    <Form.Item
                                                        {...field}
                                                        name={[field.name, 'assetId']}
                                                        rules={[{ required: true, message: 'Please select an asset' }]}
                                                        style={{ marginBottom: 0 }}
                                                    >
                                                        <Select
                                                            placeholder="Select asset"
                                                            showSearch
                                                            optionFilterProp="children"
                                                            filterOption={(input, option) =>
                                                                (option?.children as unknown as string)
                                                                    ?.toLowerCase()
                                                                    ?.includes(input.toLowerCase()) ?? false
                                                            }
                                                        >
                                                            {assets.map(asset => (
                                                                <Option key={asset.id} value={asset.id}>
                                                                    {asset.name} ({asset.ticker})
                                                                </Option>
                                                            ))}
                                                        </Select>
                                                    </Form.Item>
                                                </Col>

                                                <Col>
                                                    <Form.Item
                                                        {...field}
                                                        name={[field.name, 'percentAmount']}
                                                        rules={[
                                                            { required: true, message: 'Percentage required' },
                                                            { type: 'number', min: 0.01, max: 100, message: 'Must be between 0.01 and 100' }
                                                        ]}
                                                        style={{ marginBottom: 0 }}
                                                    >
                                                        <InputNumber
                                                            placeholder="Percentage"
                                                            min={0.01}
                                                            max={100}
                                                            precision={2}
                                                            suffix="%"
                                                            style={{ width: 120 }}
                                                        />
                                                    </Form.Item>
                                                </Col>

                                                {fields.length > 1 && (
                                                    <Col>
                                                        <Button
                                                            type="text"
                                                            danger
                                                            icon={<DeleteOutlined />}
                                                            onClick={() => {
                                                                remove(field.name);
                                                                const currentValues = form.getFieldsValue();
                                                                form.setFieldsValue(currentValues);
                                                            }}
                                                        />
                                                    </Col>
                                                )}
                                            </Row>
                                        </Card>
                                    ))}

                                    <Row justify="space-between" align="middle">
                                        <Col>
                                            <Button
                                                type="dashed"
                                                onClick={() => add()}
                                                icon={<PlusOutlined />}
                                            >
                                                Add Asset
                                            </Button>
                                        </Col>

                                        <Col>
                                            <Text type="secondary" style={{ fontSize: '13px' }}>
                                                Remaining: {calculateRemainingPercentage().toFixed(2)}%
                                            </Text>
                                        </Col>
                                    </Row>
                                </>
                            )}
                        </Form.List>
                    </Form.Item>

                    {/* Allocation Summary */}
                    <Form.Item shouldUpdate>
                        {() => {
                            const allocations = form.getFieldValue('allocations') || [];
                            const total = allocations.reduce((sum: number, alloc: any) =>
                                sum + (alloc?.percentAmount || 0), 0
                            );

                            return (
                                <Card
                                    size="small"
                                    style={{
                                        background: Math.abs(total - 100) < 0.01 ? '#f6ffed' : '#fff2f0',
                                        border: `1px solid ${Math.abs(total - 100) < 0.01 ? '#b7eb8f' : '#ffccc7'}`
                                    }}
                                >
                                    <Text strong>Total Allocation: {total.toFixed(2)}%</Text>
                                    {Math.abs(total - 100) > 0.01 && (
                                        <div style={{ marginTop: '4px' }}>
                                            <Text type="danger" style={{ fontSize: '12px' }}>
                                                Allocations must sum to exactly 100%
                                            </Text>
                                        </div>
                                    )}
                                </Card>
                            );
                        }}
                    </Form.Item>
                </Form>
            </Modal>
        </>
    );
};

export default SubscriptionCard;