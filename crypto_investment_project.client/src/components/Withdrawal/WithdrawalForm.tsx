import React, { useState, useEffect } from 'react';
import {
    Card, Form, Select, InputNumber, Input, Button,
    Typography, Divider, Row, Col, Statistic, Spin,
    Alert, Space, message, Slider,
    Skeleton,
    Badge,
    Tag,
    Modal
} from 'antd';
import {
    WalletOutlined, BankOutlined, DollarOutlined,
    SwapOutlined, ArrowRightOutlined, InfoCircleOutlined,
    ClockCircleOutlined,
    SafetyOutlined,
    LockOutlined,
    ExclamationCircleOutlined,
    CloseCircleOutlined,
    WarningOutlined
} from '@ant-design/icons';
import { useBalances } from '../../hooks/useBalances';
import { useBalance } from '../../hooks/useBalance';
import { WithdrawalMethod } from '../../constants/WithdrawalMethod';
import { NetworkDto, WithdrawalLimits } from '../../types/withdrawal';
import withdrawalService from '../../services/withdrawalService';
import { useAuth } from '../../context/AuthContext';
import exchangeService from '../../services/exchangeService';

interface WithdrawalFormProps {
    userId: string;
    onSuccess: (data: any) => void;
    onError: (error: string) => void;
}

const { Title, Text } = Typography;
const { Option } = Select;

