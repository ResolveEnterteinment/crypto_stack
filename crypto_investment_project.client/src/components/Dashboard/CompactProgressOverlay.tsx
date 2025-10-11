import React from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import {
    CheckCircleOutlined,
    SyncOutlined,
    DollarCircleOutlined,
    ShoppingOutlined,
    ShoppingCartOutlined,
    LoadingOutlined
} from '@ant-design/icons';
import { Typography, Space, Progress } from 'antd';
import { Subscription, SubscriptionState, SubscriptionStatus } from '../../types/subscription';

const { Text } = Typography;

interface CompactProgressOverlayProps {
    subscription: Subscription;
    show: boolean;
}

// Define the progression steps in order (same as full version)
const PROGRESS_STEPS = [
    {
        key: 'checkout',
        label: 'Checkout',
        icon: <ShoppingCartOutlined />,
        state: SubscriptionState.PENDING_CHECKOUT,
        description: 'Awaiting checkout'
    },
    {
        key: 'payment',
        label: 'Payment',
        icon: <SyncOutlined spin />,
        state: SubscriptionState.PENDING_PAYMENT,
        description: 'Processing payment'
    },
    {
        key: 'invoice',
        label: 'Invoice',
        icon: <DollarCircleOutlined />,
        state: SubscriptionState.PROCESSING_INVOICE,
        description: 'Recording transaction'
    },
    {
        key: 'assets',
        label: 'Assets',
        icon: <ShoppingOutlined />,
        state: SubscriptionState.ACQUIRING_ASSETS,
        description: 'Acquiring assets'
    },
    {
        key: 'complete',
        label: 'Active',
        icon: <CheckCircleOutlined />,
        state: SubscriptionState.IDLE,
        description: 'Subscription active'
    }
];

/**
 * Compact version of the progress overlay optimized for mobile and small screens
 */
