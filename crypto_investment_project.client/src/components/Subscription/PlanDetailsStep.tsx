// src/components/Subscription/PlanDetailsStep.tsx - Refactored with Global Styling & Ant Design
import React from 'react';
import { Form, Select, InputNumber, DatePicker, Card, Typography, Space, Divider } from 'antd';
import { DollarOutlined, CalendarOutlined, ClockCircleOutlined } from '@ant-design/icons';
import dayjs, { Dayjs } from 'dayjs';
import { Allocation } from '../../types/subscription';

const { Title, Text, Paragraph } = Typography;
const { Option } = Select;

const INTERVALS = [
    { value: 'ONCE', label: 'One-time payment', description: 'Single investment' },
    { value: 'DAILY', label: 'Daily', description: 'Invest every day' },
    { value: 'WEEKLY', label: 'Weekly', description: 'Invest every week' },
    { value: 'MONTHLY', label: 'Monthly', description: 'Invest every month' },
    { value: 'YEARLY', label: 'Yearly', description: 'Invest every year' }
];

interface PlanDetailsStepProps {
    formData: {
        interval: string;
        amount: number;
        endDate: Date | null;
    };
    updateFormData: (field: string, value: any) => void;
}

const PlanDetailsStep: React.FC<PlanDetailsStepProps> = ({ formData, updateFormData }) => {
    /**
     * Handle interval change
     */
    const handleIntervalChange = (value: string) => {
        updateFormData('interval', value);
    };

    /**
     * Handle amount change
     */
    const handleAmountChange = (value: number | null) => {
        if (value && value > 0) {
            updateFormData('amount', value);
        }
    };

    /**
     * Handle end date change
     */
    const handleEndDateChange = (date: Dayjs | null) => {
        updateFormData('endDate', date ? date.toDate() : null);
    };

    /**
     * Calculate estimated annual investment
     */
    const calculateAnnualAmount = (): number => {
        if (formData.interval === 'ONCE') return formData.amount;

        const multipliers: Record<string, number> = {
            'DAILY': 365,
            'WEEKLY': 52,
            'MONTHLY': 12,
            'YEARLY': 1
        };

        return formData.amount * (multipliers[formData.interval] || 0);
    };

    // Get minimum date (tomorrow)
    const tomorrow = dayjs().add(1, 'day');
    const annualAmount = calculateAnnualAmount();

    return (
        <div className="stack-lg">
            {/* Header */}
            <div>
                <Title level={2} className="mb-sm">
                    Investment Plan Details
                </Title>
                <Paragraph className="text-body text-secondary">
                    Choose how often you want to invest and how much.
                </Paragraph>
            </div>

            <Divider className="divider" />

            {/* Investment Frequency */}
            <Form.Item
                label={
                    <Space>
                        <ClockCircleOutlined />
                        <Text strong>Investment Frequency</Text>
                    </Space>
                }
                required
            >
                <Select
                    value={formData.interval}
                    onChange={handleIntervalChange}
                    size="large"
                    style={{ width: '100%' }}
                    placeholder="Select investment frequency"
                >
                    {INTERVALS.map(option => (
                        <Option key={option.value} value={option.value}>
                            <div>
                                <div className="font-medium">{option.label}</div>
                                <div className="text-caption text-secondary">{option.description}</div>
                            </div>
                        </Option>
                    ))}
                </Select>
                <Text type="secondary" className="text-body-sm" style={{ marginTop: '8px', display: 'block' }}>
                    How often would you like to invest? Choose "One-time payment" for a single investment.
                </Text>
            </Form.Item>

            {/* Investment Amount */}
            <Form.Item
                label={
                    <Space>
                        <DollarOutlined />
                        <Text strong>Investment Amount (USD)</Text>
                    </Space>
                }
                required
            >
                <InputNumber
                    value={formData.amount}
                    onChange={handleAmountChange}
                    min={10}
                    step={10}
                    precision={2}
                    size="large"
                    style={{ width: '100%' }}
                    prefix="$"
                    placeholder="Enter amount"
                    formatter={(value) => `${value}`.replace(/\B(?=(\d{3})+(?!\d))/g, ',')}
                    parser={(value) => Number(value!.replace(/\$\s?|(,*)/g, ''))}
                />
                <Text type="secondary" className="text-body-sm" style={{ marginTop: '8px', display: 'block' }}>
                    Minimum investment: $10
                </Text>
            </Form.Item>

            {/* End Date (only for recurring) */}
            {formData.interval !== 'ONCE' && (
                <Form.Item
                    label={
                        <Space>
                            <CalendarOutlined />
                            <Text strong>End Date (Optional)</Text>
                        </Space>
                    }
                >
                    <DatePicker
                        value={formData.endDate ? dayjs(formData.endDate) : null}
                        onChange={handleEndDateChange}
                        minDate={tomorrow}
                        size="large"
                        style={{ width: '100%' }}
                        placeholder="Select end date"
                        format="MMMM DD, YYYY"
                    />
                    <Text type="secondary" className="text-body-sm" style={{ marginTop: '8px', display: 'block' }}>
                        Leave empty for ongoing subscription. You can cancel anytime.
                    </Text>
                </Form.Item>
            )}

            {/* Summary Card (only for recurring) */}
            {formData.interval !== 'ONCE' && (
                <Card
                    className="card-minimal"
                    style={{
                        backgroundColor: 'var(--color-bg-container)',
                        marginTop: 'var(--spacing-lg)'
                    }}
                >
                    <Title level={4} className="mb-md">
                        Recurring Payment Summary
                    </Title>
                    <div className="grid-2">
                        <div>
                            <Text type="secondary" className="text-body-sm">Frequency</Text>
                            <div className="font-medium">
                                {INTERVALS.find(i => i.value === formData.interval)?.label}
                            </div>
                        </div>
                        <div>
                            <Text type="secondary" className="text-body-sm">Per Payment</Text>
                            <div className="font-medium">${formData.amount.toFixed(2)}</div>
                        </div>
                        <div>
                            <Text type="secondary" className="text-body-sm">End Date</Text>
                            <div className="font-medium">
                                {formData.endDate
                                    ? dayjs(formData.endDate).format('MMMM DD, YYYY')
                                    : 'Ongoing (until canceled)'}
                            </div>
                        </div>
                        <div>
                            <Text type="secondary" className="text-body-sm">Est. Annual Investment</Text>
                            <div className="font-medium text-primary">
                                ${annualAmount.toLocaleString('en-US', {
                                    minimumFractionDigits: 2,
                                    maximumFractionDigits: 2
                                })}
                            </div>
                        </div>
                    </div>
                </Card>
            )}

            {/* One-time Payment Info */}
            {formData.interval === 'ONCE' && (
                <Card
                    className="card-minimal"
                    style={{
                        backgroundColor: 'var(--color-bg-container)',
                        marginTop: 'var(--spacing-lg)'
                    }}
                >
                    <Space direction="vertical" size="small">
                        <Text type="secondary" className="text-body-sm">
                            Payment Method
                        </Text>
                        <Text className="font-medium">Credit/Debit Card via Stripe</Text>
                        <Divider style={{ margin: '12px 0' }} />
                        <Text type="secondary" className="text-body-sm">
                            Total Investment
                        </Text>
                        <Text className="text-h3 text-primary">
                            ${formData.amount.toFixed(2)}
                        </Text>
                    </Space>
                </Card>
            )}
        </div>
    );
};

export default PlanDetailsStep;