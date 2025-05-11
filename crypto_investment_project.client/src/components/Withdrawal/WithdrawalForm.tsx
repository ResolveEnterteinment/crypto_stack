import React, { useState, useEffect } from 'react';
import { Form, Input, Select, Button, Card, Alert, InputNumber, Skeleton, Typography } from 'antd';
import { useAuth } from '../../context/AuthContext';
import { WalletOutlined, BankOutlined, DollarOutlined } from '@ant-design/icons';
import { WithdrawalLimits, WithdrawalFormValues } from '../../types/withdrawal';

const { Option } = Select;
const { Title, Text } = Typography;

const WithdrawalForm: React.FC = () => {
    const { user } = useAuth();
    const [form] = Form.useForm();
    const [loading, setLoading] = useState<boolean>(true);
    const [submitting, setSubmitting] = useState<boolean>(false);
    const [error, setError] = useState<string | null>(null);
    const [success, setSuccess] = useState<string | null>(null);
    const [limits, setLimits] = useState<WithdrawalLimits | null>(null);
    const [withdrawalMethod, setWithdrawalMethod] = useState<string>('CRYPTO_TRANSFER');

    useEffect(() => {
        fetchWithdrawalLimits();
    }, []);

    const fetchWithdrawalLimits = async (): Promise<void> => {
        try {
            setLoading(true);
            const response = await fetch('/api/withdrawal/limits', {
                headers: {
                    'Authorization': `Bearer ${localStorage.getItem('token')}`,
                },
            });

            if (!response.ok) {
                throw new Error('Failed to fetch withdrawal limits');
            }

            const data: WithdrawalLimits = await response.json();
            setLimits(data);
        } catch (err) {
            const error = err as Error;
            setError(error.message);
        } finally {
            setLoading(false);
        }
    };

    const handleSubmit = async (values: WithdrawalFormValues): Promise<void> => {
        try {
            setError(null);
            setSuccess(null);
            setSubmitting(true);

            // Check if amount exceeds limits
            if (limits && values.amount > limits.dailyRemaining) {
                setError(`Amount exceeds your daily withdrawal limit. Maximum: ${limits.dailyRemaining} ${values.currency}`);
                return;
            }

            if (limits && values.amount > limits.monthlyRemaining) {
                setError(`Amount exceeds your monthly withdrawal limit. Maximum: ${limits.monthlyRemaining} ${values.currency}`);
                return;
            }

            const response = await fetch('/api/withdrawal/request', {
                method: 'POST',
                headers: {
                    'Authorization': `Bearer ${localStorage.getItem('token')}`,
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify({
                    amount: values.amount,
                    currency: values.currency,
                    withdrawalMethod: values.withdrawalMethod,
                    withdrawalAddress: values.withdrawalAddress,
                    additionalDetails: {
                        ...(values.withdrawalMethod === 'BANK_TRANSFER' && {
                            bankName: values.bankName,
                            accountNumber: values.accountNumber,
                            routingNumber: values.routingNumber,
                            accountHolder: values.accountHolder,
                        }),
                    },
                }),
            });

            if (!response.ok) {
                const errorData = await response.json();
                throw new Error(errorData.message || 'Failed to create withdrawal request');
            }

            const data = await response.json();
            setSuccess(`Withdrawal request created successfully. Reference ID: ${data.id}`);
            form.resetFields();
        } catch (err) {
            const error = err as Error;
            setError(error.message);
        } finally {
            setSubmitting(false);
        }
    };

    const handleWithdrawalMethodChange = (value: string): void => {
        setWithdrawalMethod(value);
    };

    if (loading) {
        return (
            <Card title="Withdrawal Request" className="max-w-3xl mx-auto my-5">
                <Skeleton active />
            </Card>
        );
    }

    // Check if KYC is not completed or not at required level
    if (!limits || limits.dailyLimit === 0) {
        return (
            <Card title="Withdrawal Request" className="max-w-3xl mx-auto my-5">
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
            </Card>
        );
    }

    return (
        <Card title="Withdrawal Request" className="max-w-3xl mx-auto my-5">
            {error && (
                <Alert
                    message="Error"
                    description={error}
                    type="error"
                    showIcon
                    className="mb-4"
                    closable
                    onClose={() => setError(null)}
                />
            )}

            {success && (
                <Alert
                    message="Success"
                    description={success}
                    type="success"
                    showIcon
                    className="mb-4"
                    closable
                    onClose={() => setSuccess(null)}
                />
            )}

            <div className="mb-6 p-4 bg-blue-50 rounded-lg">
                <Title level={5}>Your Withdrawal Limits</Title>
                <div className="grid grid-cols-2 gap-4">
                    <div>
                        <Text type="secondary">KYC Level:</Text>
                        <div><Text strong>{limits.kycLevel}</Text></div>
                    </div>
                    <div>
                        <Text type="secondary">Reset Date:</Text>
                        <div><Text strong>{new Date(limits.periodResetDate).toLocaleDateString()}</Text></div>
                    </div>
                    <div>
                        <Text type="secondary">Daily Limit:</Text>
                        <div><Text strong>${limits.dailyLimit.toFixed(2)}</Text></div>
                    </div>
                    <div>
                        <Text type="secondary">Daily Remaining:</Text>
                        <div><Text strong>${limits.dailyRemaining.toFixed(2)}</Text></div>
                    </div>
                    <div>
                        <Text type="secondary">Monthly Limit:</Text>
                        <div><Text strong>${limits.monthlyLimit.toFixed(2)}</Text></div>
                    </div>
                    <div>
                        <Text type="secondary">Monthly Remaining:</Text>
                        <div><Text strong>${limits.monthlyRemaining.toFixed(2)}</Text></div>
                    </div>
                </div>
            </div>

            <Form
                form={form}
                layout="vertical"
                onFinish={handleSubmit}
                initialValues={{
                    currency: 'USD',
                    withdrawalMethod: 'CRYPTO_TRANSFER',
                }}
            >
                <Form.Item
                    name="amount"
                    label="Amount"
                    rules={[
                        { required: true, message: 'Please enter the amount' },
                        { type: 'number', min: 10, message: 'Amount must be at least 10' },
                        { type: 'number', max: limits.dailyRemaining, message: `Cannot exceed daily limit of ${limits.dailyRemaining}` },
                    ]}
                >
                    <InputNumber
                        style={{ width: '100%' }}
                        placeholder="Enter withdrawal amount"
                        prefix={<DollarOutlined />}
                        precision={2}
                        min={10}
                        max={Math.min(limits.dailyRemaining, limits.monthlyRemaining)}
                    />
                </Form.Item>

                <Form.Item
                    name="currency"
                    label="Currency"
                    rules={[{ required: true, message: 'Please select currency' }]}
                >
                    <Select placeholder="Select currency">
                        <Option value="USD">USD</Option>
                        <Option value="EUR">EUR</Option>
                        <Option value="GBP">GBP</Option>
                        <Option value="BTC">Bitcoin (BTC)</Option>
                        <Option value="ETH">Ethereum (ETH)</Option>
                    </Select>
                </Form.Item>

                <Form.Item
                    name="withdrawalMethod"
                    label="Withdrawal Method"
                    rules={[{ required: true, message: 'Please select withdrawal method' }]}
                >
                    <Select
                        placeholder="Select withdrawal method"
                        onChange={handleWithdrawalMethodChange}
                    >
                        <Option value="CRYPTO_TRANSFER">Crypto Transfer</Option>
                        <Option value="BANK_TRANSFER">Bank Transfer</Option>
                    </Select>
                </Form.Item>

                {withdrawalMethod === 'CRYPTO_TRANSFER' && (
                    <Form.Item
                        name="withdrawalAddress"
                        label="Crypto Address"
                        rules={[
                            { required: true, message: 'Please enter your crypto address' },
                            { min: 26, message: 'Invalid crypto address length' },
                        ]}
                        tooltip="Enter the wallet address where you want to receive your funds"
                    >
                        <Input
                            placeholder="Enter your crypto wallet address"
                            prefix={<WalletOutlined />}
                        />
                    </Form.Item>
                )}

                {withdrawalMethod === 'BANK_TRANSFER' && (
                    <>
                        <Form.Item
                            name="bankName"
                            label="Bank Name"
                            rules={[{ required: true, message: 'Please enter your bank name' }]}
                        >
                            <Input
                                placeholder="Enter your bank name"
                                prefix={<BankOutlined />}
                            />
                        </Form.Item>

                        <Form.Item
                            name="accountHolder"
                            label="Account Holder Name"
                            rules={[{ required: true, message: 'Please enter account holder name' }]}
                        >
                            <Input placeholder="Enter the name on your bank account" />
                        </Form.Item>

                        <Form.Item
                            name="accountNumber"
                            label="Account Number"
                            rules={[{ required: true, message: 'Please enter your account number' }]}
                        >
                            <Input placeholder="Enter your account number" />
                        </Form.Item>

                        <Form.Item
                            name="routingNumber"
                            label="Routing Number"
                            rules={[{ required: true, message: 'Please enter your routing number' }]}
                        >
                            <Input placeholder="Enter your routing number" />
                        </Form.Item>

                        <Form.Item
                            name="withdrawalAddress"
                            label="IBAN/Account Reference"
                            rules={[{ required: true, message: 'Please enter your IBAN or account reference' }]}
                        >
                            <Input placeholder="Enter your IBAN or account reference" />
                        </Form.Item>
                    </>
                )}

                <Alert
                    message="Important"
                    description="Withdrawals are subject to review and may take 1-3 business days to process. Please ensure all information is correct before submitting."
                    type="info"
                    showIcon
                    className="mb-4"
                />

                <Form.Item>
                    <Button
                        type="primary"
                        htmlType="submit"
                        loading={submitting}
                        block
                        size="large"
                    >
                        Submit Withdrawal Request
                    </Button>
                </Form.Item>
            </Form>
        </Card>
    );
};

export default WithdrawalForm;