import {
    ArrowLeftOutlined,
    CheckCircleOutlined,
    ClockCircleOutlined,
    CrownOutlined,
    IdcardOutlined,
    LockOutlined,
    ReloadOutlined,
    RocketOutlined,
    SafetyOutlined,
    SecurityScanOutlined,
    StarOutlined,
    UnlockOutlined,
} from '@ant-design/icons';
import {
    Alert, Button, Card,
    Col,
    Divider,
    Layout,
    message,
    Progress,
    Row,
    Space,
    Spin,
    Tag,
    theme,
    Tooltip,
    Typography
} from 'antd';
import { motion } from 'framer-motion';
import React, { useEffect, useMemo, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
// Import verification components based on level
import ErrorBoundry from '../components/ErrorBoundary';
import { getKycLevelValue, KYC_LEVELS } from '../components/KYC';
import AdvancedVerification from '../components/KYC/AdvancedVerification';
import BasicVerification from '../components/KYC/BasicVerification';
import EnhancedVerification from '../components/KYC/EnhancedVerification';
import StandardVerification from '../components/KYC/StandardVerification';
import Navbar from '../components/Navbar';
import kycService from '../services/kycService';
import { AdvancedVerificationData, BasicVerificationData, KycStatus, StandardVerificationData } from '../types/kyc';

const { Title, Paragraph, Text } = Typography;
const { Content } = Layout;

// Define valid KYC levels type
type ValidKycLevel = 'BASIC' | 'STANDARD' | 'ADVANCED' | 'ENHANCED';
type KycLevelWithNone = ValidKycLevel | 'NONE';

// Verification level configuration
const VERIFICATION_LEVEL_CONFIG: Record<ValidKycLevel, {
    component: React.ComponentType<any>;
    title: string;
    description: string;
    features: string[];
    color: string;
    icon: React.ReactElement;
    order: number;
    benefits: string[];
    limits: {
        daily: string;
        monthly: string;
        withdrawal: string;
    };
}> = {
    [KYC_LEVELS.BASIC]: {
        component: BasicVerification,
        title: 'Basic Verification',
        description: 'Verify your identity with basic personal information and a single document.',
        features: ['Personal Information', 'Basic Validation'],
        color: '#52c41a',
        icon: <IdcardOutlined />,
        order: 1,
        benefits: ['Basic trading limits', 'Standard withdrawal limits'],
        limits: {
            daily: '$1,000',
            monthly: '$10,000',
            withdrawal: '$500'
        }
    },
    [KYC_LEVELS.STANDARD]: {
        component: StandardVerification,
        title: 'Standard Verification',
        description: 'Complete standard verification with document validation and biometric checks.',
        features: ['Personal Information', 'ID Check', 'Document Upload', 'Biometric Verification'],
        color: '#1890ff',
        icon: <SecurityScanOutlined />,
        order: 2,
        benefits: ['Higher trading limits', 'Increased withdrawal limits', 'Access to more features'],
        limits: {
            daily: '$10,000',
            monthly: '$100,000',
            withdrawal: '$5,000'
        }
    },
    [KYC_LEVELS.ADVANCED]: {
        component: AdvancedVerification,
        title: 'Advanced Verification',
        description: 'Advanced verification with enhanced security checks and live capture.',
        features: ['Address Verification', 'Document Upload'],
        color: '#722ed1',
        icon: <SafetyOutlined />,
        order: 3,
        benefits: ['Premium trading limits', 'Priority support', 'Advanced trading features'],
        limits: {
            daily: '$50,000',
            monthly: '$500,000',
            withdrawal: '$25,000'
        }
    },
    [KYC_LEVELS.ENHANCED]: {
        component: EnhancedVerification,
        title: 'Enhanced Verification',
        description: 'Highest level verification with institutional-grade security and compliance.',
        features: ['Source of Funds', 'Enhanced Due Diligence', 'Ongoing Monitoring', 'Institutional Compliance'],
        color: '#faad14',
        icon: <CrownOutlined />,
        order: 4,
        benefits: ['Maximum trading limits', 'VIP support', 'Institutional features', 'Custom solutions'],
        limits: {
            daily: 'Unlimited',
            monthly: 'Unlimited',
            withdrawal: 'Unlimited'
        }
    }
};

// Helper function to determine level status
const getLevelStatus = (levelKey: string, kycStatusList: KycStatus[] | null): string => {
    if (!kycStatusList || kycStatusList.length === 0) {
        return levelKey === 'BASIC' ? 'available' : 'locked';
    }

    // Find KYC status for the specific level
    const levelStatus = kycStatusList.find(kyc => kyc.verificationLevel === levelKey);

    if (levelStatus) {
        // If there's a record for this level, return status based on its current state
        switch (levelStatus.status) {
            case 'APPROVED':
                return 'completed';
            case 'PENDING':
            case 'NEEDS_REVIEW':
                return 'pending';
            case 'REJECTED':
            case 'BLOCKED':
                return 'available'; // Allow retry
            default:
                return 'available';
        }
    }

    // If no record exists for this level, determine availability based on completed levels
    const approvedLevels = kycStatusList
        .filter(kyc => kyc.status === 'APPROVED')
        .map(kyc => getKycLevelValue(kyc.verificationLevel))
        .sort((a, b) => b - a); // Sort descending

    const targetLevelValue = getKycLevelValue(levelKey);
    const highestApprovedLevel = approvedLevels.length > 0 ? approvedLevels[0] : 0;

    // Check if there are any pending verification for lower level
    const hasPendingLowerLevel = kycStatusList.some(kyc =>
        (kyc.status === 'PENDING' || kyc.status === 'NEEDS_REVIEW') &&
        getKycLevelValue(kyc.verificationLevel) == targetLevelValue - 1
    );

    // Level is available if:
    // 1. It's the next level after the highest approved level, OR
    // 2. It's BASIC level and no approvals exist, OR  
    // 3. There are no pending verifications for lower levels
    if (targetLevelValue === 1 && highestApprovedLevel === 0) {
        return 'available'; // BASIC is always available if no approvals
    } else if (targetLevelValue === highestApprovedLevel + 1 || hasPendingLowerLevel) {
        return 'available'; // Next sequential level
    } else if (targetLevelValue <= highestApprovedLevel) {
        return 'available'; // Allow re-verification of lower levels
    } else {
        return 'locked'; // Higher levels that skip intermediate steps
    }
};

// Animation variants
const cardVariants = {
    hidden: { opacity: 0, y: 30, scale: 0.95 },
    visible: {
        opacity: 1,
        y: 0,
        scale: 1,
        transition: {
            duration: 0.5,
            ease: [0.4, 0.0, 0.2, 1]
        }
    },
    hover: {
        y: -5,
        scale: 1.02,
        transition: {
            duration: 0.2,
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

// KYC Level Card Component
const KycLevelCard: React.FC<{
    levelKey: ValidKycLevel;
    config: typeof VERIFICATION_LEVEL_CONFIG[ValidKycLevel];
    status: string;
    onSelectLevel: (level: ValidKycLevel) => void;
    isSelected: boolean;
    index: number;
}> = ({ levelKey, config, status, onSelectLevel, isSelected, index }) => {
    const { token } = theme.useToken();

    const getStatusIcon = () => {
        switch (status) {
            case 'completed':
                return <CheckCircleOutlined style={{ color: token.colorSuccess, fontSize: 18 }} />;
            case 'pending':
                return <ClockCircleOutlined style={{ color: token.colorWarning, fontSize: 18 }} />;
            case 'current':
                return <StarOutlined style={{ color: token.colorPrimary, fontSize: 18 }} />;
            case 'available':
                return <UnlockOutlined style={{ color: token.colorInfo, fontSize: 18 }} />;
            case 'locked':
            default:
                return <LockOutlined style={{ color: token.colorTextDisabled, fontSize: 18 }} />;
        }
    };

    const getStatusText = () => {
        switch (status) {
            case 'completed':
                return 'Completed';
            case 'pending':
                return 'Under Review';
            case 'current':
                return 'In Progress';
            case 'available':
                return 'Available';
            case 'locked':
            default:
                return 'Locked';
        }
    };

    const getStatusColor = (): 'success' | 'processing' | 'default' | 'cyan' | 'warning' => {
        switch (status) {
            case 'completed':
                return 'success';
            case 'pending':
                return 'warning';
            case 'current':
                return 'processing';
            case 'available':
                return 'cyan';
            case 'locked':
            default:
                return 'default';
        }
    };

    const isClickable = status !== 'locked' && status !== 'completed';

    return (
        <motion.div
            variants={cardVariants}
            initial="hidden"
            animate="visible"
            whileHover={isClickable ? "hover" : undefined}
            custom={index}
            style={{ marginBottom: '16px' }}
        >
            <div
                onClick={() => isClickable && onSelectLevel(levelKey)}
                style={{
                    background: 'rgba(255, 255, 255, 0.9)',
                    backdropFilter: 'blur(10px)',
                    border: isSelected
                        ? `2px solid ${token.colorPrimary}`
                        : status === 'completed'
                            ? `2px solid ${token.colorSuccess}`
                            : '1px solid rgba(255, 255, 255, 0.2)',
                    borderRadius: '16px',
                    boxShadow: isSelected
                        ? `0 8px 32px ${token.colorPrimary}30`
                        : '0 8px 32px rgba(0, 0, 0, 0.1)',
                    opacity: status === 'locked' ? 0.6 : 1,
                    cursor: status === 'locked' ? 'not-allowed' : isClickable ? 'pointer' : 'default',
                    position: 'relative',
                    overflow: 'hidden',
                    padding: '15px'
                }}
            >
                {/* Premium badge for enhanced level */}
                {levelKey === 'ENHANCED' && (
                    <div style={{
                        position: 'absolute',
                        top: 0,
                        right: 0,
                        background: 'linear-gradient(135deg, #faad14 0%, #fa8c16 100%)',
                        color: 'white',
                        padding: '4px 12px',
                        borderBottomLeftRadius: '8px',
                        fontSize: '11px',
                        fontWeight: 'bold'
                    }}>
                        PREMIUM
                    </div>
                )}

                <Row align="top" gutter={16}>
                    <Col>
                        <div style={{
                            background: `linear-gradient(135deg, ${config.color}20 0%, ${config.color}10 100%)`,
                            padding: '16px',
                            borderRadius: '12px',
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                            position: 'relative'
                        }}>
                            <div style={{
                                fontSize: '24px',
                                color: config.color,
                                filter: status === 'locked' ? 'grayscale(100%)' : 'none'
                            }}>
                                {config.icon}
                            </div>
                            {status === 'completed' && (
                                <div style={{
                                    position: 'absolute',
                                    top: '-4px',
                                    right: '-4px',
                                    background: token.colorSuccess,
                                    borderRadius: '50%',
                                    width: '20px',
                                    height: '20px',
                                    display: 'flex',
                                    alignItems: 'center',
                                    justifyContent: 'center'
                                }}>
                                    <CheckCircleOutlined style={{ fontSize: '12px', color: 'white' }} />
                                </div>
                            )}
                        </div>
                    </Col>

                    <Col flex={1}>
                        <Space direction="vertical" size="small" style={{ width: '100%' }}>
                            <div>
                                <Row justify="space-between" align="middle">
                                    <Col>
                                        <Title level={4} style={{
                                            margin: 0,
                                            opacity: status === 'locked' ? 0.5 : 1,
                                            color: status === 'locked' ? token.colorTextDisabled : undefined
                                        }}>
                                            {config.title}
                                        </Title>
                                    </Col>
                                    <Col>
                                        <Space>
                                            <Tag
                                                color={getStatusColor()}
                                                icon={getStatusIcon()}
                                                style={{ borderRadius: '12px', padding: '2px 8px' }}
                                            >
                                                {getStatusText()}
                                            </Tag>
                                            {isSelected && (
                                                <Tag color="blue" style={{ borderRadius: '12px' }}>
                                                    Selected
                                                </Tag>
                                            )}
                                        </Space>
                                    </Col>
                                </Row>

                                <Text type="secondary" style={{
                                    fontSize: '14px',
                                    opacity: status === 'locked' ? 0.5 : 0.8
                                }}>
                                    {config.description}
                                </Text>
                            </div>

                            {status !== 'locked' && (
                                <Row gutter={16} style={{ marginTop: '12px' }}>
                                    <Col xs={24} sm={12}>
                                        <div>
                                            <Text strong style={{ fontSize: '12px', color: token.colorTextSecondary }}>
                                                Withdrawal Limits:
                                            </Text>
                                            <div style={{ marginTop: '4px' }}>
                                                <Space wrap size="small">
                                                    <Tag style={{ fontSize: '11px', margin: '1px' }}>
                                                        Daily: {config.limits.daily}
                                                    </Tag>
                                                    <Tag style={{ fontSize: '11px', margin: '1px' }}>
                                                        Monthly: {config.limits.monthly}
                                                    </Tag>
                                                </Space>
                                            </div>
                                        </div>
                                    </Col>

                                    <Col xs={24} sm={12}>
                                        <div>
                                            <Text strong style={{ fontSize: '12px', color: token.colorTextSecondary }}>
                                                Benefits:
                                            </Text>
                                            <div style={{ marginTop: '4px' }}>
                                                <Space wrap size="small">
                                                    {config.benefits.slice(0, 2).map((benefit: string, idx: number) => (
                                                        <Tag key={idx} style={{ fontSize: '11px', margin: '1px' }}>
                                                            {benefit}
                                                        </Tag>
                                                    ))}
                                                    {config.benefits.length > 2 && (
                                                        <Tooltip title={config.benefits.slice(2).join(', ')}>
                                                            <Tag style={{ fontSize: '11px', margin: '1px' }}>
                                                                +{config.benefits.length - 2} more
                                                            </Tag>
                                                        </Tooltip>
                                                    )}
                                                </Space>
                                            </div>
                                        </div>
                                    </Col>
                                </Row>
                            )}
                        </Space>
                    </Col>
                </Row>
            </div>
        </motion.div>
    );
};

const KycPageContent: React.FC = () => {
    const { token } = theme.useToken();
    const { user, isLoading: authLoading } = useAuth();
    const [searchParams, setSearchParams] = useSearchParams();
    const [sessionId, setSessionId] = useState<string | null>(searchParams.get("sessionId"));
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [kycStatusList, setKycStatusList] = useState<KycStatus[] | null>(null);
    const [sessionCreating, setSessionCreating] = useState(false);
    const [showLevelSelection, setShowLevelSelection] = useState(true);
    const navigate = useNavigate();

    // Get the verification level from URL parameters, default to BASIC
    const selectedLevel: ValidKycLevel = (searchParams.get('level') as ValidKycLevel) || 'BASIC';

    // Get verification level configuration
    const levelConfig = useMemo(() => {
        return VERIFICATION_LEVEL_CONFIG[selectedLevel];
    }, [selectedLevel]);

    // Determine user's current level and available levels
    const userCurrentKyc: KycStatus | undefined = useMemo(() => {
        // FIXED: Add Array.isArray check to prevent filter error
        if (!kycStatusList || !Array.isArray(kycStatusList) || kycStatusList.length === 0) {
            return {
                status: 'NOT_STARTED',
                verificationLevel: 'NONE',
                nextSteps: []
            };
        }

        // Filter for approved KYC statuses only
        const approvedKycs = kycStatusList.filter(kyc => kyc.status === 'APPROVED');

        if (approvedKycs.length === 0) {
            // If no approved KYCs, return the most recent one (could be pending, etc.)
            return kycStatusList.sort((a, b) =>
                new Date(b.updatedAt || b.submittedAt || '').getTime() -
                new Date(a.updatedAt || a.submittedAt || '').getTime()
            )[0];
        }

        // Find the highest level approved KYC using getKycLevelValue
        return approvedKycs.reduce((highest, current) => {
            const currentLevelValue = getKycLevelValue(current.verificationLevel);
            const highestLevelValue = getKycLevelValue(highest.verificationLevel);
            return currentLevelValue > highestLevelValue ? current : highest;
        });
    }, [kycStatusList]);

    const userCurrentLevel: KycLevelWithNone = userCurrentKyc?.verificationLevel as KycLevelWithNone || 'NONE';
    const userCurrentStatus = userCurrentKyc?.status || 'NOT_STARTED';

    // Dynamic component renderer based on verification level
    const renderVerificationComponent = () => {
        const VerificationComponent = levelConfig.component;

        return (
            <motion.div
                initial={{ opacity: 0, y: 20 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.5, delay: 0.2 }}
            >
                <VerificationComponent
                    userId={user!.id}
                    sessionId={sessionId}
                    level={selectedLevel}
                    onSubmit={onSubmit}
                />
            </motion.div>
        );
    };

    const handleVerificationComplete = (result: { success: boolean; level: string }) => {
        if (result.success) {
            message.success('KYC verification completed successfully!');
            setTimeout(() => {
                navigate('/dashboard?kycCompleted=true');
            }, 2000);
        } else {
            message.error('KYC verification was not successful. Please try again.');
            setError('Verification failed. Please contact support if this issue persists.');
        }
    };

    const onSubmit = async (data: BasicVerificationData | StandardVerificationData | AdvancedVerificationData) => {
        if (!sessionId) return;
        try {
            var dataToSubmit;
            if (selectedLevel === KYC_LEVELS.BASIC) {
                dataToSubmit = data as BasicVerificationData;
            } else if (selectedLevel === KYC_LEVELS.STANDARD) {
                dataToSubmit = data as StandardVerificationData;
            } else if (selectedLevel === KYC_LEVELS.ADVANCED) {
                dataToSubmit = data as AdvancedVerificationData;
            }
            setError(null);
            const result = await kycService.submitVerification({
                sessionId: sessionId,
                verificationLevel: selectedLevel,
                data: dataToSubmit,
                consentGiven: true,
                termsAccepted: true
            });
            handleVerificationComplete({ success: result.success, level: result.level })
        } catch (err: any) {
            console.error('Verification submission error:', err);
            setError(err.message || 'Failed to submit verification. Please try again.');
        }
    }

    const handleLevelSelection = (level: ValidKycLevel) => {
        const levelStatus = getLevelStatus(level, kycStatusList);
        if (levelStatus !== 'locked') {
            setSearchParams({ level });
        }
    };

    const handleStartVerification = async (level: ValidKycLevel) => {
        try {
            setSessionCreating(true);
            setError(null);

            // Try to restore existing session
            const restoredSessionId = await kycService.restoreSession();
            if (restoredSessionId) {
                const isValid = await kycService.validateSession();
                if (isValid) {
                    setSessionId(restoredSessionId);
                    setShowLevelSelection(false);
                    setSearchParams({ sessionId: restoredSessionId, level: selectedLevel });
                } else {
                    kycService.invalidateSession(restoredSessionId);
                    setSessionId(null);
                }
            }

            const sessionResult = await kycService.createSession({
                verificationLevel: level
            });

            const session = sessionResult;

            console.log("handleStartVerification::sessionResult: ", sessionResult);
            setSessionId(session);
            setSearchParams({ sessionId: session, level });
            setShowLevelSelection(false);
            message.success('Verification session created successfully');
        } catch (err: any) {
            console.error('Session creation error:', err);
            setError(err.message || 'Failed to start verification. Please try again.');
            message.error('Failed to start verification session');
        } finally {
            setSessionCreating(false);
        }
    };

    const handleBackToLevelSelection = () => {
        setShowLevelSelection(true);
        setSessionId(null);
        setSearchParams({ level: selectedLevel });
    };

    const handleRetrySession = async () => {
        setSearchParams({ level: selectedLevel });
        await handleStartVerification(selectedLevel);
    };

    const handleCancelVerification = () => {
        const confirmed = window.confirm('Are you sure you want to cancel the verification process?');
        if (confirmed) {
            navigate('/dashboard');
        }
    };

    const loadKycStatus = async () => {
        try {
            const kycStatus = await kycService.getStatusPerLevel();
            console.log('KycPage::loadStatus => kycStatus:', kycStatus);

            // FIXED: Ensure we always set an array
            if (Array.isArray(kycStatus)) {
                setKycStatusList(kycStatus);
            } else if (kycStatus && typeof kycStatus === 'object') {
                // If single object returned, wrap in array
                setKycStatusList([kycStatus as KycStatus]);
            } else {
                setKycStatusList([]);
            }
        } catch (error) {
            console.error('Error loading KYC status:', error);
            // FIXED: Set empty array on error instead of leaving it null
            setKycStatusList([]);
            message.error('Failed to load verification status. Please refresh the page.');
        }
    };

    useEffect(() => {
        const initializePage = async () => {
            if (authLoading) return;

            if (!user?.id) {
                setError('Authentication required. Please log in.');
                setLoading(false);
                return;
            }

            try {
                await loadKycStatus();
                setLoading(false);
            } catch (error) {
                console.error('Page initialization error:', error);
                setLoading(false);
            }
        };

        initializePage();
    }, [sessionId, user?.id, authLoading, searchParams]);

    // Show loading spinner while initializing
    if (loading || authLoading) {
        return (
            <div style={{
                minHeight: '100vh',
                background: 'linear-gradient(135deg, #f8fafc 0%, #e1f5fe 50%, #e8eaf6 100%)',
                display: 'flex',
                justifyContent: 'center',
                alignItems: 'center'
            }}>
                <motion.div
                    initial={{ opacity: 0, scale: 0.9 }}
                    animate={{ opacity: 1, scale: 1 }}
                    transition={{ duration: 0.5 }}
                >
                    <Card
                        style={{
                            textAlign: 'center',
                            background: 'rgba(255, 255, 255, 0.9)',
                            backdropFilter: 'blur(10px)',
                            border: '1px solid rgba(255, 255, 255, 0.2)',
                            borderRadius: '16px',
                            boxShadow: '0 20px 40px rgba(0, 0, 0, 0.1)',
                            padding: '20px'
                        }}
                    >
                        <Spin size="large" />
                        <Title level={4} style={{ marginTop: 20, marginBottom: 8 }}>
                            {authLoading ? 'Authenticating...' : 'Loading Verification Session...'}
                        </Title>
                        <Text type="secondary">
                            Preparing your secure verification environment...
                        </Text>
                        <Progress
                            percent={authLoading ? 30 : 60}
                            status="active"
                            style={{ width: 300, marginTop: 20 }}
                            strokeColor={{
                                '0%': '#108ee9',
                                '100%': '#87d068',
                            }}
                        />
                    </Card>
                </motion.div>
            </div>
        );
    }

    // Show level selection screen
    if (showLevelSelection && !sessionId) {
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
                    <Content style={{ padding: '32px 20px' }}>
                        <Row justify="center">
                            <Col xs={24} sm={24} md={22} lg={20} xl={18}>
                                <motion.div
                                    initial={{ opacity: 0, y: 30 }}
                                    animate={{ opacity: 1, y: 0 }}
                                    transition={{ duration: 0.6, ease: [0.4, 0.0, 0.2, 1] }}
                                >
                                    <div
                                        style={{
                                            background: 'rgba(255, 255, 255, 0.9)',
                                            backdropFilter: 'blur(10px)',
                                            border: '1px solid rgba(255, 255, 255, 0.2)',
                                            borderRadius: '20px',
                                            boxShadow: '0 20px 60px rgba(0, 0, 0, 0.1)',
                                            overflow: 'hidden',
                                            marginBottom: '24px'
                                        }}
                                    >
                                        {/* Header */}
                                        <div style={{
                                            background: 'linear-gradient(135deg, #1890ff 0%, #722ed1 100%)',
                                            padding: '40px 32px',
                                            color: '#fff',
                                            textAlign: 'center',
                                            position: 'relative',
                                            overflow: 'hidden'
                                        }}>
                                            <div style={{
                                                position: 'absolute',
                                                top: '-50%',
                                                right: '-50%',
                                                width: '200%',
                                                height: '200%',
                                                background: 'radial-gradient(circle, rgba(255,255,255,0.1) 0%, transparent 70%)',
                                                transform: 'rotate(-45deg)'
                                            }} />

                                            <motion.div
                                                initial={{ scale: 0 }}
                                                animate={{ scale: 1 }}
                                                transition={{ duration: 0.6, delay: 0.3 }}
                                                style={{ position: 'relative', zIndex: 1 }}
                                            >
                                                <SafetyOutlined style={{ fontSize: '48px', marginBottom: '16px' }} />
                                                <Title level={1} style={{ margin: 0, color: '#fff', fontSize: '32px' }}>
                                                    Identity Verification
                                                </Title>
                                                <Paragraph style={{
                                                    margin: '12px 0 0 0',
                                                    color: 'rgba(255, 255, 255, 0.9)',
                                                    fontSize: '16px',
                                                    maxWidth: '600px',
                                                    marginLeft: 'auto',
                                                    marginRight: 'auto'
                                                }}>
                                                    Choose your verification level to unlock new limits and premium features
                                                </Paragraph>
                                            </motion.div>
                                        </div>

                                        <div style={{ padding: '32px' }}>
                                            {/* Progress Steps */}
                                            {kycStatusList && userCurrentStatus !== 'NOT_STARTED' && (
                                                <motion.div
                                                    initial={{ opacity: 0, y: 20 }}
                                                    animate={{ opacity: 1, y: 0 }}
                                                    transition={{ duration: 0.5, delay: 0.4 }}
                                                    style={{ marginBottom: '32px' }}
                                                >
                                                    <div
                                                        style={{
                                                            background: userCurrentStatus === 'APPROVED'
                                                                ? 'linear-gradient(135deg, #f6ffed 0%, #d9f7be 100%)'
                                                                : userCurrentStatus === 'PENDING'
                                                                    ? 'linear-gradient(135deg, #fff7e6 0%, #ffd591 100%)'
                                                                    : 'linear-gradient(135deg, #fff2f0 0%, #ffccc7 100%)',
                                                            border: `1px solid ${userCurrentStatus === 'APPROVED' ? '#b7eb8f' :
                                                                userCurrentStatus === 'PENDING' ? '#ffd591' : '#ffccc7'
                                                                }`,
                                                            borderRadius: '12px',
                                                            padding: '15px'
                                                        }}
                                                    >
                                                        <Row align="middle" gutter={16}>
                                                            <Col>
                                                                <div style={{
                                                                    background: userCurrentStatus === 'APPROVED' ? '#52c41a' :
                                                                        userCurrentStatus === 'PENDING' ? '#faad14' : '#ff4d4f',
                                                                    padding: '8px',
                                                                    borderRadius: '8px',
                                                                    color: 'white'
                                                                }}>
                                                                    {userCurrentStatus === 'APPROVED' ? <CheckCircleOutlined /> :
                                                                        userCurrentStatus === 'PENDING' ? <ClockCircleOutlined /> :
                                                                            <SafetyOutlined />}
                                                                </div>
                                                            </Col>
                                                            <Col flex={1}>
                                                                <Text strong style={{ fontSize: '16px' }}>
                                                                    Current Status: {userCurrentStatus} ({userCurrentLevel} Level)
                                                                </Text>
                                                                <br />
                                                                <Text type="secondary" style={{ fontSize: '14px' }}>
                                                                    {userCurrentStatus === 'APPROVED'
                                                                        ? 'Your verification is complete! Upgrade to unlock more benefits.'
                                                                        : userCurrentStatus === 'PENDING'
                                                                            ? 'Your verification is being reviewed. You can start a higher level in the meantime.'
                                                                            : 'Continue your verification or try a higher level.'
                                                                    }
                                                                </Text>
                                                            </Col>
                                                        </Row>
                                                    </div>
                                                </motion.div>
                                            )}

                                            {error && (
                                                <motion.div
                                                    initial={{ opacity: 0, x: -20 }}
                                                    animate={{ opacity: 1, x: 0 }}
                                                    transition={{ duration: 0.3 }}
                                                    style={{ marginBottom: '24px' }}
                                                >
                                                    <Alert
                                                        message="Error"
                                                        description={error}
                                                        type="error"
                                                        showIcon
                                                        style={{ borderRadius: '12px' }}
                                                        action={
                                                            <Button
                                                                size="small"
                                                                danger
                                                                type="text"
                                                                onClick={() => setError(null)}
                                                            >
                                                                Dismiss
                                                            </Button>
                                                        }
                                                    />
                                                </motion.div>
                                            )}

                                            {/* KYC Level Cards */}
                                            <motion.div
                                                variants={staggerChildren}
                                                initial="hidden"
                                                animate="visible"
                                                style={{ marginBottom: '32px' }}
                                            >
                                                {(Object.entries(VERIFICATION_LEVEL_CONFIG) as [ValidKycLevel, typeof VERIFICATION_LEVEL_CONFIG[ValidKycLevel]][])
                                                    .sort(([, a], [, b]) => a.order - b.order)
                                                    .map(([levelKey, config], index) => {
                                                        const status = getLevelStatus(levelKey, kycStatusList);
                                                        return (
                                                            <KycLevelCard
                                                                key={levelKey}
                                                                levelKey={levelKey}
                                                                config={config}
                                                                status={status}
                                                                onSelectLevel={handleLevelSelection}
                                                                isSelected={selectedLevel === levelKey}
                                                                index={index}
                                                            />
                                                        );
                                                    })}
                                            </motion.div>

                                            <Divider style={{ margin: '24px 0' }} />

                                            {/* Action Buttons */}
                                            <Row justify="space-between" align="middle">
                                                <Col>
                                                    <Button
                                                        size="large"
                                                        onClick={handleCancelVerification}
                                                        icon={<ArrowLeftOutlined />}
                                                        style={{ borderRadius: '8px' }}
                                                    >
                                                        Return to Dashboard
                                                    </Button>
                                                </Col>
                                                <Col>
                                                    <Space>
                                                        {kycStatusList && userCurrentStatus !== 'NOT_STARTED' && (
                                                            <Button
                                                                size="large"
                                                                onClick={loadKycStatus}
                                                                icon={<ReloadOutlined />}
                                                                style={{ borderRadius: '8px' }}
                                                            >
                                                                Refresh Status
                                                            </Button>
                                                        )}
                                                        <Button
                                                            type="primary"
                                                            size="large"
                                                            loading={sessionCreating}
                                                            onClick={() => handleStartVerification(selectedLevel)}
                                                            disabled={getLevelStatus(selectedLevel, kycStatusList) === 'locked'}
                                                            style={{
                                                                background: 'linear-gradient(135deg, #1890ff 0%, #722ed1 100%)',
                                                                border: 'none',
                                                                borderRadius: '8px',
                                                                boxShadow: '0 4px 16px rgba(24, 144, 255, 0.3)',
                                                                fontWeight: 600
                                                            }}
                                                            icon={<RocketOutlined />}
                                                        >
                                                            Start {VERIFICATION_LEVEL_CONFIG[selectedLevel]?.title}
                                                        </Button>
                                                    </Space>
                                                </Col>
                                            </Row>
                                        </div>
                                    </div>

                                    <motion.div
                                        initial={{ opacity: 0 }}
                                        animate={{ opacity: 1 }}
                                        transition={{ duration: 0.5, delay: 0.6 }}
                                        style={{ textAlign: 'center' }}
                                    >
                                        <Space>
                                            <LockOutlined style={{ color: token.colorTextSecondary }} />
                                            <Text type="secondary" style={{ fontSize: '14px' }}>
                                                Your data is protected with enterprise-grade encryption
                                            </Text>
                                        </Space>
                                    </motion.div>
                                </motion.div>
                            </Col>
                        </Row>
                    </Content>
                </Layout>
            </div>
        );
    }

    // Show verification component when session ID is available
    return (
        <div style={{
            minHeight: '100vh',
            background: 'linear-gradient(135deg, #f8fafc 0%, #e1f5fe 50%, #e8eaf6 100%)',
            padding: '80px 20px 40px',
            position: 'relative'
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
            </div>

            <Row justify="center" style={{ position: 'relative', zIndex: 1 }}>
                <Col xs={24} sm={24} md={20} lg={18} xl={16}>
                    <motion.div
                        initial={{ opacity: 0, y: 30 }}
                        animate={{ opacity: 1, y: 0 }}
                        transition={{ duration: 0.6 }}
                    >
                        <div
                            style={{
                                background: 'rgba(255, 255, 255, 0.9)',
                                backdropFilter: 'blur(10px)',
                                border: '1px solid rgba(255, 255, 255, 0.2)',
                                borderRadius: '20px',
                                boxShadow: '0 20px 60px rgba(0, 0, 0, 0.1)',
                                overflow: 'hidden'
                            }}
                        >
                            {/* Header */}
                            <div style={{
                                background: `linear-gradient(135deg, ${levelConfig.color} 0%, ${levelConfig.color}dd 100%)`,
                                padding: '24px 32px',
                                color: '#fff'
                            }}>
                                <Row align="middle" justify="space-between">
                                    <Col>
                                        <Row align="middle" gutter={16}>
                                            <Col>
                                                <div style={{
                                                    background: 'rgba(255, 255, 255, 0.2)',
                                                    padding: '12px',
                                                    borderRadius: '12px',
                                                    fontSize: '24px'
                                                }}>
                                                    {levelConfig.icon}
                                                </div>
                                            </Col>
                                            <Col>
                                                <Title level={2} style={{ margin: 0, color: '#fff' }}>
                                                    {levelConfig.title}
                                                </Title>
                                            </Col>
                                        </Row>
                                    </Col>
                                    <Col>
                                        <Space>
                                            <Button
                                                type="text"
                                                onClick={handleBackToLevelSelection}
                                                style={{ color: '#fff' }}
                                                size="small"
                                            >
                                                Change Level
                                            </Button>
                                            <Button
                                                type="text"
                                                icon={<ReloadOutlined />}
                                                onClick={handleRetrySession}
                                                style={{ color: '#fff' }}
                                                size="small"
                                            >
                                                New Session
                                            </Button>
                                        </Space>
                                    </Col>
                                </Row>
                            </div>

                            <div style={{ padding: '32px' }}>
                                <Paragraph style={{ fontSize: '16px', marginBottom: '24px', textAlign: 'center' }}>
                                    Your security is our priority. Complete the verification steps below to protect your
                                    account and comply with regulatory requirements.
                                </Paragraph>

                                <div style={{
                                    background: 'rgba(240, 245, 255, 0.6)',
                                    padding: '16px',
                                    borderRadius: '12px',
                                    marginBottom: '24px'
                                }}>
                                    <Row align="middle" justify="space-between">
                                        <Col>
                                            <Space>
                                                <ClockCircleOutlined />
                                                <Text type="secondary">
                                                    Session: {(sessionId || '').substring(0, 8)}...{(sessionId || '').substring((sessionId || '').length - 8)}
                                                </Text>
                                            </Space>
                                        </Col>
                                        <Col>
                                            <Space>
                                                <LockOutlined style={{ color: '#52c41a' }} />
                                                <Text strong style={{ color: '#52c41a' }}>
                                                    Secure Connection
                                                </Text>
                                            </Space>
                                        </Col>
                                    </Row>
                                </div>

                                <Divider style={{ margin: '16px 0 24px' }} />

                                {/* Dynamic KYC Verification Component */}
                                {renderVerificationComponent()}
                            </div>
                        </div>
                    </motion.div>
                </Col>
            </Row>
        </div>
    );
};

const KycPage: React.FC = () => {
    return (
        <ErrorBoundry>
            <Navbar />
            <div className="relative">
                <KycPageContent />
            </div>
        </ErrorBoundry>
    );
};

export default KycPage;