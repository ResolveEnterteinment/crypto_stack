// Professional UI Component Library for Crypto Investment Platform
// /src/components/ui/ProfessionalUILibrary.tsx

import {
    CheckCircleOutlined,
    CloseCircleOutlined,
    ExclamationCircleOutlined,
    InfoCircleOutlined,
    LockOutlined,
    SafetyOutlined
} from '@ant-design/icons';
import {
    Badge,
    Button,
    Card,
    Progress,
    Space,
    Tag,
    Tooltip,
    Typography
} from 'antd';
import { AnimatePresence, motion } from 'framer-motion';
import React, { useEffect, useState } from 'react';
import styled, { keyframes } from 'styled-components';

const { Title, Text, Paragraph } = Typography;

// ===========================
// Styled Components & Animations
// ===========================

const shimmer = keyframes`
  0% {
    background-position: -1000px 0;
  }
  100% {
    background-position: 1000px 0;
  }
`;

const pulse = keyframes`
  0% {
    box-shadow: 0 0 0 0 rgba(37, 99, 235, 0.7);
  }
  70% {
    box-shadow: 0 0 0 10px rgba(37, 99, 235, 0);
  }
  100% {
    box-shadow: 0 0 0 0 rgba(37, 99, 235, 0);
  }
`;

const float = keyframes`
  0% { transform: translateY(0px); }
  50% { transform: translateY(-10px); }
  100% { transform: translateY(0px); }
`;

const GlassCard = styled(Card) <{ $variant?: 'default' | 'success' | 'warning' | 'error' }>`
  background: ${props => {
        switch (props.$variant) {
            case 'success': return 'rgba(76, 175, 80, 0.08)';
            case 'warning': return 'rgba(255, 193, 7, 0.08)';
            case 'error': return 'rgba(244, 67, 54, 0.08)';
            default: return 'rgba(255, 255, 255, 0.95)';
        }
    }};
  backdrop-filter: blur(20px);
  border: 1px solid ${props => {
        switch (props.$variant) {
            case 'success': return 'rgba(76, 175, 80, 0.2)';
            case 'warning': return 'rgba(255, 193, 7, 0.2)';
            case 'error': return 'rgba(244, 67, 54, 0.2)';
            default: return 'rgba(255, 255, 255, 0.2)';
        }
    }};
  border-radius: 16px;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.1);
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);

  &:hover {
    transform: translateY(-4px);
    box-shadow: 0 12px 48px rgba(0, 0, 0, 0.15);
  }
`;

const GradientButton = styled(Button)`
  background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
  border: none;
  color: white;
  font-weight: 600;
  height: 48px;
  padding: 0 32px;
  border-radius: 12px;
  transition: all 0.3s ease;
  position: relative;
  overflow: hidden;

  &::before {
    content: '';
    position: absolute;
    top: 0;
    left: -100%;
    width: 100%;
    height: 100%;
    background: linear-gradient(
      90deg,
      transparent,
      rgba(255, 255, 255, 0.3),
      transparent
    );
    transition: left 0.5s;
  }

  &:hover::before {
    left: 100%;
  }

  &:hover {
    transform: scale(1.05);
    box-shadow: 0 8px 24px rgba(102, 126, 234, 0.4);
  }
`;

const StatusIndicator = styled.div<{ $status: 'online' | 'offline' | 'pending' }>`
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 4px 12px;
  border-radius: 20px;
  font-size: 12px;
  font-weight: 600;
  background: ${props => {
        switch (props.$status) {
            case 'online': return 'rgba(76, 175, 80, 0.1)';
            case 'offline': return 'rgba(244, 67, 54, 0.1)';
            case 'pending': return 'rgba(255, 193, 7, 0.1)';
        }
    }};
  color: ${props => {
        switch (props.$status) {
            case 'online': return '#4caf50';
            case 'offline': return '#f44336';
            case 'pending': return '#ff9800';
        }
    }};

  &::before {
    content: '';
    width: 8px;
    height: 8px;
    border-radius: 50%;
    background: currentColor;
    animation: ${props => props.$status === 'online' ? pulse : 'none'} 2s infinite;
  }
`;

