import React, { useState } from 'react';
import { useAuth } from '../context/AuthContext';
import ErrorBoundry from '../components/ErrorBoundary';
import WithdrawalForm from '../components/Withdrawal/WithdrawalForm';
import WithdrawalHistory from '../components/Withdrawal/WithdrawalHistory';
import WithdrawalLimitsComponent from '../components/Withdrawal/WithdrawalLimitsComponent';
import {
    Typography,
    message,
    Space,
    Segmented,
} from 'antd';
import {
    WalletOutlined,
    HistoryOutlined,
    BarChartOutlined,
    SafetyOutlined,
} from '@ant-design/icons';
import Navbar from '../components/Navbar';
import Layout, { Content } from 'antd/es/layout/layout';
import ProtectedRoute from '../components/ProtectedRoute';
import { KYC_LEVELS } from '../components/KYC';
import './WithdrawalPage.css';

const { Title, Text } = Typography;

const WithdrawalPageContent: React.FC = () => {
    const { user } = useAuth();
    const [activeTab, setActiveTab] = useState<string>('withdraw');
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
        setActiveTab('history');
        setRefreshHistory(prev => prev + 1);
        setRefreshLimits(prev => prev + 1);
    };

    const handleWithdrawalError = (error: string) => {
        message.error(error || 'Failed to submit withdrawal request');
    };

    const handleWithdrawalCancelled = () => {
        setRefreshHistory(prev => prev + 1);
        setRefreshLimits(prev => prev + 1);
    };

    if (!user) {
        return (
            <Layout className="withdrawal-page-layout">
                <Content className="withdrawal-content">
                    <div className="access-required-card">
                        <WalletOutlined className="access-icon" />
                        <Title level={3}>Access Required</Title>
                        <Text type="secondary">Please log in to access withdrawal functionality.</Text>
                    </div>
                </Content>
            </Layout>
        );
    }

    const renderContent = () => {
        switch (activeTab) {
            case 'limits':
                return (
                    <WithdrawalLimitsComponent
                        key={refreshLimits}
                        userId={user.id}
                        onSuccess={handleLimitsSuccess}
                        onError={handleLimitsError}
                    />
                );
            case 'withdraw':
                return (
                    <ProtectedRoute requiredKycLevel={KYC_LEVELS.BASIC}>
                        <WithdrawalForm
                            userId={user.id}
                            onSuccess={handleWithdrawalSuccess}
                            onError={handleWithdrawalError}
                        />
                    </ProtectedRoute>
                );
            case 'history':
                return (
                    <WithdrawalHistory
                        key={refreshHistory}
                        onWithdrawalCancelled={handleWithdrawalCancelled}
                    />
                );
            default:
                return null;
        }
    };

    return (
        <Layout className="withdrawal-page-layout">
            <Content className="withdrawal-content">
                <div className="withdrawal-page-container">
                    {/* Header */}
                    <div className="withdrawal-header">
                        <div className="header-content">
                            <WalletOutlined className="header-icon" />
                            <div>
                                <Title level={2} className="header-title">Withdrawals</Title>
                                <Text type="secondary" className="header-subtitle">
                                    Manage your funds securely
                                </Text>
                            </div>
                        </div>
                    </div>

                    {/* Tab Navigation - Apple-style Segmented Control */}
                    <div className="tab-navigation">
                        <Segmented
                            value={activeTab}
                            onChange={setActiveTab}
                            size="large"
                            options={[
                                {
                                    label: (
                                        <div className="segment-option">
                                            <BarChartOutlined />
                                            <span>Limits</span>
                                        </div>
                                    ),
                                    value: 'limits',
                                },
                                {
                                    label: (
                                        <div className="segment-option">
                                            <WalletOutlined />
                                            <span>Withdraw</span>
                                        </div>
                                    ),
                                    value: 'withdraw',
                                },
                                {
                                    label: (
                                        <div className="segment-option">
                                            <HistoryOutlined />
                                            <span>History</span>
                                        </div>
                                    ),
                                    value: 'history',
                                },
                            ]}
                            block
                        />
                    </div>

                    {/* Content Area */}
                    <div className="tab-content">
                        {renderContent()}
                    </div>

                    {/* Help Section */}
                    <div className="help-section">
                        <SafetyOutlined />
                        <div className="help-text">
                            <Text strong>Need Help?</Text>
                            <Text type="secondary">
                                Contact support if you experience any issues with your withdrawal.
                            </Text>
                        </div>
                    </div>
                </div>
            </Content>
        </Layout>
    );
};

const WithdrawalPage: React.FC = () => {
    return (
        <ErrorBoundry>
            <Navbar />
            <div className="page-wrapper">
                <WithdrawalPageContent />
            </div>
        </ErrorBoundry>
    );
};

export default WithdrawalPage;