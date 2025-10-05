import React, { useState, useEffect, useCallback } from 'react';
import {
    Card, Form, Select, InputNumber, Input, Button,
    Typography, Row, Col, Spin, Space, message, Tooltip, Badge, Alert, Slider, Modal, Tag
} from 'antd';
import {
    WalletOutlined, BankOutlined, ArrowRightOutlined,
    InfoCircleOutlined, CheckCircleOutlined, WarningOutlined,
    SafetyOutlined, ExclamationCircleOutlined,
    ClockCircleOutlined
} from '@ant-design/icons';
import { useBalances } from '../../hooks/useBalances';
import { useBalance } from '../../hooks/useBalance';
import { WithdrawalMethod } from '../../constants/WithdrawalMethod';
import {
    BankWithdrawalRequest,
    CryptoWithdrawalRequest,
    NetworkDto,
    WithdrawalLimits,
    WithdrawalResponse
} from '../../types/withdrawal';
import withdrawalService from '../../services/withdrawalService';
import { useAuth } from '../../context/AuthContext';
import exchangeService from '../../services/exchangeService';
import { useAddressValidation } from '../../hooks/useAddressValidation';
import { AddressValidationService } from '../../services/addressValidationService';
import './WithdrawalForm.css';

interface WithdrawalFormProps {
    userId: string;
    onSuccess: (data: any) => void;
    onError: (error: string) => void;
}

interface ApiError {
    response?: {
        data?: {
            errorMessage?: string;
            message?: string;
            error?: string;
        };
        status?: number;
    };
    message?: string;
}

const { Text, Title } = Typography;
const { Option } = Select;

