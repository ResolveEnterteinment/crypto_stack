import React, { useState, useEffect } from 'react';
import {
    Card, Form, Select, InputNumber, Input, Button,
    Typography, Divider, Row, Col, Statistic, Spin,
    Alert, Space, message
} from 'antd';
import {
    WalletOutlined, BankOutlined, DollarOutlined,
    SwapOutlined, ArrowRightOutlined, InfoCircleOutlined
} from '@ant-design/icons';
import { useBalances } from '../../hooks/useBalances';
import { useBalance } from '../../hooks/useBalance';
import { WithdrawalMethod } from '../../constants/WithdrawalMethod';
import { NetworkDto, WithdrawalLimits } from '../../types/withdrawal';
import withdrawalService from '../../services/withdrawalService';
import { useAuth } from '../../context/AuthContext';

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

    //Fetch user withdrawal limits
    useEffect(() => {
        fetchWithdrawalLimits();
    }, [user]);

    const fetchWithdrawalLimits = async (): Promise<void> => {
        try {
            setIsLoadingLimits(true);
            var userLimits = await withdrawalService.getLevels();
            setLimits(userLimits);
        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : 'An unknown error occurred';
            message.error(errorMessage);
            onError(errorMessage);
        } finally {
            setIsLoadingLimits(false);
        }
    };

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

    // Handle asset selection
    const handleAssetChange = (value: string) => {
        setSelectedAsset(value);
        refetchBalance();
    };

    // Use max balance
    const useMaxBalance = () => {
        if (balance) {
            const maxAmount = balance.available - pending;
            setAmount(maxAmount);
            form.setFieldsValue({ amount: maxAmount });
        }
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

            const response = await withdrawalService.requestWithdrawal(withdrawalRequest);

            message.success('Withdrawal request submitted successfully');
            onSuccess(response);
            // Reset form
            form.resetFields();
            setAmount(null);
            setSelectedAsset('');

        } catch (error: any) {
            const errorMsg = error.response?.data?.errorMessage || 'An error occurred';
            message.error(errorMsg);
            onError(errorMsg);
        } finally {
            setIsLoading(false);
            setFormSubmitted(false);
        }
    };
    
    // Check if KYC is not completed or not at required level
    if (!limits || limits.dailyLimit === 0) {
        return (
                <Spin spinning={isLoadingLimits} tip="Loading Levels..." className="max-w-3xl mx-auto my-5" >
                    <Card title="Withdrawal Request" className="max-w-3xl mx-auto my-5" >
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
                            type="warning"
                            showIcon
                        />
                    </Card >
                </Spin>
        );
    }
    
    return (
        <Card
            className="withdrawal-card"
            title={<Title level={4}><WalletOutlined /> Withdraw Funds</Title>}
            bordered={false}
            style={{ boxShadow: '0 4px 12px rgba(0,0,0,0.08)' }}
        >
            <Spin spinning={isLoading || assetsLoading} tip="Processing...">
                <Form
                    form={form}
                    name="withdrawal_form"
                    layout="vertical"
                    requiredMark="optional"
                    onFinish={handleSubmit}
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
                            onChange={(value) => setAmount(value)}
                            precision={6}
                            min={0}
                            addonAfter={selectedAsset}
                        />
                    </Form.Item>

                    {/* Network Selection for Crypto */}
                    {isCrypto() && supportedNetworks &&(
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

                    {/* Warning for crypto withdrawals */}
                    {isCrypto() && (
                        <Alert
                            message="Important"
                            description="Please double-check the wallet address and network before confirming. Cryptocurrency transactions are irreversible."
                            type="warning"
                            showIcon
                            style={{ marginBottom: 24 }}
                        />
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
    );
};

export default WithdrawalForm;