const CompactProgressOverlay: React.FC<CompactProgressOverlayProps> = ({
    subscription,
    show
}) => {
    // Determine if we should show the overlay
    const shouldShowOverlay = () => {
        if (subscription.state !== SubscriptionState.IDLE) {
            return true;
        }
        if (subscription.status === SubscriptionStatus.PENDING) {
            return true;
        }
        return show;
    };

    // Calculate current step index
    const getCurrentStepIndex = (): number => {
        const state = subscription.state.toUpperCase();
        const status = subscription.status.toUpperCase();

        if (state === SubscriptionState.IDLE && status === SubscriptionStatus.ACTIVE) {
            return PROGRESS_STEPS.length - 1;
        }

        const stepIndex = PROGRESS_STEPS.findIndex(step => step.state === state);
        return stepIndex >= 0 ? stepIndex : 0;
    };

    // Calculate progress percentage
    const getProgressPercentage = (): number => {
        const currentIndex = getCurrentStepIndex();
        const totalSteps = PROGRESS_STEPS.length - 1;
        return Math.round((currentIndex / totalSteps) * 100);
    };

    const currentStepIndex = getCurrentStepIndex();
    const progressPercentage = getProgressPercentage();
    const currentStep = PROGRESS_STEPS[currentStepIndex];

    if (!shouldShowOverlay()) {
        return null;
    }

    // Get color based on progress
    const getProgressColor = () => {
        if (progressPercentage === 100) return '#52c41a';
        if (progressPercentage >= 75) return '#13c2c2';
        if (progressPercentage >= 50) return '#1890ff';
        if (progressPercentage >= 25) return '#faad14';
        return '#8c8c8c';
    };

    const progressColor = getProgressColor();

    return (
        <AnimatePresence>
            {shouldShowOverlay() && (
                <motion.div
                    initial={{ opacity: 0, y: -10 }}
                    animate={{ opacity: 1, y: 0 }}
                    exit={{ opacity: 0, y: -10 }}
                    transition={{ duration: 0.3 }}
                    style={{
                        position: 'absolute',
                        top: 0,
                        left: 0,
                        right: 0,
                        bottom: 0,
                        background: 'rgba(255, 255, 255, 0.98)',
                        backdropFilter: 'blur(8px)',
                        borderRadius: '16px',
                        display: 'flex',
                        flexDirection: 'column',
                        alignItems: 'center',
                        justifyContent: 'center',
                        zIndex: 10,
                        padding: '16px'
                    }}
                >
                    {/* Compact Header with Icon and Percentage */}
                    <motion.div
                        key={currentStep.key}
                        initial={{ scale: 0.8 }}
                        animate={{ scale: 1 }}
                        transition={{ duration: 0.3 }}
                        style={{
                            display: 'flex',
                            alignItems: 'center',
                            gap: '12px',
                            marginBottom: '16px'
                        }}
                    >
                        <div
                            style={{
                                fontSize: '32px',
                                color: progressColor,
                                display: 'flex',
                                alignItems: 'center'
                            }}
                        >
                            {currentStep.icon}
                        </div>
                        <div>
                            <Text
                                strong
                                style={{
                                    fontSize: '24px',
                                    display: 'block',
                                    lineHeight: 1,
                                    color: progressColor
                                }}
                            >
                                {progressPercentage}%
                            </Text>
                            <Text
                                type="secondary"
                                style={{ fontSize: '12px', display: 'block' }}
                            >
                                {currentStep.label}
                            </Text>
                        </div>
                    </motion.div>

                    {/* Progress Bar */}
                    <div style={{ width: '100%', marginBottom: '12px' }}>
                        <Progress
                            percent={progressPercentage}
                            strokeColor={{
                                '0%': progressColor,
                                '100%': '#52c41a',
                            }}
                            showInfo={false}
                            strokeWidth={6}
                            trailColor="#f0f0f0"
                        />
                    </div>

                    {/* Description */}
                    <motion.div
                        key={currentStep.key}
                        initial={{ opacity: 0 }}
                        animate={{ opacity: 1 }}
                        transition={{ delay: 0.2 }}
                        style={{ textAlign: 'center', marginBottom: '12px' }}
                    >
                        <Text
                            type="secondary"
                            style={{ fontSize: '13px', display: 'block' }}
                        >
                            {currentStep.description}
                        </Text>
                    </motion.div>

                    {/* Mini Step Indicators */}
                    <div
                        style={{
                            display: 'flex',
                            justifyContent: 'center',
                            gap: '6px',
                            marginBottom: '12px'
                        }}
                    >
                        {PROGRESS_STEPS.map((step, index) => {
                            const isCompleted = index < currentStepIndex;
                            const isCurrent = index === currentStepIndex;

                            let dotColor = '#d9d9d9';
                            let dotSize = '6px';

                            if (isCompleted) {
                                dotColor = '#52c41a';
                                dotSize = '8px';
                            } else if (isCurrent) {
                                dotColor = progressColor;
                                dotSize = '10px';
                            }

                            return (
                                <motion.div
                                    key={step.key}
                                    initial={{ scale: 0 }}
                                    animate={{
                                        scale: 1,
                                        width: dotSize,
                                        height: dotSize,
                                        backgroundColor: dotColor
                                    }}
                                    transition={{
                                        delay: index * 0.05,
                                        duration: 0.2
                                    }}
                                    style={{
                                        borderRadius: '50%',
                                        boxShadow: isCurrent
                                            ? `0 0 8px ${dotColor}`
                                            : 'none'
                                    }}
                                />
                            );
                        })}
                    </div>

                    {/* Loading or Success Message */}
                    {progressPercentage < 100 ? (
                        <Space size="small" style={{ marginTop: '8px' }}>
                            <LoadingOutlined
                                style={{ fontSize: '12px', color: progressColor }}
                            />
                            <Text
                                type="secondary"
                                style={{ fontSize: '11px' }}
                            >
                                Processing...
                            </Text>
                        </Space>
                    ) : (
                        subscription.status === SubscriptionStatus.ACTIVE && (
                            <motion.div
                                initial={{ opacity: 0, scale: 0.9 }}
                                animate={{ opacity: 1, scale: 1 }}
                                transition={{ delay: 0.3 }}
                                style={{
                                    padding: '8px 16px',
                                    background: 'linear-gradient(135deg, #f6ffed 0%, #d9f7be 100%)',
                                    borderRadius: '6px',
                                    border: '1px solid #b7eb8f',
                                    marginTop: '8px'
                                }}
                            >
                                <Space size="small">
                                    <CheckCircleOutlined
                                        style={{ fontSize: '14px', color: '#52c41a' }}
                                    />
                                    <Text
                                        strong
                                        style={{ color: '#389e0d', fontSize: '12px' }}
                                    >
                                        Activated!
                                    </Text>
                                </Space>
                            </motion.div>
                        )
                    )}
                </motion.div>
            )}
        </AnimatePresence>
    );
};

export default CompactProgressOverlay;
