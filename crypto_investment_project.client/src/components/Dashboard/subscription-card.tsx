import React, { useState } from 'react';
import { SubscriptionCardProps, Allocation, SubscriptionStatus } from '../../types/subscription';
import { Modal, Form, Input, Select, DatePicker, Button, message, InputNumber, Card, Divider, Popconfirm, Flex } from 'antd';
import { PlusOutlined, DeleteOutlined, EditOutlined, HistoryOutlined, RedoOutlined, CloseOutlined, CreditCardOutlined, QuestionCircleOutlined } from '@ant-design/icons';
import SubscriptionPaymentManager from '../Subscription/SubscriptionPaymentManager';
import { Asset, AssetColors } from '../../types/assetTypes';
import { updateSubscription } from '../../services/subscription';
import { getSupportedAssets } from '../../services/asset';
import dayjs from 'dayjs';
import { PaymentRequestData, initiatePayment } from '../../services/payment';
import { formatApiError } from '../../utils/apiErrorHandler';
import { useAuth } from '../../context/AuthContext';

const { Option } = Select;

interface EditFormData {
    amount: number;
    endDate?: Date | null;
    allocations: Allocation[];
}

const SubscriptionCard: React.FC<SubscriptionCardProps & { onDataUpdated?: () => void }> = ({
    subscription,
    onEdit,
    onCancel,
    onViewHistory,
    onDataUpdated
}) => {
    const [isExpanded, setIsExpanded] = useState(false);
    const [showPaymentModal, setShowPaymentModal] = useState(false);
    const [showEditModal, setShowEditModal] = useState(false);

    const [loading, setLoading] = useState(false);
    const [retrying, setRetrying] = useState(false);
    const [assets, setAssets] = useState<Asset[]>([]);
    const [form] = Form.useForm<EditFormData>();
    const { user } = useAuth();

    // Format date
    const formatDate = (dateString: string | Date): string => {
        return new Date(dateString).toLocaleDateString('en-US', {
            year: 'numeric',
            month: 'short',
            day: 'numeric'
        });
    };

    // Calculate progress to next payment
    const calculateProgress = (): number => {
        const now = new Date();
        const createdDate = new Date(subscription.createdAt);

        const intervalMap: Record<string, number> = {
            DAILY: 1,
            WEEKLY: 7,
            MONTHLY: 30
        };

        const intervalKey = subscription.interval.toUpperCase();
        const intervalDays = intervalMap[intervalKey] || 30;
        const totalDuration = intervalDays * 24 * 60 * 60 * 1000;
        const elapsed = now.getTime() - createdDate.getTime();
        const progress = (elapsed % totalDuration) / totalDuration * 100;

        return Math.min(Math.max(progress, 0), 100);
    };

    // Handle view history click
    const handleViewHistoryClick = () => {
        if (subscription && subscription.id) {
            onViewHistory(subscription.id);
        } else {
            console.error("Cannot view history: Missing subscription ID");
        }
    };

    // Handle payment modal close
    const handlePaymentModalClose = () => {
        setShowPaymentModal(false);
    };

    // Handle payment data update
    const handlePaymentDataUpdated = () => {
        setTimeout(() => {
            setShowPaymentModal(false);
        }, 2000);

        if (onDataUpdated) {
            onDataUpdated();
        }
    };

    // Handle edit modal open
    const handleEditClick = () => {
        // Populate form with current subscription data (excluding interval)

        const loadAssets = async () => {
            try {
                const assetData = await getSupportedAssets();
                setAssets(assetData);
            } catch (error) {
                console.error('Error loading assets:', error);
                message.error('Failed to load available assets');
            }
        };

        loadAssets();

        const initialValues: EditFormData = {
            amount: subscription.amount,
            endDate: subscription.endDate ? new Date(subscription.endDate) : null,
            allocations: subscription.allocations.map(alloc => ({
                assetId: alloc.assetId,
                assetName: alloc.assetName,
                assetTicker: alloc.assetTicker,
                percentAmount: alloc.percentAmount
            }))
        };

        form.setFieldsValue({
            ...initialValues,
            endDate: initialValues.endDate ? dayjs(initialValues.endDate) : undefined
        });
        
        setShowEditModal(true);
    };

    const handleRetryPaymentClick = async () => {
        // Retry a new payment checkout session
        // Initialize payment with the subscription ID
        try {
            setRetrying(true);
            const paymentRequest: PaymentRequestData = {
                subscriptionId: subscription.id,
                userId: user?.id!,
                amount: subscription.amount,
                currency: subscription.currency,
                isRecurring: subscription.interval !== 'ONCE',
                interval: subscription.interval,
                // Add return URLs with better error handling
                returnUrl: window.location.origin + `/payment/checkout/success?subscription_id=${subscription.id}&amount=${subscription.amount}&currency=${subscription.currency}`,
                cancelUrl: window.location.origin + `/payment/checkout/cancel?subscription_id=${subscription.id}`
            };

            const checkoutUrl = await initiatePayment(paymentRequest);

            if (!checkoutUrl) {
                throw new Error('Payment initialization returned empty checkout URL');
            }

            // Save current transaction state to session storage for recovery
            sessionStorage.setItem('pendingSubscription', JSON.stringify({
                subscriptionId: subscription.id,
                timestamp: Date.now(),
                amount: subscription.amount,
                currency: subscription.currency
            }));

            // Redirect to checkout
            window.location.href = checkoutUrl;
        } catch (paymentError: any) {
            console.error('Payment initialization error:', paymentError);
            // Format user-friendly error message
            const errorMessage = formatApiError(paymentError);
            throw new Error(`Payment initialization failed: ${errorMessage}`);
        } finally {
            setRetrying(false);
        }
    };

    // Handle edit modal close
    const handleEditModalClose = () => {
        setShowEditModal(false);
        form.resetFields();
    };

    // Validate allocations sum to 100%
    const validateAllocations = (allocations: any[]) => {
        if (!allocations || allocations.length === 0) {
            return Promise.reject('At least one allocation is required');
        }

        const total = allocations.reduce((sum, alloc) => {
            return sum + (alloc?.percentAmount || 0);
        }, 0);

        if (Math.abs(total - 100) > 0.01) {
            return Promise.reject(`Allocations must sum to 100%. Current total: ${total.toFixed(2)}%`);
        }

        return Promise.resolve();
    };

    // Handle form submission
    const handleEditSubmit = async () => {
        try {
            setLoading(true);

            const values = await form.validateFields();
            
            // Validate allocations
            await validateAllocations(values.allocations);

            // Prepare update payload (no interval changes allowed)
            const updatePayload = {
                amount: values.amount,
                endDate: values.endDate ? dayjs(values.endDate).toDate() : undefined,
                allocations: values.allocations.map(alloc => ({
                    assetId: alloc.assetId,
                    assetName: alloc.assetName,
                    assetTicker: alloc.assetTicker,
                    percentAmount: alloc.percentAmount
                }))
            };

            // Update subscription
            await updateSubscription(subscription.id, updatePayload);

            message.success('Subscription updated successfully');
            handleEditModalClose();

            // Refresh data
            if (onDataUpdated) {
                onDataUpdated();
            }

        } catch (error: any) {
            console.error('Error updating subscription:', error);
            
            if (error.name === 'ValidationError') {
                // Form validation errors are handled by Ant Design
                return;
            }
            
            const errorMessage = error?.response?.data?.message || 
                               error?.message || 
                               'Failed to update subscription';
            message.error(errorMessage);
        } finally {
            setLoading(false);
        }
    };

    // Calculate remaining percentage
    const calculateRemainingPercentage = () => {
        const allocations = form.getFieldValue('allocations') || [];
        const total = allocations.reduce((sum: number, alloc: any) => {
            return sum + (alloc?.percentAmount || 0);
        }, 0);
        return Math.max(0, 100 - total);
    };

    return (
        <>
            <div className={`bg-white shadow-lg rounded-xl p-6 relative transition-all duration-200 ${subscription.isCancelled && 'opacity-75'}`}>
                {/* Status badge */}
                <div className={`absolute top-4 right-4 px-2 py-1 text-xs font-semibold rounded-full ${subscription.isCancelled ? 'bg-red-100 text-red-800' : subscription.status == 'PENDING' ? 'bg-orange-100 text-orange-700' : 'bg-green-100 text-green-800'
                    }`}>
                    {subscription.status}
                </div>

                {/* Subscription header */}
                <div className="mb-4">
                    <h3 className="text-xl font-semibold">
                        {subscription.amount} {subscription.currency} {subscription.interval.toLowerCase()}
                    </h3>
                    <p className="text-gray-500 text-sm">Created: {formatDate(subscription.createdAt)}</p>
                </div>

                {/* Progress to next payment */}
                <div className="mb-6">
                    <div className="flex justify-between text-xs text-gray-500 mb-1">
                        <span>Last payment: {subscription.lastPayment ? formatDate(subscription.lastPayment) : "N/A"}</span>
                        <span>Next payment: {formatDate(subscription.nextDueDate)}</span>
                    </div>
                    <div className="w-full bg-gray-200 rounded-full h-2.5">
                        <div
                            className="bg-blue-600 h-2.5 rounded-full"
                            style={{ width: `${calculateProgress()}%` }}
                        />
                    </div>

                    {/* Toggle details button */}
                    <Button
                        color="default" variant="link"
                        size = "small"
                        onClick={() => setIsExpanded(!isExpanded)}
                        aria-expanded={isExpanded}
                    >
                        {isExpanded ? 'Hide details' : 'Show details'}
                        <svg
                            className={`ml-1 w-4 h-4 transition-transform ${isExpanded ? 'rotate-180' : ''}`}
                            fill="none"
                            stroke="currentColor"
                            viewBox="0 0 24 24"
                            xmlns="http://www.w3.org/2000/svg"
                        >
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M19 9l-7 7-7-7" />
                        </svg>
                    </Button>
                </div>

                {/* Expanded details */}
                {isExpanded && (
                    <div className="mb-6 space-y-2 text-sm">
                        <p className="text-gray-700">
                            <span className="font-semibold">ID:</span> {subscription.id}
                        </p>
                        <p className="text-gray-700">
                            <span className="font-semibold">Total invested:</span> {subscription.totalInvestments} {subscription.currency}
                        </p>
                        <div>
                            <p className="font-semibold mb-1">Allocations:</p>
                            <div className="mb-4 h-4 bg-gray-200 rounded-full overflow-hidden flex">
                                {subscription.allocations?.map((alloc: Allocation) => {
                                    const percentage = alloc.percentAmount;
                                    return (
                                        <div
                                            key={alloc.assetId}
                                            style={{
                                                width: `${percentage}%`,
                                                backgroundColor: AssetColors[alloc.assetTicker] || '#6B7280'
                                            }}
                                            className="h-full"
                                            title={`${alloc.assetTicker}: ${percentage.toFixed(1)}%`}
                                        />
                                    );
                                })}
                            </div>
                            <div className="space-y-3">
                                {subscription.allocations.map((alloc: Allocation) => (
                                    <div key={alloc.assetId} className="flex justify-between items-center">
                                        <div className="flex items-center">
                                            <div
                                                className="w-3 h-3 rounded-full mr-2"
                                                style={{ backgroundColor: AssetColors[alloc.assetTicker] || '#6B7280' }}
                                            />
                                            <span className="font-medium">
                                                {(alloc.assetName || alloc.assetTicker) && <span className="text-gray-500 ml-1">{alloc.assetName} ({alloc.assetTicker})</span>}
                                            </span>
                                        </div>
                                        <div className="font-bold">{alloc.percentAmount}%</div>
                                    </div>
                                ))}
                            </div>
                        </div>
                    </div>
                )}

                {/* Action buttons */}
                <Flex justify="flex-end" gap="small" wrap >
                    {subscription.status == SubscriptionStatus.ACTIVE &&
                        <Button
                            onClick={() => setShowPaymentModal(true)}
                            icon={<CreditCardOutlined className="mr-1" />}
                            iconPosition="end"
                        >
                            Payment Status
                        </Button>
                    }
                    {!subscription.isCancelled &&
                        <Button
                            color="blue" variant="solid"
                            onClick={handleEditClick}
                            disabled={subscription.isCancelled}
                            icon={<EditOutlined className="mr-1" />}
                            iconPosition="end"
                        >
                            Edit
                        </Button>
                    }

                        <Popconfirm
                            title="Cancel subscription"
                        description={subscription.status == SubscriptionStatus.PENDING || subscription.isCancelled ? ("Are you sure to delete this subscription? This will permanantly delete all the data.") : ("Are you sure to cancel this subscription? This action will suspend any pending payments. You can always reactivate later.")}
                            onConfirm={() => onCancel(subscription.id)}
                            icon={<QuestionCircleOutlined style={{ color: 'red' }} />}
                        >
                            <Button color="danger" variant="solid"
                                icon={<CloseOutlined className="mr-1" />}
                                iconPosition="end"
                            >
                                {subscription.status == SubscriptionStatus.PENDING || subscription.isCancelled ? ("Delete") : ("Cancel") }
                                
                            </Button>
                        </Popconfirm>
                    {subscription.status == SubscriptionStatus.PENDING &&
                        <Button
                            loading={retrying}
                            color="gold" variant="solid"
                            onClick={handleRetryPaymentClick}
                            icon={<RedoOutlined className="mr-1" />}
                            iconPosition="end"
                        >
                            {retrying ? (
                                <>
                                    Retrying...
                                </>) : (
                                <>
                                    Retry Payment
                                </>
                            )}
                            
                        </Button>
                    }
                    {subscription.status != SubscriptionStatus.PENDING &&
                        <Button
                            color="default" variant="solid"
                            onClick={handleViewHistoryClick}
                        >
                            <HistoryOutlined className="mr-1" />
                            History
                        </Button>
                    }
                </Flex>
            </div>

            {/* Payment Status Modal */}
            
            <Modal
                title="Payment Status"
                visible={showPaymentModal}
                onCancel={handlePaymentModalClose}
                footer={null}
                width={600}
            >
                <SubscriptionPaymentManager
                    subscription={subscription}
                    onDataUpdated={handlePaymentDataUpdated}
                />
            </Modal>            

            {/* Edit Subscription Modal */}
            <Modal
                title={
                    <div className="flex items-center">
                        <EditOutlined className="mr-2" />
                        Edit Subscription
                    </div>
                }
                visible={showEditModal}
                onCancel={handleEditModalClose}
                width={800}
                footer={[
                    <Button key="cancel" onClick={handleEditModalClose}>
                        Cancel
                    </Button>,
                    <Button 
                        key="submit" 
                        type="primary" 
                        loading={loading}
                        onClick={handleEditSubmit}
                    >
                        Update Subscription
                    </Button>
                ]}
            >
                <Form
                    form={form}
                    layout="vertical"
                    initialValues={{
                        allocations: [{ assetId: '', percentAmount: 0 }]
                    }}
                >
                    <div className="grid grid-cols-1 gap-4">
                        {/* Amount */}
                        <Form.Item
                            label="Amount"
                            name="amount"
                            rules={[
                                { required: true, message: 'Amount is required' },
                                { type: 'number', min: 1, message: 'Amount must be greater than 0' }
                            ]}
                        >
                            <InputNumber
                                style={{ width: '100%' }}
                                prefix="$"
                                placeholder="Enter amount"
                                min={1}
                                precision={2}
                            />
                        </Form.Item>

                        {/* Current Interval (Read-only) */}
                        <Form.Item label="Payment Interval">
                            <Input 
                                value={`${subscription.interval.toLowerCase()} (cannot be changed)`}
                                disabled
                                style={{ color: '#666', fontStyle: 'italic' }}
                            />
                            <div className="text-xs text-gray-500 mt-1">
                                To change the payment interval, please create a new subscription
                            </div>
                        </Form.Item>
                    </div>

                    {/* End Date */}
                    <Form.Item
                        label="End Date (Optional)"
                        name="endDate"
                    >
                        <DatePicker
                            style={{ width: '100%' }}
                            placeholder="Select end date"
                            disabledDate={(current) => current && current.isBefore(dayjs(), 'day')}
                        />
                    </Form.Item>

                    <Divider>Asset Allocations</Divider>

                    {/* Allocations */}
                    <Form.Item
                        label="Asset Allocations"
                        required
                    >
                        <Form.List name="allocations">
                            {(fields, { add, remove }) => (
                                <>
                                    {fields.map((field, index) => (
                                        <Card key={field.key} size="small" className="mb-2">
                                            <div className="flex items-center space-x-2">
                                                <Form.Item
                                                    {...field}
                                                    name={[field.name, 'assetId']}
                                                    rules={[{ required: true, message: 'Please select an asset' }]}
                                                    className="flex-1 mb-0"
                                                >
                                                    <Select
                                                        placeholder="Select asset"
                                                        showSearch
                                                        optionFilterProp="children"
                                                        filterOption={(input, option) =>
                                                            (option?.children as unknown as string)
                                                                ?.toLowerCase()
                                                                ?.includes(input.toLowerCase()) ?? false
                                                        }
                                                    >
                                                        {assets.map(asset => (
                                                            <Option key={asset.id} value={asset.id}>
                                                                {asset.name} ({asset.ticker})
                                                            </Option>
                                                        ))}
                                                    </Select>
                                                </Form.Item>

                                                <Form.Item
                                                    {...field}
                                                    name={[field.name, 'percentAmount']}
                                                    rules={[
                                                        { required: true, message: 'Percentage required' },
                                                        { type: 'number', min: 0.01, max: 100, message: 'Must be between 0.01 and 100' }
                                                    ]}
                                                    className="mb-0"
                                                >
                                                    <InputNumber
                                                        placeholder="Percentage"
                                                        min={0.01}
                                                        max={100}
                                                        precision={2}
                                                        suffix="%"
                                                        style={{ width: 120 }}
                                                    />
                                                </Form.Item>

                                                {fields.length > 1 && (
                                                    <Button
                                                        type="text"
                                                        danger
                                                        icon={<DeleteOutlined />}
                                                        onClick={() => {
                                                            remove(field.name);
                                                            // Update form to trigger re-calculation
                                                            const currentValues = form.getFieldsValue();
                                                            form.setFieldsValue(currentValues);
                                                        }}
                                                    />
                                                )}
                                            </div>
                                        </Card>
                                    ))}

                                    <div className="flex justify-between items-center">
                                        <Button
                                            type="dashed"
                                            onClick={() => add()}
                                            icon={<PlusOutlined />}
                                        >
                                            Add Asset
                                        </Button>
                                        
                                        <div className="text-sm text-gray-600">
                                            Remaining: {calculateRemainingPercentage().toFixed(2)}%
                                        </div>
                                    </div>
                                </>
                            )}
                        </Form.List>
                    </Form.Item>

                    {/* Allocation Summary */}
                    <Form.Item shouldUpdate>
                        {() => {
                            const allocations = form.getFieldValue('allocations') || [];
                            const total = allocations.reduce((sum: number, alloc: any) => 
                                sum + (alloc?.percentAmount || 0), 0
                            );
                            
                            return (
                                <div className={`p-3 rounded ${Math.abs(total - 100) < 0.01 ? 'bg-green-50 border-green-200' : 'bg-red-50 border-red-200'} border`}>
                                    <div className="text-sm">
                                        <strong>Total Allocation: {total.toFixed(2)}%</strong>
                                        {Math.abs(total - 100) > 0.01 && (
                                            <div className="text-red-600 mt-1">
                                                Allocations must sum to exactly 100%
                                            </div>
                                        )}
                                    </div>
                                </div>
                            );
                        }}
                    </Form.Item>
                </Form>
            </Modal>
        </>
    );
};

export default SubscriptionCard;