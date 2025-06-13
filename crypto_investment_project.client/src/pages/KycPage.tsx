import React, { useEffect, useState } from 'react';
import { useSearchParams, useNavigate } from 'react-router-dom';
import {
    Spin, Alert, Button, Card, Typography, Steps,
    Divider, Row, Col, Progress, Layout, theme
} from 'antd';
import { useAuth } from '../context/AuthContext';
import kycService from '../services/kycService';
import {
    CheckCircleOutlined, ExclamationCircleOutlined,
    ArrowLeftOutlined, SecurityScanOutlined, IdcardOutlined,
    LockOutlined, SafetyOutlined
} from '@ant-design/icons';
import KycVerification from '../components/KYC/KycVerification';
import Navbar from '../components/Navbar';

const { Title, Paragraph, Text } = Typography;
const { Step } = Steps;
const { Content } = Layout;

const KycPageContent: React.FC = () => {
    const { token } = theme.useToken();
    const { user } = useAuth();
    const [searchParams] = useSearchParams();
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const navigate = useNavigate();

    // Get the sessionId from URL parameters
    const sessionId = searchParams.get('sessionId');

    const handleVerificationComplete = () => {
        // Redirect to a success page or dashboard
        window.location.href = '/dashboard?kycVerified=true';
    };

    const handleStartNewVerification = async () => {
        try {
            setLoading(true);
            const response = await kycService.createSession({
                userId: user?.id ?? 'current',
                verificationLevel: 'STANDARD'
            });

            if (response) {
                navigate(`/kyc-verification?sessionId=${response}`);
            } else {
                setError('Failed to start verification');
                setLoading(false);
            }
        } catch (err) {
            setError('An error occurred. Please try again.');
            setLoading(false);
        }
    };

    const handleCancelVerification = () => {
        navigate(-1);
    };

    useEffect(() => {
        // Validate session parameters
        if (!sessionId) {
            setError('Invalid or missing session ID. Please start a new verification process.');
            setLoading(false);
        } else if (!user?.id) {
            setError('Authentication required. Please log in.');
            setLoading(false);
        } else {
            setLoading(false);
        }
    }, [sessionId, user?.id]);

    if (loading) {
        return (
            <Layout style={{ minHeight: '100vh', background: token.colorBgContainer }}>
                <Content className="mx-auto" style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', padding: '50px 20px' }}>
                    <div style={{ textAlign: 'center' }}>
                        <Spin size="large" />
                        <Paragraph style={{ marginTop: 20, color: token.colorTextSecondary }}>
                            Loading verification session...
                        </Paragraph>
                        <Progress percent={30} status="active" style={{ width: 300, marginTop: 20 }} />
                    </div>
                </Content>
            </Layout>
        );
    }

    if (!sessionId) {
        return (
            <Layout style={{ minHeight: '100vh', background: 'linear-gradient(to right, #f7fafc, #edf2f7)' }}>
                <Content className="mx-auto" style={{ padding: '50px 20px' }}>
                    <Row justify="center">
                        <Col xs={24} sm={24} md={18} lg={16} xl={14}>
                            <Card
                                bordered={false}
                                style={{
                                    borderRadius: '12px',
                                    boxShadow: '0 4px 20px rgba(0, 0, 0, 0.08)',
                                    overflow: 'hidden'
                                }}
                                className="verification-card"
                            >
                                <div style={{
                                    background: token.colorPrimary,
                                    margin: '-24px -24px 24px -24px',
                                    padding: '24px',
                                    color: '#fff'
                                }}>
                                    <Row align="middle" gutter={16}>
                                        <Col>
                                            <SecurityScanOutlined style={{ fontSize: 28 }} />
                                        </Col>
                                        <Col>
                                            <Title level={2} style={{ margin: 0, color: '#fff' }}>
                                                Identity Verification
                                            </Title>
                                        </Col>
                                    </Row>
                                </div>

                                <Paragraph style={{ fontSize: 16, marginBottom: 24, color: token.colorTextSecondary }}>
                                    To comply with financial regulations and protect your investments, we need to verify
                                    your identity before you can access all platform features.
                                </Paragraph>

                                {error && (
                                    <Alert
                                        message="Verification Error"
                                        description={error}
                                        type="error"
                                        showIcon
                                        icon={<ExclamationCircleOutlined />}
                                        style={{ marginBottom: 24, borderRadius: 8 }}
                                    />
                                )}

                                <Card
                                    style={{
                                        marginBottom: 24,
                                        borderRadius: 8,
                                        background: 'rgba(240, 245, 255, 0.6)'
                                    }}
                                    bordered={false}
                                >
                                    <Steps
                                        direction="vertical"
                                        current={-1}
                                        style={{ maxWidth: 700, margin: '0 auto' }}
                                    >
                                        <Step
                                            title={<Text strong>Prepare Documents</Text>}
                                            description="Have your government-issued ID and proof of address ready."
                                            icon={<IdcardOutlined />}
                                        />
                                        <Step
                                            title={<Text strong>Complete Verification</Text>}
                                            description="Follow the on-screen instructions to verify your identity."
                                            icon={<SafetyOutlined />}
                                        />
                                        <Step
                                            title={<Text strong>Get Verified</Text>}
                                            description="Once approved, you'll have full access to all platform features."
                                            icon={<CheckCircleOutlined />}
                                        />
                                    </Steps>
                                </Card>

                                <Divider style={{ margin: '24px 0' }} />

                                <Row justify="space-between">
                                    <Col>
                                        <Button
                                            type="default"
                                            size="large"
                                            onClick={handleCancelVerification}
                                            icon={<ArrowLeftOutlined />}
                                        >
                                            Return to Dashboard
                                        </Button>
                                    </Col>
                                    <Col>
                                        <Button
                                            type="primary"
                                            size="large"
                                            onClick={handleStartNewVerification}
                                            style={{
                                                background: token.colorPrimary,
                                                boxShadow: `0 2px 10px ${token.colorPrimaryActive}40`
                                            }}
                                        >
                                            Start Verification Process
                                        </Button>
                                    </Col>
                                </Row>
                            </Card>

                            <Paragraph style={{ textAlign: 'center', marginTop: 16, color: token.colorTextSecondary }}>
                                <LockOutlined /> Your data is protected with enterprise-grade encryption
                            </Paragraph>
                        </Col>
                    </Row>
                </Content>
            </Layout>
        );
    }

    return (
        <Layout style={{ minHeight: '100vh', background: 'linear-gradient(to right, #f7fafc, #edf2f7)' }}>
            <Content className="mx-auto" style={{ padding: '50px 20px' }}>
                <Row justify="center">
                    <Col xs={24} sm={24} md={20} lg={18} xl={16}>
                        <Card
                            bordered={false}
                            style={{
                                borderRadius: '12px',
                                boxShadow: '0 4px 20px rgba(0, 0, 0, 0.08)',
                                overflow: 'hidden'
                            }}
                            className="verification-card"
                        >
                            <div style={{
                                background: token.colorPrimary,
                                margin: '-24px -24px 24px -24px',
                                padding: '24px',
                                color: '#fff'
                            }}>
                                <Row align="middle" gutter={16}>
                                    <Col>
                                        <SecurityScanOutlined style={{ fontSize: 28 }} />
                                    </Col>
                                    <Col>
                                        <Title level={2} style={{ margin: 0, color: '#fff' }}>
                                            Identity Verification
                                        </Title>
                                    </Col>
                                </Row>
                            </div>

                            <Paragraph style={{ fontSize: 16, marginBottom: 24 }}>
                                Your security is our priority. Complete the verification steps below to protect your
                                account and comply with regulatory requirements.
                            </Paragraph>

                            <Divider style={{ margin: '16px 0 24px' }} />

                            <div style={{
                                background: 'rgba(240, 245, 255, 0.6)',
                                padding: 16,
                                borderRadius: 8,
                                marginBottom: 24
                            }}>
                                <Row align="middle" justify="space-between">
                                    <Col>
                                        <Text type="secondary">Session ID: {sessionId.substring(0, 8)}...{sessionId.substring(sessionId.length - 4)}</Text>
                                    </Col>
                                    <Col>
                                        <Text strong style={{ display: 'flex', alignItems: 'center' }}>
                                            <LockOutlined style={{ marginRight: 8 }} /> Secure Connection
                                        </Text>
                                    </Col>
                                </Row>
                                <Paragraph style={{ margin: '12px 0 0' }}>
                                    <strong>Security Note:</strong> We use industry-standard encryption to protect your personal information.
                                </Paragraph>
                            </div>

                            {sessionId && user?.id && (
                                <div style={{
                                    border: `1px solid ${token.colorBorder}`,
                                    borderRadius: 12,
                                    padding: 20,
                                    background: '#fff'
                                }}>
                                    <KycVerification
                                        userId={user.id}
                                        sessionId={sessionId}
                                        onComplete={handleVerificationComplete}
                                    />
                                </div>
                            )}
                        </Card>
                    </Col>
                </Row>
            </Content>
        </Layout>
    );
};

const KycPage: React.FC = () => {
    return (
        <>
            <Navbar />
            <div className="relative top-5">
                <KycPageContent />
            </div>
        </>
    );
}
export default KycPage;