// ===========================
// Enhanced Loading Component
// ===========================

interface LoadingScreenProps {
    message?: string;
    progress?: number;
}

export const ProfessionalLoadingScreen: React.FC<LoadingScreenProps> = ({
    message = 'Loading secure connection...',
    progress
}) => {
    const [dots, setDots] = useState('');

    useEffect(() => {
        const interval = setInterval(() => {
            setDots(prev => prev.length >= 3 ? '' : prev + '.');
        }, 500);
        return () => clearInterval(interval);
    }, []);

    return (
        <motion.div
            initial= {{ opacity: 0 }
}
animate = {{ opacity: 1 }}
exit = {{ opacity: 0 }}
style = {{
    position: 'fixed',
        inset: 0,
            background: 'linear-gradient(135deg, #667eea 0%, #764ba2 100%)',
                display: 'flex',
                    alignItems: 'center',
                        justifyContent: 'center',
                            zIndex: 9999
}}
        >
    <GlassCard style={ { maxWidth: 400, width: '90%', textAlign: 'center' } }>
        <Space direction="vertical" size = "large" style = {{ width: '100%' }}>
            <motion.div
                        animate={ { rotate: 360 } }
transition = {{ duration: 2, repeat: Infinity, ease: 'linear' }}
                    >
    <SafetyOutlined style={ { fontSize: 48, color: '#667eea' } } />
        </motion.div>

        < div >
        <Title level={ 4 } style = {{ marginBottom: 8 }}>
            Securing Your Session
                </Title>
                < Text type = "secondary" >
                    { message }{ dots }
</Text>
    </div>

{
    progress !== undefined && (
        <Progress 
                            percent={ progress }
    strokeColor = {{
        '0%': '#667eea',
            '100%': '#764ba2'
    }
}
showInfo = { false}
strokeWidth = { 4}
    />
                    )}

<div style={ { display: 'flex', justifyContent: 'center', gap: 8 } }>
{
    [0, 1, 2].map((i) => (
        <motion.div
                                key= { i }
                                animate = {{
        scale: [1, 1.2, 1],
        opacity: [0.5, 1, 0.5]
    }}
transition = {{
    duration: 1.5,
        repeat: Infinity,
            delay: i * 0.2
}}
style = {{
    width: 8,
        height: 8,
            borderRadius: '50%',
                background: '#667eea'
}}
                            />
                        ))}
</div>
    </Space>
    </GlassCard>
    </motion.div>
    );
};

// ===========================
// Security Alert Component
// ===========================

interface SecurityAlertProps {
    type: 'success' | 'warning' | 'error' | 'info';
    title: string;
    message: string;
    action?: () => void;
    actionText?: string;
}

export const SecurityAlert: React.FC<SecurityAlertProps> = ({
    type,
    title,
    message,
    action,
    actionText
}) => {
    const icons = {
        success: <CheckCircleOutlined />,
        warning: <ExclamationCircleOutlined />,
        error: <CloseCircleOutlined />,
        info: <InfoCircleOutlined />
    };

    const colors = {
        success: '#4caf50',
        warning: '#ff9800',
        error: '#f44336',
        info: '#2196f3'
    };

    return (
        <motion.div
            initial= {{ opacity: 0, y: -20 }
}
animate = {{ opacity: 1, y: 0 }}
exit = {{ opacity: 0, y: -20 }}
        >
    <GlassCard 
                $variant={ type === 'info' ? 'default' : type }
style = {{ marginBottom: 16 }}
            >
    <Space size="middle" align = "start" >
        <div style={
            {
                fontSize: 24,
                    color: colors[type],
                        marginTop: 2
            }
}>
    { icons[type]}
    </div>
    < div style = {{ flex: 1 }}>
        <Title level={ 5 } style = {{ marginBottom: 4 }}>
            { title }
            </Title>
            < Text type = "secondary" >
                { message }
                </Text>
{
    action && (
        <div style={ { marginTop: 12 } }>
            <Button 
                                    type="link"
    onClick = { action }
    style = {{
        padding: 0,
            color: colors[type],
                fontWeight: 600
    }
}
                                >
    { actionText || 'Take Action'} →
</Button>
    </div>
                        )}
</div>
    </Space>
    </GlassCard>
    </motion.div>
    );
};

