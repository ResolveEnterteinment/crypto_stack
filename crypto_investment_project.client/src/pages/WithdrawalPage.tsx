import React, { useState } from 'react';
import { useAuth } from '../context/AuthContext';
import WithdrawalForm from '../components/Withdrawal/WithdrawalForm';
import WithdrawalHistory from '../components/Withdrawal/WithdrawalHistory';
import {
    Typography,
    Tabs,
    message,
    Row,
    Col,
    Card,
    Space,
    Statistic,
    Alert
} from 'antd';
import {
    WalletOutlined,
    HistoryOutlined,
    InfoCircleOutlined,
    SecurityScanOutlined,
    ClockCircleOutlined,
    SafetyOutlined
} from '@ant-design/icons';
import Navbar from '../components/Navbar';
import Layout, { Content } from 'antd/es/layout/layout';

const { Title, Text } = Typography;

const WithdrawalPageContent: React.FC = () => {
    const { user } = useAuth();
    const [activeTab, setActiveTab] = useState('1');
    const [refreshHistory, setRefreshHistory] = useState<number>(0);

    const handleWithdrawalSuccess = () => {
        message.success('Withdrawal request submitted successfully');
        setActiveTab('2'); // Switch to history tab
        setRefreshHistory(prev => prev + 1); // Trigger history refresh
    };

    const handleWithdrawalError = (error: string) => {
        message.error(error || 'Failed to submit withdrawal request');
    };

    if (!user) {
        return (
            <Layout style={{ minHeight: '100vh', background: 'linear-gradient(to right, #f7fafc, #edf2f7)' }}>
                <Content style={{ padding: '50px 20px' }}>
                <Card className="text-center">
                    <Space direction="vertical" size="large">
                        <WalletOutlined style={{ fontSize: '48px', color: '#1890ff' }} />
                        <Title level={3}>Access Required</Title>
                        <Text type="secondary">Please log in to access withdrawal functionality.</Text>
                    </Space>
                </Card>
                </Content>
            </Layout>
        );
    }

    // Enhanced tab items with better content organization
    const tabItems = [
        {
            key: '1',
            label: (
                <Space>
                    <WalletOutlined />
                    <span>Withdraw Funds</span>
                </Space>
            ),
            children: (
                <>
                    <Row gutter={[24, 24]}>
                        <Col xs={24} lg={16}>
                            <WithdrawalForm
                                userId={user.id}
                                onSuccess={handleWithdrawalSuccess}
                                onError={handleWithdrawalError}
                            />
                        </Col>
                        <Col xs={24} lg={8}>
                            {/* Quick info sidebar */}
                            <Space direction="vertical" size="middle" style={{ width: '100%' }}>
                                <Card size="small" className="bg-blue-50">
                                    <Statistic
                                        title="Processing Time"
                                        value="24-48"
                                        suffix="hours"
                                        prefix={<ClockCircleOutlined />}
                                    />
                                </Card>
                                <Card size="small" className="bg-green-50">
                                    <Space direction="vertical">
                                        <Text strong>
                                            <SafetyOutlined /> Security First
                                        </Text>
                                        <Text type="secondary" style={{ fontSize: '12px' }}>
                                            All withdrawals undergo security verification
                                        </Text>
                                    </Space>
                                </Card>
                            </Space>
                        </Col>
                    </Row>
                </>
            )
        },
        {
            key: '2',
            label: (
                <Space>
                    <HistoryOutlined />
                    <span>Transaction History</span>
                </Space>
            ),
            children: <WithdrawalHistory key={refreshHistory} />
        }
    ];

    return (
        <Layout style={{ minHeight: '100vh', background: 'linear-gradient(to right, #f7fafc, #edf2f7)' }}>
            <Content style={{ padding: '50px 20px' }}>
            {/* Enhanced header */}
            <div className="mb-6">
                <Space direction="vertical" size="small">
                    <Title level={2} className="mb-0">
                        <WalletOutlined className="mr-2" />
                        Withdrawals
                    </Title>
                    <Text type="secondary">
                        Manage your withdrawal requests and view transaction history
                    </Text>
                </Space>
            </div>

            {/* Main content with improved spacing */}
            <Card
                className="mb-6"
                bodyStyle={{ padding: 0 }}
                style={{
                    borderRadius: '8px',
                    boxShadow: '0 2px 8px rgba(0,0,0,0.06)'
                }}
            >
                <Tabs
                    activeKey={activeTab}
                    onChange={setActiveTab}
                    type="card"
                    size="large"
                    items={tabItems}
                    tabBarStyle={{
                        margin: 0,
                        padding: '0 24px',
                        background: '#fafafa'
                    }}
                    tabBarGutter={0}
                />
            </Card>

            {/* Important information - made more prominent and organized */}
            <Row gutter={[16, 16]}>
                <Col xs={24} md={12}>
                    <Alert
                        message="Processing Information"
                        description={
                            <Space direction="vertical" size="small">
                                <Text>• Requests processed within 24-48 business hours</Text>
                                <Text>• All withdrawals subject to security review</Text>
                            </Space>
                        }
                        type="info"
                        showIcon
                        icon={<ClockCircleOutlined />}
                    />
                </Col>
                <Col xs={24} md={12}>
                    <Alert
                        message="Security Requirements"
                        description={
                            <Space direction="vertical" size="small">
                                <Text>• Complete KYC verification required</Text>
                                <Text>• Double-check crypto addresses and networks</Text>
                            </Space>
                        }
                        type="warning"
                        showIcon
                        icon={<SecurityScanOutlined />}
                    />
                </Col>
            </Row>

            {/* Additional help section */}
            <Card className="mt-6 bg-gray-50" size="small">
                <Row align="middle">
                    <Col flex="auto">
                        <Space>
                            <InfoCircleOutlined style={{ color: '#1890ff' }} />
                            <Text strong>Need Help?</Text>
                            <Text type="secondary">
                                Contact support if you experience any issues with your withdrawal.
                            </Text>
                        </Space>
                    </Col>
                </Row>
            </Card>
            </Content>
        </Layout>
    );
};

const WithdrawalPage: React.FC = () => {
    return (
        <>
            <Navbar />
            <div className="container mx-auto px-4 py-6 max-w-7xl">
                <WithdrawalPageContent />
            </div>
        </>
    );
}

export default WithdrawalPage;