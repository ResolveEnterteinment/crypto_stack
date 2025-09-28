import React, { useState } from 'react';
import { useAuth } from '../context/AuthContext';
import WithdrawalForm from '../components/Withdrawal/WithdrawalForm';
import WithdrawalHistory from '../components/Withdrawal/WithdrawalHistory';
import WithdrawalLimitsComponent from '../components/Withdrawal/WithdrawalLimitsComponent';
import {
    Typography,
    Tabs,
    message,
    Row,
    Col,
    Card,
    Space,
} from 'antd';
import {
    WalletOutlined,
    HistoryOutlined,
    InfoCircleOutlined,
} from '@ant-design/icons';
import Navbar from '../components/Navbar';
import Layout, { Content } from 'antd/es/layout/layout';
import ProtectedRoute from '../components/ProtectedRoute';

const { Title, Text } = Typography;

const WithdrawalPageContent: React.FC = () => {
    const { user } = useAuth();
    const [activeTab, setActiveTab] = useState('1');
    const [refreshHistory, setRefreshHistory] = useState<number>(0);
    const [refreshLimits, setRefreshLimits] = useState<number>(0);

    const handleLimitsSuccess = () => {
        message.success('Fetched withdrawal limits successfully');
    };

    const handleLimitsError = (error: string) => {
        message.error(error || 'Failed to fetch withdrawal limits');
    };

    const handleWithdrawalSuccess = () => {
        message.success('Withdrawal request submitted successfully');
        setActiveTab('3'); // Switch to history tab
        setRefreshHistory(prev => prev + 1); // Trigger history refresh
        setRefreshLimits(prev => prev + 1); // Trigger limits refresh
    };

    const handleWithdrawalError = (error: string) => {
        message.error(error || 'Failed to submit withdrawal request');
        //setRefreshHistory(prev => prev + 1); // Trigger history refresh
    };

    const handleWithdrawalCancelled = () => {
        setRefreshHistory(prev => prev + 1); // Trigger history refresh
        setRefreshLimits(prev => prev + 1); // Trigger limits refresh
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
                    <span>Withdraw Limits</span>
                </Space>
            ),
            children: (
                <WithdrawalLimitsComponent
                    key={refreshLimits}
                    userId={user.id}
                    onSuccess={handleLimitsSuccess}
                    onError={handleLimitsError}
                />
            )
        },
        {
            key: '2',
            label: (
                <Space>
                    <WalletOutlined />
                    <span>Withdraw Funds</span>
                </Space>
            ),
            children: (
                <ProtectedRoute requiredKycLevel = "BASIC">
                    <WithdrawalForm
                        userId={user.id}
                        onSuccess={handleWithdrawalSuccess}
                        onError={handleWithdrawalError}
                        />
                </ProtectedRoute>
            )
        },
        {
            key: '3',
            label: (
                <Space>
                    <HistoryOutlined />
                    <span>Transaction History</span>
                </Space>
            ),
            children: <WithdrawalHistory key={refreshHistory} onWithdrawalCancelled={handleWithdrawalCancelled} />
        }
    ];

    return (
        <Layout style={{ minHeight: '100vh', background: 'linear-gradient(to right, #f7fafc, #edf2f7)' }}>
            <Content className="max-w-7xl mx-auto" style={{ padding: '50px 20px' }}>
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
            <div className="relative top-5">
                <WithdrawalPageContent />
            </div>
        </>
    );
}

export default WithdrawalPage;