// ===========================
// Connection Status Component
// ===========================

interface ConnectionStatusProps {
    isConnected: boolean;
    isSecure?: boolean;
    latency?: number;
}

export const ConnectionStatus: React.FC<ConnectionStatusProps> = ({
    isConnected,
    isSecure = true,
    latency
}) => {
    return (
        <motion.div
            initial= {{ opacity: 0, scale: 0.9 }
}
animate = {{ opacity: 1, scale: 1 }}
style = {{
    position: 'fixed',
        bottom: 24,
            right: 24,
                zIndex: 1000
}}
        >
    <GlassCard
                size="small"
style = {{
    minWidth: 180,
        cursor: 'pointer'
}}
hoverable
    >
    <Space size="small" align = "center" >
        <StatusIndicator $status={ isConnected ? 'online' : 'offline' }>
            { isConnected? 'Connected': 'Disconnected' }
            </StatusIndicator>

{
    isSecure && (
        <Tooltip title="Secure Connection" >
            <LockOutlined style={ { color: '#4caf50' } } />
                </Tooltip>
                    )
}

{
    latency !== undefined && (
        <Tag color={ latency < 100 ? 'success' : latency < 300 ? 'warning' : 'error' }>
            { latency }ms
                </Tag>
                    )
}
</Space>
    </GlassCard>
    </motion.div>
    );
};

// ===========================
// Enhanced Error Boundary
// ===========================

interface ErrorFallbackProps {
    error: Error;
    resetError: () => void;
}

export const ErrorFallback: React.FC<ErrorFallbackProps> = ({ error, resetError }) => {
    return (
        <div style= {{
        minHeight: '100vh',
            display: 'flex',
                alignItems: 'center',
                    justifyContent: 'center',
                        background: 'linear-gradient(135deg, #f5f7fa 0%, #c3cfe2 100%)',
                            padding: 16
    }
}>
    <motion.div
                initial={ { scale: 0.9, opacity: 0 } }
animate = {{ scale: 1, opacity: 1 }}
transition = {{ type: 'spring', stiffness: 100 }}
            >
    <GlassCard style={ { maxWidth: 500, textAlign: 'center' } }>
        <Space direction="vertical" size = "large" style = {{ width: '100%' }}>
            <motion.div
                            animate={
    {
        rotate: [0, -10, 10, -10, 0],
            scale: [1, 1.1, 1]
    }
}
transition = {{ duration: 0.5 }}
                        >
    <ExclamationCircleOutlined 
                                style={ { fontSize: 64, color: '#f44336' } }
                            />
    </motion.div>

    < div >
    <Title level={ 3 } style = {{ marginBottom: 8 }}>
        Something went wrong
            </Title>
            < Paragraph type = "secondary" >
                We encountered an unexpected error.Our team has been notified.
                            </Paragraph>

                    < details style = {{
    marginTop: 16,
        padding: 12,
            background: '#f5f5f5',
                borderRadius: 8,
                    textAlign: 'left'
}}>
    <summary style={ { cursor: 'pointer', fontWeight: 600 } }>
        Technical Details
            </summary>
            < pre style = {{
    marginTop: 8,
        fontSize: 12,
            overflow: 'auto',
                whiteSpace: 'pre-wrap',
                    wordBreak: 'break-word'
}}>
    { error.stack || error.message }
    </pre>
    </details>
    </div>

    < Space >
    <Button onClick={ () => window.location.href = '/' }>
        Go Home
            </Button>
            < GradientButton onClick = { resetError } >
                Try Again
                    </GradientButton>
                    </Space>
                    </Space>
                    </GlassCard>
                    </motion.div>
                    </div>
    );
};

// ===========================
// Rate Limit Warning Component
// ===========================

interface RateLimitWarningProps {
    remainingRequests: number;
    resetTime: Date;
    onClose?: () => void;
}