const WithdrawalForm: React.FC<WithdrawalFormProps> = ({ onSuccess, onError }) => {
    const { user } = useAuth();
    const [limits, setLimits] = useState<WithdrawalLimits | null>(null);
    const [isLoadingLimits, setIsLoadingLimits] = useState(false);
    const [form] = Form.useForm();
    const [selectedAsset, setSelectedAsset] = useState('');
    const [amount, setAmount] = useState<number | null>(null);
    const [withdrawalMethod, setWithdrawalMethod] = useState('');
    const [selectedNetwork, setSelectedNetwork] = useState<string | null>(null);
    const [supportedNetworks, setSupportedNetworks] = useState<NetworkDto[]>([]);
    const [isLoading, setIsLoading] = useState(false);
    const [minimumWithdrawal, setMinimumWithdrawal] = useState<number>(0);
    const [maximumWithdrawal, setMaximumWithdrawal] = useState<number>(0);
    const [currentError, setCurrentError] = useState<string | null>(null);
    const [confirmModalOpen, setConfirmModalOpen] = useState<boolean>(false);
    const [isLoadingMinimum, setIsLoadingMinimum] = useState(false);
    const [isLoadingMaximum, setIsLoadingMaximum] = useState(false);

    const { balances, isLoading: assetsLoading } = useBalances();
    const { balance, pending, isLoading: balanceLoading, refetch: refetchBalance } = useBalance(user?.id || '', selectedAsset);

    const {
        address: validatedAddress,
        memo: validatedMemo,
        setAddress: setValidatedAddress,
        setMemo: setValidatedMemo,
        addressValidation,
        memoValidation,
        networkInfo,
        isValid: isAddressValid,
        requiresMemo: isValidationMemoRequired
    } = useAddressValidation({
        network: selectedNetwork ?? "",
        currency: selectedAsset,
        validateOnChange: true
    });

    // Helper Functions
    const isCrypto = useCallback((): boolean => {
        if (!selectedAsset || !balances) return false;
        const asset = balances.find(b => b.asset.ticker === selectedAsset)?.asset;
        return asset?.class === 'CRYPTO';
    }, [selectedAsset, balances]);

    const getSelectedNetwork = useCallback((): NetworkDto | null => {
        if (!supportedNetworks || supportedNetworks.length === 0) return null;
        return supportedNetworks.find(n => n.name === selectedNetwork) ?? null;
    }, [supportedNetworks, selectedNetwork]);

    const isMemoRequired = useCallback((): boolean => {
        const network = getSelectedNetwork();
        return network?.requiresMemo ?? false;
    }, [getSelectedNetwork]);

    // Error Handling
    const extractErrorMessage = (error: ApiError): string => {
        // Try multiple paths to get the error message
        const errorMessage =
            error?.response?.data?.errorMessage ||
            error?.response?.data?.message ||
            error?.response?.data?.error ||
            error?.message ||
            'An unexpected error occurred';

        return errorMessage;
    };

    const showErrorNotification = useCallback((
        error: ApiError,
        requestedAmount?: number,
        currency?: string
    ): void => {
        const errorMessage = extractErrorMessage(error);
        const lowerError = errorMessage.toLowerCase();

        let title = 'Withdrawal Failed';
        let description = errorMessage;
        let duration = 6;

        // Categorize and enhance error messages
        if (lowerError.includes('daily limit') || lowerError.includes('limit exceeded')) {
            title = 'Daily Limit Exceeded';
            description = limits?.dailyRemaining
                ? `You have ${limits.dailyRemaining.toFixed(2)} ${currency || 'USD'} remaining in your daily limit.`
                : 'You have reached your daily withdrawal limit.';
        } else if (lowerError.includes('insufficient') || lowerError.includes('balance')) {
            title = 'Insufficient Balance';
            const available = balance?.available ?? 0;
            description = `Available balance: ${available.toFixed(6)} ${currency || selectedAsset}`;
        } else if (lowerError.includes('minimum')) {
            title = 'Amount Too Low';
            description = `Minimum withdrawal: ${minimumWithdrawal.toFixed(6)} ${currency || selectedAsset}`;
        } else if (lowerError.includes('maximum')) {
            title = 'Amount Too High';
            description = `Maximum withdrawal: ${maximumWithdrawal.toFixed(6)} ${currency || selectedAsset}`;
        } else if (lowerError.includes('address') || lowerError.includes('invalid')) {
            title = 'Invalid Address';
            description = 'Please verify your wallet address and try again.';
        } else if (lowerError.includes('network')) {
            title = 'Network Error';
            description = 'Please check your connection and try again.';
        } else if (lowerError.includes('memo') || lowerError.includes('tag')) {
            title = 'Memo Required';
            description = 'This network requires a memo/tag. Please provide one.';
        } else if (lowerError.includes('kyc') || lowerError.includes('verification')) {
            title = 'Verification Required';
            description = 'Please complete account verification to proceed.';
            duration = 8;
        }

        // Set error state for inline display
        setCurrentError(description);

        // Show toast notification
        message.error({
            content: (
                <div>
                    <div style={{ fontWeight: 600, marginBottom: 4 }}>{title}</div>
                    <div>{description}</div>
                </div>
            ),
            duration,
            style: {
                marginTop: '10vh',
                maxWidth: '500px'
            },
            icon: <ExclamationCircleOutlined style={{ color: '#ff4d4f' }} />
        });

        // Log for debugging
        console.error('Withdrawal Error:', {
            title,
            description,
            originalError: error,
            requestedAmount,
            currency
        });
    }, [limits, balance, minimumWithdrawal, maximumWithdrawal, selectedAsset]);

    // Data Fetching
    const fetchWithdrawalLimits = async (): Promise<void> => {
        if (!user?.id) {
            console.warn('Cannot fetch limits: User ID not available');
            return;
        }

        try {
            setIsLoadingLimits(true);
            setCurrentError(null);
            const userLimits = await withdrawalService.getCurrentUserLimits();
            setLimits(userLimits);
        } catch (err) {
            const errorMessage = extractErrorMessage(err as ApiError);
            console.error('Failed to load withdrawal limits:', err);
            message.warning('Unable to load withdrawal limits. Some features may be limited.');
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
        } catch (err) {
            console.error("Error fetching minimum withdrawal:", err);
            setMinimumWithdrawal(0);
            // Don't show error to user for this non-critical failure
        } finally {
            setIsLoadingMinimum(false);
        }
    };

    const fetchAssetPrice = async (assetTicker: string): Promise<void> => {
        if (!limits?.dailyRemaining) {
            setMaximumWithdrawal(0);
            return;
        }

        try {
            setIsLoadingMaximum(true);
            const rate = await exchangeService.getAssetPrice(assetTicker);
            if (rate > 0) {
                const maximum = Math.floor((limits.dailyRemaining / rate) * 100000000) / 100000000;
                setMaximumWithdrawal(maximum);
            } else {
                setMaximumWithdrawal(0);
            }
        } catch (err) {
            console.error("Error fetching asset price:", err);
            setMaximumWithdrawal(0);
        } finally {
            setIsLoadingMaximum(false);
        }
    };

    // Calculate slider parameters
    const getSliderParams = useCallback(() => {
        if (!limits || !balance || !selectedAsset) {
            return { min: 0, max: 100, marks: {} };
        }

        const availableBalance = Math.max(0, balance.available - pending);
        const maxWithdrawable = Math.min(availableBalance, maximumWithdrawal || availableBalance);
        const max = Math.max(maxWithdrawable, minimumWithdrawal);

        const marks: { [key: number]: any } = {};

        if (minimumWithdrawal > 0) {
            marks[minimumWithdrawal] = {
                style: { color: '#ff4d4f', fontSize: '11px' },
                label: <span>Min</span>
            };
        }

        var quarterPercentMark = (maxWithdrawable - minimumWithdrawal) * .25 + minimumWithdrawal;
        marks[quarterPercentMark] = {
            style: { color: '#ff4d4f', fontSize: '11px' },
            label: <span>25%</span>
        };

        var halfPercentMark = (maxWithdrawable - minimumWithdrawal) * .5 + minimumWithdrawal;
        marks[halfPercentMark] = {
            style: { color: '#ff4d4f', fontSize: '11px' },
            label: <span>50%</span>
        };

        var threeQuartersPercentMark = (maxWithdrawable - minimumWithdrawal) * .75 + minimumWithdrawal;
        marks[threeQuartersPercentMark] = {
            style: { color: '#ff4d4f', fontSize: '11px' },
            label: <span>75%</span>
        };

        if (maximumWithdrawal > minimumWithdrawal && maximumWithdrawal <= maxWithdrawable) {
            marks[maximumWithdrawal] = {
                style: { color: '#1890ff', fontSize: '11px' },
                label: <span>Limit</span>
            };
        }

        if (maxWithdrawable > minimumWithdrawal) {
            marks[maxWithdrawable] = {
                style: { color: '#52c41a', fontSize: '11px' },
                label: <span>Max</span>
            };
        }

        return {
            min: minimumWithdrawal || 0,
            max: max || 100,
            marks: marks
        };
    }, [limits, balance, selectedAsset, pending, minimumWithdrawal, maximumWithdrawal]);

    // Handle slider change
    const handleSliderChange = (value: number): void => {
        setAmount(value);
        form.setFieldsValue({ amount: value });
    };

    // Confirmation modal handlers
    const handleOpenConfirmModal = (): void => {
        setConfirmModalOpen(true);
    };

    const handleCloseConfirmModal = (): void => {
        setConfirmModalOpen(false);
    };

    const handleConfirmWithdrawal = async (): Promise<void> => {
        const values = form.getFieldsValue();
        setConfirmModalOpen(false);
        await handleSubmit(values);
    };

    const fetchSupportedNetworks = async (assetTicker: string): Promise<void> => {
        try {
            setIsLoading(true);
            const response = await withdrawalService.getSupportedNetworks(assetTicker);

            if (response && response.length > 0) {
                setSupportedNetworks(response);
                const defaultNetwork = response[0].name;
                setSelectedNetwork(defaultNetwork);
                form.setFieldsValue({ network: defaultNetwork });
            } else {
                setSupportedNetworks([]);
                setSelectedNetwork(null);
            }
        } catch (error) {
            console.error('Error fetching networks:', error);
            message.error('Failed to load supported networks');
            setSupportedNetworks([]);
            setSelectedNetwork(null);
        } finally {
            setIsLoading(false);
        }
    };

    // Effects
    useEffect(() => {
        if (user?.id) {
            fetchWithdrawalLimits();
        }
    }, [user?.id]);

    useEffect(() => {
        if (selectedAsset && balances) {
            const asset = balances.find(b => b.asset.ticker === selectedAsset)?.asset;

            if (asset?.class === 'CRYPTO') {
                setWithdrawalMethod(WithdrawalMethod.CryptoTransfer);
                fetchSupportedNetworks(selectedAsset);
                form.setFieldsValue({ memo: undefined });
            } else if (asset?.class === 'FIAT') {
                setWithdrawalMethod(WithdrawalMethod.BankTransfer);
                setSupportedNetworks([]);
                setSelectedNetwork(null);
            }

            fetchMinimumWithdrawal(selectedAsset);
            fetchAssetPrice(selectedAsset);
            setAmount(null);
            setCurrentError(null);
            refetchBalance();
            form.setFieldsValue({
                amount: null,
                withdrawalAddress: undefined,
                memo: undefined
            });
        }
    }, [selectedAsset, balances]);

    // Event Handlers
    const handleAssetChange = (value: string): void => {
        setSelectedAsset(value);
        setCurrentError(null);
        refetchBalance();
    };

    const useMaxBalance = (): void => {
        if (balance && balance.available > 0) {
            const availableAmount = Math.max(0, balance.available - pending);
            const maxAmount = maximumWithdrawal > 0
                ? Math.min(availableAmount, maximumWithdrawal)
                : availableAmount;

            setAmount(maxAmount);
            form.setFieldsValue({ amount: maxAmount });
        }
    };

    const handleSubmit = async (values: any): Promise<void> => {
        // Guard clause for user validation
        if (!user?.id) {
            message.error('User session expired. Please log in again.');
            return;
        }

        try {
            setIsLoading(true);
            setCurrentError(null);

            // Validate amount
            if (!values.amount || values.amount <= 0) {
                throw new Error('Please enter a valid withdrawal amount');
            }

            // Pre-submission validation
            const canUserWithdraw = await withdrawalService.canUserWithdraw(
                values.amount,
                selectedAsset
            );

            if (!canUserWithdraw.data) {
                showErrorNotification(
                    { message: canUserWithdraw.message || 'Withdrawal not allowed' } as ApiError,
                    values.amount,
                    selectedAsset
                );
                return;
            }

            let response: WithdrawalResponse | null = null;

            // Process withdrawal based on type
            if (withdrawalMethod === WithdrawalMethod.CryptoTransfer) {
                if (!values.withdrawalAddress || !values.network) {
                    throw new Error('Wallet address and network are required');
                }

                const cryptoRequest: CryptoWithdrawalRequest = {
                    userId: user.id,
                    amount: values.amount,
                    currency: selectedAsset,
                    withdrawalMethod,
                    withdrawalAddress: values.withdrawalAddress.trim(),
                    network: values.network,
                    memo: values.memo?.trim() || null
                };

                response = await withdrawalService.requestCryproWithdrawal(cryptoRequest);

            } else if (withdrawalMethod === WithdrawalMethod.BankTransfer) {
                if (!values.withdrawalAddress) {
                    throw new Error('Account number is required');
                }

                const bankRequest: BankWithdrawalRequest = {
                    userId: user.id,
                    amount: values.amount,
                    currency: selectedAsset,
                    withdrawalMethod,
                    withdrawalAddress: values.withdrawalAddress.trim(),
                };

                // Fixed: Use correct service method for bank withdrawals
                response = await withdrawalService.requestBankWithdrawal(bankRequest);
            }

            // Success handling
            message.success({
                content: 'Withdrawal request submitted successfully! Processing may take 24-48 hours.',
                duration: 5,
                style: { marginTop: '10vh' }
            });

            // Reset form
            form.resetFields();
            setAmount(null);
            setSelectedAsset('');
            setSelectedNetwork(null);
            setSupportedNetworks([]);
            setCurrentError(null);

            // Callback to parent
            if (response) {
                onSuccess(response);
            }

            // Refresh balance
            await refetchBalance();

        } catch (error: any) {
            console.error('Withdrawal submission error:', error);
            showErrorNotification(error as ApiError, values.amount, selectedAsset);

            const errorMsg = extractErrorMessage(error as ApiError);
            onError(errorMsg);
        } finally {
            setIsLoading(false);
        }
    };

    // Loading State
    if (!limits || limits.dailyLimit === 0) {
        if (isLoadingLimits) {
            return (
                <div className="withdrawal-loading">
                    <Spin size="large" tip="Loading withdrawal limits..." />
                </div>
            );
        }

        return (
            <Card className="withdrawal-kyc-required" bordered={false}>
                <div className="kyc-content">
                    <SafetyOutlined className="kyc-icon" />
                    <Title level={4}>Verification Required</Title>
                    <Text type="secondary">
                        Complete KYC verification to enable withdrawals
                    </Text>
                    <Button type="primary" size="large" href="/kyc-verification">
                        Verify Account
                    </Button>
                </div>
            </Card>
        );
    }

    // Computed Values
    const availableBalance = balance ? Math.max(0, balance.available - pending) : 0;

    const getValidationStatus = (validation: any): "success" | "error" | undefined => {
        if (!validation) return undefined;
        return validation.isValid ? "success" : "error";
    };

    const isSubmitDisabled =
        !selectedAsset ||
        isLoading ||
        !amount ||
        amount <= 0 ||
        (isCrypto() && selectedNetwork != null && !isAddressValid) ||
        availableBalance <= 0;

    return (
        <div className="withdrawal-form-wrapper">
            <Card className="withdrawal-card" bordered={false}>
                <Spin spinning={isLoading || assetsLoading} tip={isLoading ? "Processing..." : "Loading..."}>

                    {/* Error Alert */}
                    {currentError && (
                        <Alert
                            message="Withdrawal Error"
                            description={currentError}
                            type="error"
                            showIcon
                            closable
                            onClose={() => setCurrentError(null)}
                            style={{ marginBottom: 24 }}
                        />
                    )}

                    <Form
                        form={form}
                        layout="vertical"
                        onFinish={handleOpenConfirmModal}
                        disabled={isLoading}
                        requiredMark={false}
                    >
                        {/* Asset Selection */}
                        <Form.Item
                            name="asset"
                            label={<Text strong>Select Asset</Text>}
                            rules={[{ required: true, message: 'Please select an asset' }]}
                        >
                            <Select
                                size="large"
                                placeholder="Choose asset"
                                onChange={handleAssetChange}
                                loading={assetsLoading}
                                showSearch
                                optionFilterProp="children"
                                filterOption={(input, option) =>
                                    (option?.children?.toString().toLowerCase() ?? '').includes(input.toLowerCase())
                                }
                            >
                                {balances?.map(balance => (
                                    <Option key={balance.asset.id} value={balance.asset.ticker}>
                                        <Space>
                                            {balance.asset.class === 'CRYPTO' ? <WalletOutlined /> : <BankOutlined />}
                                            <span>{balance.asset.name}</span>
                                            <Text type="secondary">({balance.asset.ticker})</Text>
                                        </Space>
                                    </Option>
                                ))}
                            </Select>
                        </Form.Item>

                        {/* Balance Display */}
                        {selectedAsset && (
                            <div className="balance-section">
                                <Row align="middle" justify="space-between">
                                    <Col>
                                        <Text type="secondary" className="balance-label">Available Balance</Text>
                                        <div className="balance-amount">
                                            {balanceLoading ? (
                                                <Spin size="small" />
                                            ) : (
                                                <>
                                                    <Text strong className="balance-value">
                                                        {availableBalance.toFixed(8)}
                                                    </Text>
                                                    <Text type="secondary" className="balance-currency">
                                                        {selectedAsset}
                                                    </Text>
                                                </>
                                            )}
                                        </div>
                                    </Col>
                                    {availableBalance > 0 && (
                                        <Col>
                                            <Button
                                                type="link"
                                                onClick={useMaxBalance}
                                                disabled={balanceLoading || isLoading}
                                                className="use-max-button"
                                            >
                                                Use Max
                                            </Button>
                                        </Col>
                                    )}
                                </Row>
                            </div>
                        )}

                        {/* Amount Input */}
                        <Form.Item
                            name="amount"
                            label={<Text strong>Amount</Text>}
                            rules={[
                                { required: true, message: 'Enter withdrawal amount' },
                                { type: 'number', min: 0.000001, message: 'Amount must be greater than 0' },
                                () => ({
                                    validator(_, value) {
                                        if (!value) return Promise.resolve();

                                        if (value > availableBalance) {
                                            return Promise.reject(new Error(`Insufficient balance. Available: ${availableBalance.toFixed(8)}`));
                                        }

                                        if (minimumWithdrawal > 0 && value < minimumWithdrawal) {
                                            return Promise.reject(new Error(`Minimum withdrawal: ${minimumWithdrawal.toFixed(8)}`));
                                        }

                                        if (maximumWithdrawal > 0 && value > maximumWithdrawal) {
                                            return Promise.reject(new Error(`Maximum withdrawal: ${maximumWithdrawal.toFixed(8)}`));
                                        }

                                        return Promise.resolve();
                                    },
                                }),
                            ]}
                        >
                            <InputNumber
                                size="large"
                                placeholder="0.00000000"
                                disabled={!selectedAsset || balanceLoading}
                                value={amount}
                                onChange={setAmount}
                                precision={8}
                                min={0}
                                max={Math.min(availableBalance, maximumWithdrawal || Infinity)}
                                style={{ width: '100%' }}
                                addonAfter={selectedAsset || 'Asset'}
                            />
                        </Form.Item>

                        {/* Withdrawal Amount Slider */}
                        {selectedAsset && limits && balance && !isLoadingMinimum && !isLoadingMaximum && (
                            <Form.Item>
                                <div style={{ padding: '0 16px', marginBottom: 8 }}>
                                    <Slider
                                        min={getSliderParams().min}
                                        max={getSliderParams().max}
                                        marks={getSliderParams().marks}
                                        value={amount || 0}
                                        onChange={handleSliderChange}
                                        disabled={!selectedAsset || balanceLoading || isLoading}
                                        step={0.000001}
                                        tooltip={{
                                            formatter: (value) => `${value?.toFixed(8)} ${selectedAsset}`
                                        }}
                                    />
                                </div>
                            </Form.Item>
                        )}

                        {/* Limits Info */}
                        {selectedAsset && limits && (
                            <div className="limits-info">
                                <Row gutter={16}>
                                    <Col span={8}>
                                        <Text type="secondary" className="limit-label">Minimum</Text>
                                        <div>
                                            <Tooltip title={`${minimumWithdrawal} ${selectedAsset}`}>
                                                <Text className="limit-value">
                                                    {minimumWithdrawal.toFixed(6)}
                                                </Text>
                                            </Tooltip>
                                        </div>
                                    </Col>
                                    <Col span={8}>
                                        <Text type="secondary" className="limit-label">Maximum</Text>
                                        <div>
                                            <Tooltip title={`${maximumWithdrawal} ${selectedAsset}`}>
                                                <Text className="limit-value">
                                                    {maximumWithdrawal.toFixed(6)}
                                                </Text>
                                            </Tooltip>
                                        </div>
                                    </Col>
                                    <Col span={8}>
                                        <Text type="secondary" className="limit-label">Daily Limit</Text>
                                        <div>
                                            <Tooltip title={`${limits.dailyRemaining.toFixed(2)} USD remaining`}>
                                                <Text className="limit-value">
                                                    ${limits.dailyRemaining.toFixed(2)}
                                                </Text>
                                            </Tooltip>
                                        </div>
                                    </Col>
                                </Row>
                            </div>
                        )}

                        {/* Network Selection */}
                        {isCrypto() && supportedNetworks.length > 0 && (
                            <Form.Item
                                name="network"
                                label={
                                    <Space>
                                        <Text strong>Network</Text>
                                        <Tooltip title="Select the blockchain network for this withdrawal">
                                            <InfoCircleOutlined style={{ color: '#8c8c8c' }} />
                                        </Tooltip>
                                    </Space>
                                }
                                rules={[{ required: true, message: 'Select network' }]}
                            >
                                <Select
                                    size="large"
                                    onChange={(value) => {
                                        setSelectedNetwork(value);
                                        // Clear address when network changes
                                        form.setFieldsValue({ withdrawalAddress: undefined, memo: undefined });
                                        setValidatedAddress('');
                                        setValidatedMemo('');
                                    }}
                                    disabled={isLoading}
                                >
                                    {supportedNetworks.map(network => (
                                        <Option key={network.name} value={network.name}>
                                            <Space>
                                                <span>{network.name}</span>
                                                <Text type="secondary">({network.tokenStandard})</Text>
                                                {network.requiresMemo && (
                                                    <Badge status="warning" text="Memo Required" />
                                                )}
                                            </Space>
                                        </Option>
                                    ))}
                                </Select>
                            </Form.Item>
                        )}

                        {/* Wallet Address */}
                        <Form.Item
                            name="withdrawalAddress"
                            label={
                                <Space>
                                    <Text strong>{isCrypto() ? "Wallet Address" : "Account Number"}</Text>
                                    {networkInfo && (
                                        <Tooltip title={`Expected format: ${networkInfo.addressFormat}`}>
                                            <InfoCircleOutlined style={{ color: '#8c8c8c' }} />
                                        </Tooltip>
                                    )}
                                </Space>
                            }
                            validateStatus={getValidationStatus(addressValidation)}
                            help={
                                addressValidation && !addressValidation.isValid && addressValidation.errors.length > 0 ? (
                                    <div className="validation-help">
                                        {addressValidation.errors[0]}
                                    </div>
                                ) : networkInfo ? (
                                    <Text type="secondary" style={{ fontSize: 12 }}>
                                        Expected: {networkInfo.addressFormat} format
                                    </Text>
                                ) : null
                            }
                            rules={[
                                { required: true, message: `Enter ${isCrypto() ? 'wallet address' : 'account number'}` },
                                {
                                    min: 5,
                                    message: `${isCrypto() ? 'Address' : 'Account number'} is too short`
                                },
                                {
                                    validator: async (_, value) => {
                                        if (!value || !selectedNetwork || !isCrypto()) {
                                            return Promise.resolve();
                                        }

                                        const validation = AddressValidationService.validateAddress(
                                            value.trim(),
                                            selectedNetwork,
                                            selectedAsset
                                        );

                                        if (!validation.isValid) {
                                            return Promise.reject(new Error(validation.errors[0] || 'Invalid address format'));
                                        }

                                        return Promise.resolve();
                                    }
                                }
                            ]}
                        >
                            <Input
                                placeholder={networkInfo ? `Enter ${networkInfo.addressFormat} address` : "Enter address"}
                                disabled={!selectedAsset || (isCrypto() && !selectedNetwork)}
                                prefix={isCrypto() ? <WalletOutlined /> : <BankOutlined />}
                                onChange={(e) => {
                                    const value = e.target.value.trim();
                                    form.setFieldsValue({ withdrawalAddress: value });
                                    setValidatedAddress(value);
                                }}
                                onPaste={(e) => {
                                    // Clean pasted content
                                    e.preventDefault();
                                    const pastedText = e.clipboardData.getData('text').trim();
                                    form.setFieldsValue({ withdrawalAddress: pastedText });
                                    setValidatedAddress(pastedText);
                                }}
                                suffix={
                                    addressValidation && (
                                        addressValidation.isValid ? (
                                            <CheckCircleOutlined className="validation-icon success" />
                                        ) : (
                                            <WarningOutlined className="validation-icon error" />
                                        )
                                    )
                                }
                                autoComplete="off"
                            />
                        </Form.Item>

                        {/* Memo/Tag Field */}
                        {isCrypto() && selectedNetwork && (isMemoRequired() || isValidationMemoRequired) && (
                            <Form.Item
                                name="memo"
                                label={
                                    <Space>
                                        <Text strong>Memo/Tag</Text>
                                        {(isMemoRequired() || isValidationMemoRequired) && (
                                            <Badge status="error" text="Required" />
                                        )}
                                        <Tooltip title="Some networks require a memo/tag for deposits. Double-check with recipient.">
                                            <InfoCircleOutlined style={{ color: '#8c8c8c' }} />
                                        </Tooltip>
                                    </Space>
                                }
                                validateStatus={getValidationStatus(memoValidation)}
                                help={
                                    memoValidation && !memoValidation.isValid && memoValidation.errors.length > 0 ? (
                                        <div className="validation-help">
                                            {memoValidation.errors[0]}
                                        </div>
                                    ) : (
                                        <Text type="secondary" style={{ fontSize: 12 }}>
                                            Required for this network - verify with recipient
                                        </Text>
                                    )
                                }
                                rules={[
                                    {
                                        required: isMemoRequired() || isValidationMemoRequired,
                                        message: 'Memo/tag is required for this network'
                                    }
                                ]}
                            >
                                <Input
                                    size="large"
                                    placeholder="Enter memo/tag (if required)"
                                    onChange={(e) => {
                                        const value = e.target.value.trim();
                                        form.setFieldsValue({ memo: value });
                                        setValidatedMemo(value);
                                    }}
                                    suffix={
                                        memoValidation && (
                                            memoValidation.isValid ? (
                                                <CheckCircleOutlined className="validation-icon success" />
                                            ) : (
                                                <WarningOutlined className="validation-icon error" />
                                            )
                                        )
                                    }
                                    autoComplete="off"
                                />
                            </Form.Item>
                        )}

                        {/* Submit Button */}
                        <Form.Item className="submit-section">
                            <Button
                                type="primary"
                                htmlType="submit"
                                loading={isLoading}
                                disabled={isSubmitDisabled}
                                block
                                size="large"
                                icon={<ArrowRightOutlined />}
                                className="submit-button"
                            >
                                {isLoading ? 'Processing...' : 'Withdraw Funds'}
                            </Button>
                        </Form.Item>
                    </Form>
                </Spin>
            </Card>

            {/* Withdrawal Confirmation Modal */}
            <Modal
                title="Confirm Withdrawal"
                open={confirmModalOpen}
                onOk={handleConfirmWithdrawal}
                onCancel={handleCloseConfirmModal}
                okText="Confirm"
                cancelText="Cancel"
                okType="primary"
                width={520}
                centered
            >
                <Space direction="vertical" style={{ width: '100%' }} size="middle">
                    <Space direction="horizontal">
                        <Text type="secondary" style={{ fontSize: 14 }}>Amount</Text>
                        <div>
                            <Text strong style={{ fontSize: 18 }}>
                                {amount?.toFixed(8)} {selectedAsset}
                            </Text>
                        </div>
                    </Space>
                    {isCrypto() && (
                        <>
                            <Space direction="horizontal">
                                <Text type="secondary" style={{ fontSize: 14 }}>Network</Text>
                                <div>
                                    <Text strong style={{ fontSize: 18 }}>{selectedNetwork}</Text>

                                </div>
                            </Space>

                            <Space direction="vertical">
                                <Text type="secondary" style={{ fontSize: 14 }}>Wallet Address</Text>
                                <Text style={{
                                    background: '#f5f5f5',
                                    padding: '12px 12px 12px 12px',
                                    borderRadius: 10,
                                    wordBreak: 'keep-all',
                                    fontFamily: 'monospace',
                                    fontSize: 14
                                }}>
                                    {validatedAddress || form.getFieldValue("withdrawalAddress")}
                                </Text>
                            </Space>

                            {(validatedMemo || form.getFieldValue("memo")) && (
                                <Space direction="horizontal">
                                    <Text type="secondary" style={{ fontSize: 12 }}>Memo/Tag</Text>
                                    <div>
                                        <Tag color="orange">{validatedMemo || form.getFieldValue("memo")}</Tag>
                                    </div>
                                </Space>
                            )}
                        </>
                    )}

                    {!isCrypto() && (
                        <div>
                            <Text type="secondary" style={{ fontSize: 14 }}>Account Number</Text>
                            <div style={{
                                background: '#f5f5f5',
                                padding: '8px 12px',
                                paddingBottom : '12px',
                                borderRadius: 4,
                                fontFamily: 'monospace'
                            }}>
                                <Text>{form.getFieldValue("withdrawalAddress")}</Text>
                            </div>
                        </div>
                    )}
                </Space>
                <div style={{paddingTop:'20px'} }>
                    <Space direction="horizontal" style={{ width: '100%' }} size="small">
                        <WarningOutlined />
                        <Text type="secondary" style={{ fontSize: 12 }}>Cryptocurrency transactions are irreversible. Please double-check all information before confirming.</Text>
                    </Space>
                    <Space direction="horizontal" style={{ width: '100%' }} size="small">
                        <SafetyOutlined color = "#ff0000" />
                        <Text type="secondary" style={{ fontSize: 12 }}>All transactions are verified for security.</Text>
                    </Space>
                    <Space direction="horizontal" style={{ width: '100%' }} size="small">
                        <ClockCircleOutlined color="#ff0000" />
                        <Text type="secondary" style={{ fontSize: 12 }}>Processing Time: 24-48 hours.</Text>
                    </Space>
                </div>
            </Modal>
        </div>
    );
};

export default WithdrawalForm;