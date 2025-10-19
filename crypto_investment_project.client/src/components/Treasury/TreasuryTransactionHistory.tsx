import React, { useState, useEffect } from 'react';
import {
    Table,
    Card,
    Input,
    Select,
    DatePicker,
    Button,
    Space,
    Tag,
    Typography,
    Drawer,
    Descriptions,
    message,
    Modal,
    Form,
    Row,
    Col,
    Tooltip
} from 'antd';
import {
    SearchOutlined,
    FilterOutlined,
    EyeOutlined,
    UndoOutlined,
    ExclamationCircleOutlined
} from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import dayjs from 'dayjs';

const { Text } = Typography;
const { RangePicker } = DatePicker;

interface TreasuryTransaction {
    id: string;
    transactionType: string;
    source: string;
    amount: number;
    assetTicker: string;
    usdValue: number | null;
    status: string;
    createdAt: string;
    collectedAt: string | null;
    userEmail: string | null;
    description: string | null;
    exchange: string | null;
    orderId: string | null;
}

interface TransactionFilter {
    startDate?: string;
    endDate?: string;
    transactionType?: string;
    source?: string;
    assetTicker?: string;
    status?: string;
    exchange?: string;
}

export const TreasuryTransactionHistory: React.FC = () => {
    const [transactions, setTransactions] = useState<TreasuryTransaction[]>([]);
    const [loading, setLoading] = useState(false);
    const [pagination, setPagination] = useState({ current: 1, pageSize: 50, total: 0 });
    const [filters, setFilters] = useState<TransactionFilter>({});
    const [selectedTransaction, setSelectedTransaction] = useState<TreasuryTransaction | null>(null);
    const [drawerVisible, setDrawerVisible] = useState(false);
    const [reverseModalVisible, setReverseModalVisible] = useState(false);
    const [reverseForm] = Form.useForm();

    useEffect(() => {
        fetchTransactions();
    }, [pagination.current, pagination.pageSize, filters]);

    const fetchTransactions = async () => {
        setLoading(true);
        try {
            const queryParams = new URLSearchParams({
                page: pagination.current.toString(),
                pageSize: pagination.pageSize.toString(),
                ...Object.fromEntries(
                    Object.entries(filters).filter(([_, v]) => v != null)
                )
            });

            const response = await fetch(`/api/treasury/transactions?${queryParams}`);
            const data = await response.json();
            
            setTransactions(data.transactions);
            setPagination(prev => ({
                ...prev,
                total: data.totalCount
            }));
        } catch (error) {
            console.error('Error fetching transactions:', error);
            message.error('Failed to load transactions');
        } finally {
            setLoading(false);
        }
    };

    const handleReverse = async (transactionId: string, reason: string) => {
        try {
            const response = await fetch(`/api/treasury/transactions/${transactionId}/reverse`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ reason })
            });

            if (response.ok) {
                message.success('Transaction reversed successfully');
                fetchTransactions();
                setReverseModalVisible(false);
                reverseForm.resetFields();
            } else {
                message.error('Failed to reverse transaction');
            }
        } catch (error) {
            console.error('Error reversing transaction:', error);
            message.error('An error occurred');
        }
    };

    const columns: ColumnsType<TreasuryTransaction> = [
        {
            title: 'Date',
            dataIndex: 'createdAt',
            key: 'createdAt',
            width: 180,
            render: (date: string) => dayjs(date).format('YYYY-MM-DD HH:mm:ss'),
            sorter: true
        },
        {
            title: 'Type',
            dataIndex: 'transactionType',
            key: 'transactionType',
            width: 100,
            render: (type: string) => {
                const colors: Record<string, string> = {
                    'Fee': 'blue',
                    'Dust': 'green',
                    'Rounding': 'orange',
                    'Interest': 'purple',
                    'Penalty': 'red',
                    'Other': 'default'
                };
                return <Tag color={colors[type] || 'default'}>{type}</Tag>;
            }
        },
        {
            title: 'Source',
            dataIndex: 'source',
            key: 'source',
            width: 150,
            ellipsis: true
        },
        {
            title: 'Amount',
            dataIndex: 'amount',
            key: 'amount',
            width: 150,
            render: (amount: number, record: TreasuryTransaction) => (
                <Space direction="vertical" size={0}>
                    <Text>{amount.toFixed(8)} {record.assetTicker}</Text>
                    {record.usdValue && (
                        <Text type="secondary" style={{ fontSize: '12px' }}>
                            ${record.usdValue.toFixed(2)}
                        </Text>
                    )}
                </Space>
            ),
            sorter: true
        },
        {
            title: 'Status',
            dataIndex: 'status',
            key: 'status',
            width: 100,
            render: (status: string) => {
                const colors: Record<string, string> = {
                    'Collected': 'success',
                    'Pending': 'processing',
                    'Failed': 'error',
                    'Reversed': 'default'
                };
                return <Tag color={colors[status] || 'default'}>{status}</Tag>;
            }
        },
        {
            title: 'User',
            dataIndex: 'userEmail',
            key: 'userEmail',
            width: 200,
            ellipsis: true,
            render: (email: string | null) => email || '-'
        },
        {
            title: 'Exchange/Order',
            key: 'exchange',
            width: 150,
            render: (_, record: TreasuryTransaction) => (
                record.exchange ? (
                    <Tooltip title={`Order: ${record.orderId || 'N/A'}`}>
                        <Tag>{record.exchange}</Tag>
                    </Tooltip>
                ) : '-'
            )
        },
        {
            title: 'Actions',
            key: 'actions',
            width: 120,
            fixed: 'right',
            render: (_, record: TreasuryTransaction) => (
                <Space>
                    <Button
                        type="link"
                        size="small"
                        icon={<EyeOutlined />}
                        onClick={() => {
                            setSelectedTransaction(record);
                            setDrawerVisible(true);
                        }}
                    >
                        View
                    </Button>
                    {record.status === 'Collected' && (
                        <Button
                            type="link"
                            size="small"
                            danger
                            icon={<UndoOutlined />}
                            onClick={() => {
                                setSelectedTransaction(record);
                                setReverseModalVisible(true);
                            }}
                        >
                            Reverse
                        </Button>
                    )}
                </Space>
            )
        }
    ];

    return (
        <Card
            title="Transaction History"
            extra={
                <Space>
                    <RangePicker
                        onChange={(dates) => {
                            if (dates) {
                                setFilters(prev => ({
                                    ...prev,
                                    startDate: dates[0]?.format('YYYY-MM-DD'),
                                    endDate: dates[1]?.format('YYYY-MM-DD')
                                }));
                            } else {
                                setFilters(prev => {
                                    const { startDate, endDate, ...rest } = prev;
                                    return rest;
                                });
                            }
                        }}
                    />
                    <Select
                        placeholder="Type"
                        style={{ width: 120 }}
                        allowClear
                        onChange={(value) => setFilters(prev => ({ ...prev, transactionType: value }))}
                    >
                        <Select.Option value="Fee">Fee</Select.Option>
                        <Select.Option value="Dust">Dust</Select.Option>
                        <Select.Option value="Rounding">Rounding</Select.Option>
                        <Select.Option value="Interest">Interest</Select.Option>
                        <Select.Option value="Other">Other</Select.Option>
                    </Select>
                    <Select
                        placeholder="Status"
                        style={{ width: 120 }}
                        allowClear
                        onChange={(value) => setFilters(prev => ({ ...prev, status: value }))}
                    >
                        <Select.Option value="Collected">Collected</Select.Option>
                        <Select.Option value="Pending">Pending</Select.Option>
                        <Select.Option value="Failed">Failed</Select.Option>
                        <Select.Option value="Reversed">Reversed</Select.Option>
                    </Select>
                    <Input
                        placeholder="Asset"
                        style={{ width: 100 }}
                        onChange={(e) => setFilters(prev => ({ ...prev, assetTicker: e.target.value }))}
                    />
                </Space>
            }
        >
            <Table
                columns={columns}
                dataSource={transactions}
                rowKey="id"
                loading={loading}
                pagination={{
                    ...pagination,
                    showSizeChanger: true,
                    showTotal: (total) => `Total ${total} transactions`
                }}
                onChange={(newPagination) => {
                    setPagination({
                        current: newPagination.current || 1,
                        pageSize: newPagination.pageSize || 50,
                        total: pagination.total
                    });
                }}
                scroll={{ x: 1200 }}
            />

            {/* Transaction Detail Drawer */}
            <Drawer
                title="Transaction Details"
                width={600}
                open={drawerVisible}
                onClose={() => setDrawerVisible(false)}
            >
                {selectedTransaction && (
                    <Descriptions column={1} bordered>
                        <Descriptions.Item label="Transaction ID">
                            {selectedTransaction.id}
                        </Descriptions.Item>
                        <Descriptions.Item label="Type">
                            <Tag color="blue">{selectedTransaction.transactionType}</Tag>
                        </Descriptions.Item>
                        <Descriptions.Item label="Source">
                            {selectedTransaction.source}
                        </Descriptions.Item>
                        <Descriptions.Item label="Amount">
                            {selectedTransaction.amount.toFixed(8)} {selectedTransaction.assetTicker}
                        </Descriptions.Item>
                        <Descriptions.Item label="USD Value">
                            ${selectedTransaction.usdValue?.toFixed(2) || 'N/A'}
                        </Descriptions.Item>
                        <Descriptions.Item label="Status">
                            <Tag color={selectedTransaction.status === 'Collected' ? 'success' : 'default'}>
                                {selectedTransaction.status}
                            </Tag>
                        </Descriptions.Item>
                        <Descriptions.Item label="Created At">
                            {dayjs(selectedTransaction.createdAt).format('YYYY-MM-DD HH:mm:ss')}
                        </Descriptions.Item>
                        <Descriptions.Item label="Collected At">
                            {selectedTransaction.collectedAt 
                                ? dayjs(selectedTransaction.collectedAt).format('YYYY-MM-DD HH:mm:ss')
                                : 'Not collected'}
                        </Descriptions.Item>
                        <Descriptions.Item label="User">
                            {selectedTransaction.userEmail || 'N/A'}
                        </Descriptions.Item>
                        <Descriptions.Item label="Exchange">
                            {selectedTransaction.exchange || 'N/A'}
                        </Descriptions.Item>
                        <Descriptions.Item label="Order ID">
                            {selectedTransaction.orderId || 'N/A'}
                        </Descriptions.Item>
                        <Descriptions.Item label="Description">
                            {selectedTransaction.description || 'N/A'}
                        </Descriptions.Item>
                    </Descriptions>
                )}
            </Drawer>

            {/* Reverse Transaction Modal */}
            <Modal
                title={
                    <Space>
                        <ExclamationCircleOutlined style={{ color: '#ff4d4f' }} />
                        Reverse Transaction
                    </Space>
                }
                open={reverseModalVisible}
                onCancel={() => {
                    setReverseModalVisible(false);
                    reverseForm.resetFields();
                }}
                footer={null}
            >
                <Form
                    form={reverseForm}
                    layout="vertical"
                    onFinish={(values) => {
                        if (selectedTransaction) {
                            handleReverse(selectedTransaction.id, values.reason);
                        }
                    }}
                >
                    <Text type="warning">
                        Warning: This action will create a reversal transaction and update the treasury balance.
                        This should only be done for corrections or errors.
                    </Text>
                    
                    {selectedTransaction && (
                        <Descriptions column={1} bordered style={{ marginTop: 16, marginBottom: 16 }}>
                            <Descriptions.Item label="Amount">
                                {selectedTransaction.amount.toFixed(8)} {selectedTransaction.assetTicker}
                            </Descriptions.Item>
                            <Descriptions.Item label="USD Value">
                                ${selectedTransaction.usdValue?.toFixed(2) || 'N/A'}
                            </Descriptions.Item>
                        </Descriptions>
                    )}

                    <Form.Item
                        name="reason"
                        label="Reason for Reversal"
                        rules={[
                            { required: true, message: 'Please provide a reason' },
                            { min: 10, message: 'Reason must be at least 10 characters' }
                        ]}
                    >
                        <Input.TextArea
                            rows={4}
                            placeholder="Provide a detailed reason for reversing this transaction..."
                        />
                    </Form.Item>

                    <Form.Item>
                        <Space style={{ float: 'right' }}>
                            <Button onClick={() => {
                                setReverseModalVisible(false);
                                reverseForm.resetFields();
                            }}>
                                Cancel
                            </Button>
                            <Button type="primary" danger htmlType="submit">
                                Confirm Reversal
                            </Button>
                        </Space>
                    </Form.Item>
                </Form>
            </Modal>
        </Card>
    );
};

export default TreasuryTransactionHistory;
