// Add this component to subscription-card.tsx

import { ClockCircleOutlined } from "@ant-design/icons";
import { Space, Progress, Typography } from "antd";
import { motion } from "framer-motion";
import { Subscription, SubscriptionStateType, StateConfig } from "../../types/subscription";

const { Title, Text } = Typography;

interface ProcessingIndicatorProps {
    config: StateConfig;
    subscription: Subscription;
}

const ProcessingIndicator: React.FC<ProcessingIndicatorProps> = ({ config, subscription }) => {
    if (!config.showProgress && !config.description) return null;

    return (
        <motion.div
            initial={{ opacity: 0, height: 0 }}
            animate={{ opacity: 1, height: 'auto' }}
            exit={{ opacity: 0, height: 0 }}
            transition={{ duration: 0.3 }}
            style={{
                marginTop: '16px',
                padding: '16px',
                background: `linear-gradient(135deg, ${config.gradientStart} 0%, ${config.gradientEnd} 100%)`,
                borderRadius: '12px',
                border: `1px solid ${config.borderColor}`,
                overflow: 'hidden'
            }}
        >
            <Space direction="vertical" size="middle" style={{ width: '100%' }}>
                {/* Header with icon and text */}
                <Space align="center">
                    {config.icon}
                    <div>
                        <Text strong style={{ fontSize: '14px', color: config.color }}>
                            {config.text}
                        </Text>
                        {config.description && (
                            <div>
                                <Text type="secondary" style={{ fontSize: '12px' }}>
                                    {config.description}
                                </Text>
                            </div>
                        )}
                    </div>
                </Space>

                {/* Progress bar for processing states */}
                {config.showProgress && (
                    <Progress
                        percent={config.progressPercent}
                        status="active"
                        strokeColor={{
                            '0%': config.color,
                            '100%': '#52c41a',
                        }}
                        showInfo={false}
                        strokeWidth={6}
                    />
                )}

                {/* Estimated time for processing states */}
                {config.showProgress && (
                    <Space size="small">
                        <ClockCircleOutlined style={{ fontSize: '12px', color: '#8c8c8c' }} />
                        <Text type="secondary" style={{ fontSize: '11px' }}>
                            {getEstimatedTimeMessage(subscription.state)}
                        </Text>
                    </Space>
                )}
            </Space>
        </motion.div>
    );
};

const getEstimatedTimeMessage = (state: SubscriptionStateType): string => {
    const timeEstimates = {
        PENDING_CHECKOUT: 'Waiting for checkout completion',
        PENDING_PAYMENT: 'Usually takes 1-2 minutes',
        PROCESSING_INVOICE: 'Usually takes 30-60 seconds',
        ACQUIRING_ASSETS: 'Usually takes 1-3 minutes',
        IDLE: ''
    };

    return timeEstimates[state] || '';
};

export default ProcessingIndicator;