export const RateLimitWarning: React.FC<RateLimitWarningProps> = ({
    remainingRequests,
    resetTime,
    onClose
}) => {
    const [timeUntilReset, setTimeUntilReset] = useState('');

    useEffect(() => {
        const interval = setInterval(() => {
            const now = new Date().getTime();
            const reset = resetTime.getTime();
            const diff = reset - now;

            if (diff <= 0) {
                setTimeUntilReset('Reset completed');
                clearInterval(interval);
            } else {
                const minutes = Math.floor(diff / 60000);
                const seconds = Math.floor((diff % 60000) / 1000);
                setTimeUntilReset(`${minutes}m ${seconds}s`);
            }
        }, 1000);

        return () => clearInterval(interval);
    }, [resetTime]);

    const severity = remainingRequests < 10 ? 'error' : remainingRequests < 50 ? 'warning' : 'info';

    return (
        <AnimatePresence>
        {
            remainingRequests< 100 && (
                <motion.div
                    initial={ { opacity: 0, x: 300 } }
    animate = {{ opacity: 1, x: 0 }
}
exit = {{ opacity: 0, x: 300 }}
style = {{
    position: 'fixed',
        top: 80,
            right: 16,
                zIndex: 1001,
                    maxWidth: 320
}}
                >
    <GlassCard $variant={ severity as any }>
        <Space direction="vertical" size = "small" style = {{ width: '100%' }}>
            <div style={ { display: 'flex', justifyContent: 'space-between', alignItems: 'center' } }>
                <Space>
                <ExclamationCircleOutlined style={ { color: severity === 'error' ? '#f44336' : '#ff9800' } } />
                    < Text strong > Rate Limit Warning </Text>
                        </Space>
{
    onClose && (
        <Button 
                                        type="text"
    size = "small"
    onClick = { onClose }
    icon = {< CloseCircleOutlined />}
                                    />
                                )}
</div>

    < Text type = "secondary" style = {{ fontSize: 12 }}>
        { remainingRequests } requests remaining
            </Text>

            < Progress
percent = {(remainingRequests / 100) * 100}
showInfo = { false}
strokeColor = { severity === 'error' ? '#f44336' : '#ff9800'}
size = "small"
    />

    <Text type="secondary" style = {{ fontSize: 11 }}>
        Resets in { timeUntilReset }
        </Text>
        </Space>
        </GlassCard>
        </motion.div>
            )}
</AnimatePresence>
    );
};

// ===========================
// Security Badge Component
// ===========================

interface SecurityBadgeProps {
    level: 'basic' | 'standard' | 'advanced';
    verified?: boolean;
}

export const SecurityBadge: React.FC<SecurityBadgeProps> = ({ level, verified = false }) => {
    const colors = {
        basic: '#9e9e9e',
        standard: '#2196f3',
        advanced: '#4caf50'
    };

    const icons = {
        basic: '🔒',
        standard: '🛡️',
        advanced: '🏆'
    };

    return (
        <Tooltip title= {`Security Level: ${level.toUpperCase()}${verified ? ' (Verified)' : ''}`
}>
    <Badge
                count={ verified ? <CheckCircleOutlined style={ { color: '#4caf50' } } /> : 0 }
offset = { [-5, 5]}
    >
    <motion.div
                    whileHover={ { scale: 1.1 } }
whileTap = {{ scale: 0.95 }}
style = {{
    display: 'inline-flex',
        alignItems: 'center',
            gap: 6,
                padding: '6px 12px',
                    background: `linear-gradient(135deg, ${colors[level]}20 0%, ${colors[level]}10 100%)`,
                        border: `2px solid ${colors[level]}`,
                            borderRadius: 20,
                                cursor: 'pointer'
}}
                >
    <span style={ { fontSize: 16 } }> { icons[level]} </span>
        < Text strong style = {{ color: colors[level], fontSize: 12 }}>
            { level.toUpperCase() }
            </Text>
            </motion.div>
            </Badge>
            </Tooltip>
    );
};

// Export all components
export default {
    ProfessionalLoadingScreen,
    SecurityAlert,
    ConnectionStatus,
    ErrorFallback,
    RateLimitWarning,
    SecurityBadge,
    GlassCard,
    GradientButton,
    StatusIndicator
};