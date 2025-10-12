import {
    CheckCircleOutlined,
    DollarCircleOutlined,
    LoadingOutlined,
    ShoppingCartOutlined,
    ShoppingOutlined,
    SyncOutlined
} from '@ant-design/icons';
import { Space, Typography } from 'antd';
import { AnimatePresence, motion } from 'framer-motion';
import React from 'react';
import { Subscription, SubscriptionState, SubscriptionStatus } from '../../types/subscription';

const { Text } = Typography;

interface ProgressStep {
    key: string;
    label: string;
    icon: React.ReactNode;
    state: string;
    description: string;
}

interface SubscriptionProgressOverlayProps {
    subscription: Subscription;
    show: boolean;
    showSteps: boolean;
}

// Define the progression steps in order
const PROGRESS_STEPS: ProgressStep[] = [
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
        description: 'Pending payment'
    },
    {
        key: 'invoice',
        label: 'Invoice',
        icon: <DollarCircleOutlined />,
        state: SubscriptionState.PROCESSING_INVOICE,
        description: 'Processing invoice'
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
        description: 'Subscription activated'
    }
];

const SubscriptionProgressOverlay: React.FC<SubscriptionProgressOverlayProps> = ({
    subscription,
    show,
    showSteps = false
}) => {
    // Determine if we should show the overlay
    const shouldShowOverlay = () => {
        // Show overlay when subscription is in a processing state
        if (subscription.state !== SubscriptionState.IDLE) {
            return true;
        }

        // Show overlay when status is PENDING
        if (subscription.status === SubscriptionStatus.PENDING) {
            return true;
        }

        return show;
    };

    // Calculate current step index based on subscription state
    const getCurrentStepIndex = (): number => {
        const state = subscription.state.toUpperCase();
        const status = subscription.status.toUpperCase();

        // If status is not PENDING and state is IDLE, subscription is complete
        if (state === SubscriptionState.IDLE && status === SubscriptionStatus.ACTIVE) {
            return PROGRESS_STEPS.length - 1; // Complete
        }

        // Find step index based on current state
        const stepIndex = PROGRESS_STEPS.findIndex(step => step.state === state);
        return stepIndex >= 0 ? stepIndex : 0;
    };

    // Calculate progress percentage (0-100)
    const getProgressPercentage = (): number => {
        const currentIndex = getCurrentStepIndex();
        const totalSteps = PROGRESS_STEPS.length - 1; // Exclude first step from calculation
        return Math.round((currentIndex / totalSteps) * 100);
    };

    const currentStepIndex = getCurrentStepIndex();
    const progressPercentage = getProgressPercentage();
    const currentStep = PROGRESS_STEPS[currentStepIndex];

    // Don't render if we shouldn't show overlay
    if (!shouldShowOverlay()) {
        return null;
    }

    // Calculate stroke dash offset for circular progress
    const radius = 70;
    const circumference = 2 * Math.PI * radius;
    const strokeDashoffset = circumference - (progressPercentage / 100) * circumference;

    // Get color based on progress
    const getProgressColor = () => {
        if (progressPercentage === 100) return '#52c41a'; // Green for complete
        if (progressPercentage >= 75) return '#13c2c2'; // Cyan for near complete
        if (progressPercentage >= 50) return '#1890ff'; // Blue for halfway
        if (progressPercentage >= 25) return '#faad14'; // Orange for started
        return '#8c8c8c'; // Gray for beginning
    };

    const progressColor = getProgressColor();

    return (
        <AnimatePresence>
            {shouldShowOverlay() && (
                <motion.div
                    initial={{ opacity: 0 }}
                    animate={{ opacity: 1 }}
                    exit={{ opacity: 0 }}
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
                        padding: '24px'
                    }}
                >
                    {/* Circular Progress */}
                    <div style={{ position: 'relative', marginBottom: '32px' }}>
                        <svg width="180" height="180" style={{ transform: 'rotate(-90deg)' }}>
                            {/* Background circle */}
                            <circle
                                cx="90"
                                cy="90"
                                r={radius}
                                fill="none"
                                stroke="#f0f0f0"
                                strokeWidth="8"
                            />
                            {/* Progress circle */}
                            <motion.circle
                                cx="90"
                                cy="90"
                                r={radius}
                                fill="none"
                                stroke={progressColor}
                                strokeWidth="8"
                                strokeLinecap="round"
                                strokeDasharray={circumference}
                                initial={{ strokeDashoffset: circumference }}
                                animate={{ strokeDashoffset }}
                                transition={{ duration: 0.8, ease: 'easeInOut' }}
                                style={{
                                    filter: `drop-shadow(0 0 8px ${progressColor}40)`
                                }}
                            />
                        </svg>

                        {/* Center content */}
                        <div
                            style={{
                                position: 'absolute',
                                top: '50%',
                                left: '50%',
                                transform: 'translate(-50%, -50%)',
                                textAlign: 'center'
                            }}
                        >
                            <motion.div
                                key={currentStep.key}
                                initial={{ scale: 0.8, opacity: 0 }}
                                animate={{ scale: 1, opacity: 1 }}
                                transition={{ duration: 0.3 }}
                                style={{
                                    fontSize: '36px',
                                    color: progressColor,
                                    marginBottom: '8px'
                                }}
                            >
                                {currentStep.icon}
                            </motion.div>
                            <Text
                                strong
                                style={{
                                    fontSize: '24px',
                                    display: 'block',
                                    color: '#262626'
                                }}
                            >
                                {progressPercentage}%
                            </Text>
                        </div>
                    </div>

                    {/* Current Step Info */}
                    <motion.div
                        key={currentStep.key}
                        initial={{ y: 10, opacity: 0 }}
                        animate={{ y: 0, opacity: 1 }}
                        transition={{ duration: 0.3, delay: 0.2 }}
                        style={{ textAlign: 'center', marginBottom: '24px' }}
                    >
                        <Text
                            strong
                            style={{
                                fontSize: '20px',
                                display: 'block',
                                marginBottom: '8px',
                                color: progressColor
                            }}
                        >
                            {currentStep.label}
                        </Text>
                        <Text
                            type="secondary"
                            style={{ fontSize: '14px', display: 'block' }}
                        >
                            {currentStep.description}
                        </Text>
                    </motion.div>

                    {/* Progress Steps Timeline */}
                    {showSteps && (
                        <div
                            style={{
                                display: 'flex',
                                justifyContent: 'center',
                                alignItems: 'center',
                                gap: '8px',
                                width: '100%',
                                maxWidth: '400px'
                            }}
                        >
                            {PROGRESS_STEPS.map((step, index) => {
                                const isCompleted = index < currentStepIndex;
                                const isCurrent = index === currentStepIndex;
                                const isPending = index > currentStepIndex;

                                let stepColor = '#d9d9d9';
                                let stepSize = '8px';

                                if (isCompleted) {
                                    stepColor = '#52c41a';
                                    stepSize = '10px';
                                } else if (isCurrent) {
                                    stepColor = progressColor;
                                    stepSize = '12px';
                                }

                                return (
                                    <React.Fragment key={step.key}>
                                        <motion.div
                                            initial={{ scale: 0 }}
                                            animate={{ scale: 1 }}
                                            transition={{ delay: index * 0.1 }}
                                            style={{
                                                position: 'relative'
                                            }}
                                        >
                                            <motion.div
                                                animate={{
                                                    width: stepSize,
                                                    height: stepSize,
                                                    backgroundColor: stepColor
                                                }}
                                                transition={{ duration: 0.3 }}
                                                style={{
                                                    borderRadius: '50%',
                                                    boxShadow: isCurrent
                                                        ? `0 0 12px ${stepColor}`
                                                        : 'none'
                                                }}
                                            />
                                            {/* Tooltip on hover */}
                                            <div
                                                style={{
                                                    position: 'absolute',
                                                    bottom: '20px',
                                                    left: '50%',
                                                    transform: 'translateX(-50%)',
                                                    background: 'rgba(0, 0, 0, 0.85)',
                                                    color: 'white',
                                                    padding: '4px 8px',
                                                    borderRadius: '4px',
                                                    fontSize: '11px',
                                                    whiteSpace: 'nowrap',
                                                    opacity: 0,
                                                    pointerEvents: 'none',
                                                    transition: 'opacity 0.2s',
                                                    zIndex: 1000
                                                }}
                                                className="step-tooltip"
                                            >
                                                {step.label}
                                            </div>
                                        </motion.div>
                                        {index < PROGRESS_STEPS.length - 1 && (
                                            <motion.div
                                                initial={{ scaleX: 0 }}
                                                animate={{ scaleX: 1 }}
                                                transition={{ delay: index * 0.1 + 0.05 }}
                                                style={{
                                                    height: '2px',
                                                    flex: 1,
                                                    backgroundColor: isCompleted
                                                        ? '#52c41a'
                                                        : '#f0f0f0',
                                                    transformOrigin: 'left',
                                                    transition: 'background-color 0.3s'
                                                }}
                                            />
                                        )}
                                    </React.Fragment>
                                );
                            })}
                        </div>
                    )}

                    {/* Loading indicator for processing states */}
                    {progressPercentage < 100 && (
                        <motion.div
                            initial={{ opacity: 0 }}
                            animate={{ opacity: 1 }}
                            transition={{ delay: 0.5 }}
                            style={{ marginTop: '24px' }}
                        >
                            <Space size="small">
                                <LoadingOutlined style={{ fontSize: '14px', color: progressColor }} />
                                <Text type="secondary" style={{ fontSize: '13px' }}>
                                    Processing... Please wait.
                                </Text>
                            </Space>
                        </motion.div>
                    )}

                    {/* Success message for completed */}
                    {progressPercentage === 100 && subscription.status === SubscriptionStatus.ACTIVE && subscription.state == SubscriptionState.IDLE && (
                        <motion.div
                            initial={{ opacity: 0, y: 10 }}
                            animate={{ opacity: 1, y: 0 }}
                            transition={{ delay: 0.5 }}
                            style={{
                                marginTop: '24px',
                                padding: '12px 24px',
                                background: 'linear-gradient(135deg, #f6ffed 0%, #d9f7be 100%)',
                                borderRadius: '8px',
                                border: '1px solid #b7eb8f'
                            }}
                        >
                            <Space>
                                <CheckCircleOutlined style={{ fontSize: '18px', color: '#52c41a' }} />
                                <Text strong style={{ color: '#389e0d' }}>
                                    Subscription activated!
                                </Text>
                            </Space>
                        </motion.div>
                    )}
                </motion.div>
            )}
        </AnimatePresence>
    );
};

// Add CSS for tooltip hover effect
const style = document.createElement('style');
style.textContent = `
    .step-tooltip {
        opacity: 0 !important;
    }
    
    div:hover > .step-tooltip {
        opacity: 1 !important;
    }
`;
document.head.appendChild(style);

export default SubscriptionProgressOverlay;