const WithdrawalForm: React.FC<WithdrawalFormProps> = ({ onSuccess, onError }) => {
    // State variables
    const { user } = useAuth();
    const [limits, setLimits] = useState<WithdrawalLimits | null>(null);
    const [isLoadingLimits, setIsLoadingLimits] = useState(false);
    const [form] = Form.useForm();
    const [selectedAsset, setSelectedAsset] = useState('');
    const [amount, setAmount] = useState<number | null>(null);
    const [withdrawalMethod, setWithdrawalMethod] = useState('');
    const [selectedNetwork, setSelectedNetwork] = useState('');
    const [supportedNetworks, setSupportedNetworks] = useState<NetworkDto[] | null>([]);
    const [isLoading, setIsLoading] = useState(false);
    const [formSubmitted, setFormSubmitted] = useState(false);
    const [confirmModalOpen, setConfirmModalOpen] = useState<boolean>(false);
    const [errorModalOpen, setErrorModalOpen] = useState<boolean>(false);
    const [errorDetails, setErrorDetails] = useState<{
        title: string;
        message: string;
        suggestions: string[];
    } | null>(null);
    const [minimumWithdrawal, setMinimumWithdrawal] = useState<number>(0);
    const [maximumWithdrawal, setMaximumWithdrawal] = useState<number>(0);
    const [isLoadingMinimum, setIsLoadingMinimum] = useState(false);
    const [isLoadingMaximum, setIsLoadingMaximum] = useState(false);

    // Custom hooks for asset and balance data
    const { balances, isLoading: assetsLoading } = useBalances();
    const { balance, pending, isLoading: balanceLoading, refetch: refetchBalance } = useBalance(user?.id!, selectedAsset);

    // Determine if the selected asset is a cryptocurrency
    const isCrypto = () => {
        if (!selectedAsset || !balances) return false;
        const asset = balances.find(b => b.asset.ticker == selectedAsset)?.asset;
        return asset?.class == 'CRYPTO';
    };
    // Get selected network details
    const getSelectedNetwork = () => {
        if (!supportedNetworks || !(supportedNetworks.length > 0)) return null;
        const network = supportedNetworks.find(n => n.name == selectedNetwork) ?? null;
        return network;
    };
    const isMemoRequired = () => {
        if (!supportedNetworks || !selectedNetwork) return false;
        var network = getSelectedNetwork();
        if (network == null)
            return false;
        return network.requiresMemo;
    };

    const fetchWithdrawalLimits = async (): Promise<void> => {
        try {
            setIsLoadingLimits(true);
            var userLimits = await withdrawalService.getLevels();
            setLimits(userLimits);
            console.log("WithdrawalForm::fetchWithdrawalLimits => userLimits: ", userLimits);
        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : 'An unknown error occurred';
            message.error(errorMessage);
            onError(errorMessage);
        } finally {
            setIsLoadingLimits(false);
        }
    };

    const fetchMinimumWithdrawal = async (assetTicker: string): Promise<void> => {
        try {
            setIsLoadingMinimum(true);
            const minimum = await withdrawalService.getMinimumWithdrawalAmount(assetTicker);
            setMinimumWithdrawal(minimum);
            console.log("WithdrawalForm::fetchMinimumWithdrawal => minimum: ", minimum);
        } catch (err) {
            console.error("Error fetching minimum withdrawal:", err);
            setMinimumWithdrawal(0);
        } finally {
            setIsLoadingMinimum(false);
        }
    };

    const fetchAssetPrice = async (assetTicker: string): Promise<void> => {
        try {
            setIsLoadingMaximum(true);
            const rate = await exchangeService.getAssetPrice(assetTicker);
            const maximum = Math.trunc((limits?.dailyRemaining! / rate) * 100000000) / 100000000;
            setMaximumWithdrawal(maximum);
            console.log("WithdrawalForm::fetchMinimumWithdrawal => maximum: ", maximum);
        } catch (err) {
            console.error("Error fetching minimum withdrawal:", err);
            setMinimumWithdrawal(0);
        } finally {
            setIsLoadingMaximum(false);
        }
    };

    //Fetch user withdrawal limits
    useEffect(() => {
        fetchWithdrawalLimits();
    }, [user]);

    // Update withdrawal method when asset changes
    useEffect(() => {
        if (selectedAsset && balances) {
            const asset = balances.find(b => b.asset.ticker === selectedAsset)?.asset;

            if (asset?.class === 'CRYPTO') {
                setWithdrawalMethod(WithdrawalMethod.CryptoTransfer);
                // Load supported networks for this crypto
                fetchSupportedNetworks(selectedAsset);
                // Reset memo field when asset changes
                form.setFieldsValue({ memo: undefined });
            } else if (asset?.class === 'FIAT') {
                setWithdrawalMethod(WithdrawalMethod.BankTransfer);
                setSupportedNetworks([]);
                setSelectedNetwork('');
            }

            // Fetch minimum withdrawal for this asset
            fetchMinimumWithdrawal(selectedAsset);

            fetchAssetPrice(selectedAsset);

            // Reset amount when asset changes
            setAmount(null);
            refetchBalance();
            form.setFieldsValue({ amount: null, withdrawalAddress: undefined });
        }
    }, [selectedAsset, balances, form]);

    // Fetch supported networks for crypto assets
    const fetchSupportedNetworks = async (assetTicker: string) => {
        try {
            setIsLoading(true);
            const response = await withdrawalService.getSupportedNetworks(assetTicker);
            console.log('fetchSupportedNetworks => response: ', response);
            // Auto-select first network if available
            if (response.length > 0) {
                setSupportedNetworks(response);
                const defaultNetwork = response[0].name;
                setSelectedNetwork(defaultNetwork);
                form.setFieldsValue({ network: defaultNetwork });
            }
        } catch (error) {
            console.error('Error fetching networks:', error);
            message.error('Failed to load supported networks');
            setSupportedNetworks([]);
        } finally {
            setIsLoading(false);
        }
    };

    const formatCurrency = (amount: number): string => {
        return new Intl.NumberFormat('en-US', {
            style: 'currency',
            currency: 'USD',
            minimumFractionDigits: 2,
            maximumFractionDigits: 2,
        }).format(amount);
    };

    // Handle asset selection
    const handleAssetChange = (value: string) => {
        setSelectedAsset(value);
        refetchBalance();
    };

    // Use max balance
    const useMaxBalance = () => {
        if (balance) {
            const maxAmount = maximumWithdrawal;
            setAmount(maxAmount);
            form.setFieldsValue({ amount: maxAmount });
        }
    };

    // Calculate slider parameters
    const getSliderParams = () => {
        if (!limits || !balance || !selectedAsset) {
            return { min: 0, max: 100, marks: {} };
        }

        const availableBalance = balance.available - pending;
        const dailyRemainingUSD = limits.dailyRemaining;

        // Convert USD limits to asset amounts (assuming 1:1 for simplicity, you may need exchange rates)
        // For now, treating all amounts as USD equivalent
        const maxWithdrawable = Math.min(availableBalance, maximumWithdrawal);
        const max = Math.max(maxWithdrawable, minimumWithdrawal * 2);

        const marks: { [key: number]: any } = {};

        if (minimumWithdrawal > 0) {
            marks[minimumWithdrawal] = {
                style: { color: '#ff4d4f' },
                label: <span style={{ fontSize: '10px' }}>Min</span>
            };
        }

        if (dailyRemainingUSD > minimumWithdrawal) {
            marks[dailyRemainingUSD] = {
                style: { color: '#52c41a' },
                label: <span style={{ fontSize: '10px' }}>Daily Limit</span>
            };
        }

        if (maxWithdrawable > minimumWithdrawal && maxWithdrawable !== dailyRemainingUSD) {
            marks[maxWithdrawable] = {
                style: { color: '#1890ff' },
                label: <span style={{ fontSize: '10px' }}>Max</span>
            };
        }

        return {
            min: minimumWithdrawal,
            max: max,
            marks: marks
        };
    };

    // Handle slider change
    const handleSliderChange = (value: number) => {
        setAmount(value);
        form.setFieldsValue({ amount: value });
    };

    // Handle amount input change
    const handleAmountChange = (value: number | null) => {
        setAmount(value);
    };

    const handleConfirmWithdrawal = async (values: any): Promise<void> => {
        if (form.getFieldsValue()) {
            setConfirmModalOpen(false);
            await handleSubmit(form.getFieldsValue());
        }
    };

    const handleCloseConfirmModal = (): void => {
        setConfirmModalOpen(false);
    };

    const handleOpenConfirmModal = (): void => {
        setConfirmModalOpen(true);
    };

    // Show elegant error modal for withdrawal eligibility issues
    const showWithdrawalErrorModal = (error: any, message: string, requestedAmount: number, currency: string) => {
        let errorData = {
            title: 'Withdrawal Not Available',
            message: 'We are unable to process your withdrawal request at this time.',
            suggestions: [
                'Please verify your account information is up to date',
                'Contact support if you believe this is an error'
            ]
        };

        // Parse different types of eligibility errors
        const errorMessage = error?.response?.data?.errorMessage || error?.message || error?.toString() || '';

        if (errorMessage.toLowerCase().includes('daily limit') || errorMessage.toLowerCase().includes('limit exceeded')) {
            errorData = {
                title: 'Daily Withdrawal Limit Exceeded',
                message: `Your withdrawal of ${requestedAmount} ${currency} exceeds your current daily remaining limit of ${limits?.dailyRemaining || 'N/A'} ${currency}.`,
                suggestions: [
                    'Try withdrawing a smaller amount within your daily limit',
                    'Wait until tomorrow when your daily limit resets',
                    'Complete additional KYC verification to increase your limits',
                    'Contact support to discuss your withdrawal needs'
                ]
            };
        } else if (errorMessage.toLowerCase().includes('monthly limit') || errorMessage.toLowerCase().includes('limit exceeded')) {
            errorData = {
                title: 'Monthly Withdrawal Limit Exceeded',
                message: `Your withdrawal of ${requestedAmount} ${currency} exceeds your current monthly remaining limit of ${limits?.monthlyRemaining || 'N/A'} ${currency}.`,
                suggestions: [
                    'Try withdrawing a smaller amount within your monthly limit',
                    'Complete additional KYC verification to increase your limits',
                    'Contact support to discuss your withdrawal needs'
                ]
            };
        } else if (errorMessage.toLowerCase().includes('kyc') || errorMessage.toLowerCase().includes('verification')) {
            errorData = {
                title: 'Account Verification Required',
                message: 'Your current verification level does not allow for this withdrawal amount.',
                suggestions: [
                    'Complete additional KYC verification to increase withdrawal limits',
                    'Reduce the withdrawal amount to match your current verification level',
                    'Contact support for assistance with the verification process'
                ]
            };
        } else if (errorMessage.toLowerCase().includes('insufficient') || errorMessage.toLowerCase().includes('balance')) {
            errorData = {
                title: 'Insufficient Available Balance',
                message: `You do not have sufficient available balance to withdraw ${requestedAmount} ${currency}.`,
                suggestions: [
                    'Check that you have enough available balance (excluding pending transactions)',
                    'Wait for pending transactions to complete',
                    'Reduce the withdrawal amount'
                ]
            };
        } else if (errorMessage.toLowerCase().includes('pending') || errorMessage.toLowerCase().includes('processing')) {
            errorData = {
                title: 'Pending Transactions Limit',
                message: 'You have reached the maximum number of pending withdrawal requests.',
                suggestions: [
                    'Wait for existing withdrawal requests to complete',
                    'Cancel any unnecessary pending withdrawals',
                    'Contact support if you need to process urgent withdrawals'
                ]
            };
        } else if (errorMessage.toLowerCase().includes('remaining withdrawal')) {
            errorData = {
                title: 'Insufficient Withdrawal Limit',
                message: `Your remaining withdrawal limit of ${limits?.dailyRemaining || 'N/A'} ${currency} is less than the minimum withdrawal threshold of ${20} ${currency}.`,
                suggestions: [
                    'Cancel any unnecessary pending withdrawals',
                    'Wait until tomorrow when your daily limit resets',
                    'Complete additional KYC verification to increase your limits'
                ]
            };
        } else if (errorMessage.toLowerCase().includes('minimum') || errorMessage.toLowerCase().includes('amount')) {
            errorData = {
                title: 'Invalid Withdrawal Amount',
                message: `The withdrawal amount of ${requestedAmount} ${currency} does not meet the requirements. ${message}`,
                suggestions: [
                    'Ensure the amount is within acceptable limits',
                    'Verify that decimals are placed correctly'
                ]
            };
        }

        setErrorDetails(errorData);
        setErrorModalOpen(true);
    };

    const handleCloseErrorModal = (): void => {
        setErrorModalOpen(false);
        setErrorDetails(null);
    };

    // Handle form submission
    const handleSubmit = async (values: any) => {
        setFormSubmitted(true);

        try {
            setIsLoading(true);

            const withdrawalRequest = {
                userId: user?.id!,
                amount: values.amount,
                currency: selectedAsset,
                withdrawalMethod,
                withdrawalAddress: values.withdrawalAddress,
                additionalDetails: {
                    network: values.network,
                    memo: values.memo || null
                }
            };

            const canUserWithdraw = await withdrawalService.canUserWithdraw(withdrawalRequest.amount, withdrawalRequest.currency);

            if (!canUserWithdraw.data) {
                const error = new Error("User is not eligible to withdraw that amount.");
                const message = canUserWithdraw.message ?? ""
                showWithdrawalErrorModal(error, message, withdrawalRequest.amount, withdrawalRequest.currency);
                return;
            }

            const response = await withdrawalService.requestWithdrawal(withdrawalRequest);

            message.success('Withdrawal request submitted successfully');
            onSuccess(response);
            // Reset form
            form.resetFields();
            setAmount(null);
            setSelectedAsset('');

        } catch (error: any) {
            console.error('Withdrawal submission error:', error);

            // Show elegant error modal instead of simple message
            showWithdrawalErrorModal(error, error.message, values.amount, selectedAsset);

            // Keep the old error callback for backwards compatibility
            const errorMsg = error.response?.data?.errorMessage || error.message || 'An error occurred during withdrawal';
            onError(errorMsg);
        } finally {
            setIsLoading(false);
            setFormSubmitted(false);
        }
    };

    // Check if KYC is not completed or not at required level
    if (!limits || limits.dailyLimit === 0) {
        if (isLoadingLimits) {
            return (
                <Card title="Loading Levels..." className="max-w-6xl mx-auto my-5">
                    <Skeleton active />
                </Card>
            );
        }
        return (
            <Card title="Withdrawal Request" className="max-w-3xl mx-auto my-5"
                content="asfzdgsdgs sar ae ae ae"
            >
                <Alert
                    message="KYC Verification Required"
                    description={
                        <div>
                            <p>You need to complete KYC verification before making withdrawals.</p>
                            <p>Your current KYC level: {limits?.kycLevel || 'Not verified'}</p>
                            <p>Required KYC level: At least STANDARD</p>
                            <Button type="primary" href="/kyc-verification">
                                Complete KYC Verification
                            </Button>
                        </div>
                    }
                    icon={<LockOutlined />}
                    type="warning"
                    showIcon
                />
            </Card >
        );
    }

    const sliderParams = getSliderParams();

    return (
        <>
            <Card
                className="withdrawal-card"
                variant="borderless"
                style={{ boxShadow: '0 4px 12px rgba(0,0,0,0.08)' }}
            >
                <Spin spinning={isLoading || assetsLoading} tip="Processing...">
                    <Form
                        form={form}
                        name="withdrawal_form"
                        layout="vertical"
                        requiredMark="optional"
                        onFinish={handleOpenConfirmModal}
                        disabled={isLoading}
                    >
                        {/* Asset Selection */}
                        <Form.Item
                            name="asset"
                            label="Select Asset"
                            rules={[{ required: true, message: 'Please select an asset' }]}
                        >
                            <Select
                                placeholder="Select an asset to withdraw"
                                onChange={handleAssetChange}
                                loading={assetsLoading}
                                disabled={isLoading}
                                showSearch
                                optionFilterProp="children"
                                value={selectedAsset}
                            >
                                {balances?.map(balance => (
                                    <Option key={balance.asset.id} value={balance.asset.ticker}>
                                        <Space>
                                            {balance.asset.class === 'CRYPTO' ? <SwapOutlined /> : <DollarOutlined />}
                                            {balance.asset.name} ({balance.asset.ticker})
                                        </Space>
                                    </Option>
                                ))}
                            </Select>
                        </Form.Item>

                        {/* Balance Display */}
                        {selectedAsset && (
                            <>
                                <Divider />
                                <Row gutter={16} align="middle">
                                    <Col span={12}>
                                        <Statistic
                                            title="Available Balance"
                                            value={balanceLoading ? '-' : balance?.available! - pending || 0}
                                            suffix={selectedAsset}
                                            loading={balanceLoading}
                                            precision={6}
                                        />
                                    </Col>
                                    <Col span={12} style={{ textAlign: 'right' }}>
                                        {balance?.available! > 0 && (
                                            <Button
                                                type="link"
                                                onClick={useMaxBalance}
                                                disabled={balanceLoading || isLoading}
                                            >
                                                Use Max Balance
                                            </Button>
                                        )}
                                    </Col>
                                </Row>
                            </>
                        )}

                        {/* Amount Input */}
                        <Form.Item
                            name="amount"
                            label="Amount"
                            rules={[
                                { required: true, message: 'Please enter an amount' },
                                {
                                    type: 'number',
                                    min: 0.000001,
                                    message: 'Amount must be greater than 0'
                                },
                                (/*{ getFieldValue }*/) => ({
                                    validator(_, value) {
                                        if (!value || !balance || value <= balance.available - pending) {
                                            return Promise.resolve();
                                        }
                                        return Promise.reject(new Error('Insufficient balance'));
                                    },
                                }),
                            ]}
                        >
                            <InputNumber
                                style={{ width: '100%' }}
                                placeholder="Enter amount to withdraw"
                                disabled={!selectedAsset || balanceLoading}
                                value={amount}
                                onChange={handleAmountChange}
                                precision={6}
                                min={0}
                                addonAfter={selectedAsset}
                            />
                        </Form.Item>

                        {/* Withdrawal Amount Slider */}
                        {selectedAsset && limits && balance && !isLoadingMinimum && (
                            <Form.Item label="Withdrawal Amount Slider">
                                <div style={{ padding: '0 16px' }}>
                                    <Slider
                                        min={sliderParams.min}
                                        max={sliderParams.max}
                                        marks={sliderParams.marks}
                                        value={amount || 0}
                                        onChange={handleSliderChange}
                                        disabled={!selectedAsset || balanceLoading || isLoading}
                                        step={0.000001}
                                        tooltip={{
                                            formatter: (value) => `${value?.toFixed(6)} ${selectedAsset}`
                                        }}
                                    />
                                    <div style={{ marginTop: '8px', fontSize: '12px', color: '#666' }}>
                                        <Row justify="space-between">
                                            <Col>
                                                <Text type="danger">Min: {minimumWithdrawal?.toFixed(6)} {selectedAsset}</Text>
                                            </Col>
                                            <Col>
                                                <Text type="success">Daily Limit: {maximumWithdrawal?.toFixed(6)}</Text>
                                            </Col>
                                        </Row>
                                    </div>
                                </div>
                            </Form.Item>
                        )}

                        {/* Network Selection for Crypto */}
                        {isCrypto() && supportedNetworks && (
                            <Form.Item
                                name="network"
                                label="Network"
                                rules={[{ required: true, message: 'Please select a network' }]}
                                initialValue={supportedNetworks.length > 0 ? supportedNetworks[0]?.name : "Not available"}
                            >
                                <Select
                                    placeholder="Select network"
                                    onChange={(value) => setSelectedNetwork(value)}
                                    disabled={isLoading || !supportedNetworks || !(supportedNetworks.length > 0)}
                                >
                                    {supportedNetworks.length > 0 &&
                                        supportedNetworks.map(network => (
                                            <Option key={network.name} value={network.name}>
                                                {network.name} ({network.tokenStandard})
                                            </Option>
                                        ))
                                    }
                                </Select>
                            </Form.Item>
                        )}

                        {/* Withdrawal Address */}
                        <Form.Item
                            name="withdrawalAddress"
                            label={isCrypto() ? "Wallet Address" : "Bank Account Number"}
                            rules={[
                                { required: true, message: `Please enter your ${isCrypto() ? 'wallet address' : 'bank account number'}` },
                                { min: 5, message: 'Address must be at least 5 characters' }
                            ]}
                        >
                            <Input
                                placeholder={isCrypto() ? "Enter your wallet address" : "Enter your bank account number"}
                                disabled={!selectedAsset}
                                prefix={isCrypto() ? <WalletOutlined /> : <BankOutlined />}
                            />
                        </Form.Item>

                        {/* Memo Field (for certain crypto networks) */}
                        {isCrypto() && selectedNetwork && isMemoRequired() && (
                            <Form.Item
                                name="memo"
                                label={
                                    <Space>
                                        Memo/Tag
                                        {isMemoRequired() && (
                                            <Text type="danger">(Required)</Text>
                                        )}
                                    </Space>
                                }
                                tooltip={{
                                    title: 'Some networks require an additional memo or tag for correct routing.',
                                    icon: <InfoCircleOutlined />
                                }}
                                rules={[
                                    {
                                        required: isMemoRequired(),
                                        message: 'Memo is required for this network'
                                    }
                                ]}
                            >
                                <Input placeholder="Enter memo/tag if required" />
                            </Form.Item>
                        )}

                        {/* Submit Button */}
                        <Form.Item>
                            <Button
                                type="primary"
                                htmlType="submit"
                                loading={isLoading}
                                disabled={!selectedAsset || isLoading || formSubmitted}
                                block
                                size="large"
                                icon={<ArrowRightOutlined />}
                            >
                                Withdraw Funds
                            </Button>
                        </Form.Item>
                    </Form>
                </Spin>
            </Card>
            <Card size="small" className="bg-blue-50">
                <Statistic
                    title="Processing Time"
                    value="24-48"
                    suffix="hours"
                    prefix={<ClockCircleOutlined />}
                />
            </Card>
            <Card size="small" className="bg-green-50">
                <Space direction="horizontal">
                    <Text strong>
                        <SafetyOutlined /> Security First
                    </Text>
                    <Text type="secondary" style={{ fontSize: '12px' }}>
                        All withdrawals undergo security verification
                    </Text>
                </Space>
            </Card>

            {/* Withdraw confirmation modal */}
            <Modal
                title="Confirm Withdrawal Request"
                open={confirmModalOpen}
                onOk={handleConfirmWithdrawal}
                onCancel={handleCloseConfirmModal}
                okText="Yes, Confirm"
                cancelText="No"
                okType="primary"
            //confirmLoading={withdrawalToCancel ? cancelLoadingMap[withdrawalToCancel] : false}
            >
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                    <ExclamationCircleOutlined style={{ color: '#faad14', fontSize: '16px' }} />
                    <span>Please double-check the wallet address and network before confirming. Cryptocurrency transactions are irreversible.</span>
                </div>
                <div>Network: <Tag>{selectedAsset}</Tag></div>
                <div>Address: <Tag>{form.getFieldValue("withdrawalAddress")}</Tag></div>
            </Modal>

            {/* Elegant error modal for withdrawal eligibility issues */}
            <Modal
                title={
                    <Space>
                        <CloseCircleOutlined style={{ color: '#ff4d4f' }} />
                        {errorDetails?.title || 'Withdrawal Error'}
                    </Space>
                }
                open={errorModalOpen}
                onCancel={handleCloseErrorModal}
                footer={[
                    <Button key="close" type="primary" onClick={handleCloseErrorModal}>
                        I Understand
                    </Button>
                ]}
                width={520}
                centered
            >
                <div style={{ padding: '16px 0' }}>
                    <div style={{ marginBottom: '16px' }}>
                        <Text>{errorDetails?.message}</Text>
                    </div>

                    {errorDetails?.suggestions && errorDetails.suggestions.length > 0 && (
                        <div>
                            <div style={{ display: 'flex', alignItems: 'center', marginBottom: '12px' }}>
                                <WarningOutlined style={{ color: '#faad14', marginRight: '8px' }} />
                                <Text strong>Suggestions to resolve this issue:</Text>
                            </div>
                            <ul style={{ paddingLeft: '24px', margin: 0 }}>
                                {errorDetails.suggestions.map((suggestion, index) => (
                                    <li key={index} style={{ marginBottom: '8px' }}>
                                        <Text type="secondary">{suggestion}</Text>
                                    </li>
                                ))}
                            </ul>
                        </div>
                    )}

                    {limits && (
                        <div style={{
                            marginTop: '20px',
                            padding: '12px',
                            backgroundColor: '#f6f6f6',
                            borderRadius: '6px'
                        }}>
                            <Text strong style={{ display: 'block', marginBottom: '8px' }}>
                                Your Current Limits:
                            </Text>
                            <Row gutter={16}>
                                <Col span={12}>
                                    <Text type="secondary">Daily Limit:</Text>
                                    <div><Text strong>{formatCurrency(limits.dailyRemaining) || 'N/A'}</Text></div>
                                </Col>
                                <Col span={12}>
                                    <Text type="secondary">Monthly Limit:</Text>
                                    <div><Text strong>{formatCurrency(limits.monthlyRemaining) || 'N/A'}</Text></div>
                                </Col>
                                <Col span={12}>
                                    <Text type="secondary">KYC Level:</Text>
                                    <div><Text strong>{limits.kycLevel || 'Not verified'}</Text></div>
                                </Col>
                            </Row>
                        </div>
                    )}
                </div>
            </Modal>
        </>
    );
};

export default WithdrawalForm;