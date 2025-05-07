import React, { useState, useEffect } from 'react';
import { Table, Tag, Button, Modal, Form, Input, Select, Card, Tooltip, Alert, Skeleton } from 'antd';
import { ClockCircleOutlined, CheckCircleOutlined, CloseCircleOutlined } from '@ant-design/icons';
import type { TablePaginationConfig } from 'antd/es/table';
import { Withdrawal } from '../../../src/types/withdrawal';

const { Option } = Select;
const { TextArea } = Input;

// Update PaginationType to match TablePaginationConfig
interface PaginationType {
    current: number;
    pageSize: number;
    total: number;
}

const WithdrawalManagement: React.FC = () => {
    const [withdrawals, setWithdrawals] = useState<Withdrawal[]>([]);
    const [loading, setLoading] = useState<boolean>(true);
    const [pagination, setPagination] = useState<PaginationType>({
        current: 1,
        pageSize: 10,
        total: 0
    });
    const [modalVisible, setModalVisible] = useState<boolean>(false);
    const [currentWithdrawal, setCurrentWithdrawal] = useState<Withdrawal | null>(null);
    const [processing, setProcessing] = useState<boolean>(false);
    const [error, setError] = useState<string | null>(null);
    const [form] = Form.useForm();

    useEffect(() => {
        fetchPendingWithdrawals();
    }, [pagination.current]);

    const fetchPendingWithdrawals = async (): Promise<void> => {
        try {
            setLoading(true);
            const response = await fetch(
                `/api/withdrawal/admin/pending?page=${pagination.current}&pageSize=${pagination.pageSize}`,
                {
                    headers: {
                        'Authorization': `Bearer ${localStorage.getItem('token')}`,
                    },
                }
            );

            if (!response.ok) {
                throw new Error('Failed to fetch pending withdrawals');
            }

            const data = await response.json();
            setWithdrawals(data.items);
            setPagination({
                ...pagination,
                total: data.totalCount,
            });
        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : 'An unknown error occurred';
            setError(errorMessage);
        } finally {
            setLoading(false);
        }
    };

    // Fix the table onChange handler with proper Ant Design typing
    const handleTableChange = (newPagination: TablePaginationConfig): void => {
        setPagination({
            ...pagination,
            current: newPagination.current || 1,
            pageSize: newPagination.pageSize || 10,
        });
    };

    const showProcessModal = (withdrawal: Withdrawal): void => {
        setCurrentWithdrawal(withdrawal);
        form.setFieldsValue({
            status: '',
            comment: '',
        });
        setModalVisible(true);
    };

    const handleModalCancel = (): void => {
        setModalVisible(false);
    };

    const handleModalSubmit = async (): Promise<void> => {
        try {
            setError(null);
            const values = await form.validateFields();
            setProcessing(true);

            const response = await fetch(`/api/withdrawal/admin/${currentWithdrawal?.id}/update-status`, {
                method: 'PUT',
                headers: {
                    'Authorization': `Bearer ${localStorage.getItem('token')}`,
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify({
                    status: values.status,
                    comment: values.comment,
                }),
            });

            if (!response.ok) {
                const errorData = await response.json();
                throw new Error(errorData.message || 'Failed to update withdrawal status');
            }

            setModalVisible(false);
            fetchPendingWithdrawals();
        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : 'An unknown error occurred';
            setError(errorMessage);
        } finally {
            setProcessing(false);
        }
    };

    const getStatusTag = (status: string): React.ReactNode => {
        switch (status) {
            case 'PENDING':
                return <Tag icon={<ClockCircleOutlined />} color="processing">Pending</Tag>;
            case 'APPROVED':
                return <Tag icon={<CheckCircleOutlined />} color="blue">Approved</Tag>;
            case 'COMPLETED':
                return <Tag icon={<CheckCircleOutlined />} color="success">Completed</Tag>;
            case 'REJECTED':
                return <Tag icon={<CloseCircleOutlined />} color="error">Rejected</Tag>;
            default:
                return <Tag>{status}</Tag>;
        }
    };

    const columns = [
        {
            title: 'Date',
            dataIndex: 'createdAt',
            key: 'createdAt',
            render: (text: string) => new Date(text).toLocaleString(),
        },
        {
            title: 'User ID',
            dataIndex: 'userId',
            key: 'userId',
            ellipsis: true,
            render: (text: string) => (
                <Tooltip title={text}>
                    <span>{text?.substring(0, 8)}...</span>
                </Tooltip>
            ),
        },
        {
            title: 'Requested By',
            dataIndex: 'requestedBy',
            key: 'requestedBy',
            ellipsis: true,
        },
        {
            title: 'Amount',
            key: 'amount',
            render: (_: any, record: Withdrawal) => (
                <span>
                    {record.amount.toFixed(2)} {record.currency}
                </span>
            ),
        },
        {
            title: 'Method',
            dataIndex: 'withdrawalMethod',
            key: 'withdrawalMethod',
            render: (method: string) => {
                switch (method) {
                    case 'CRYPTO_TRANSFER':
                        return 'Crypto Transfer';
                    case 'BANK_TRANSFER':
                        return 'Bank Transfer';
                    default:
                        return method;
                }
            },
        },
        {
            title: 'KYC Level',
            dataIndex: 'kycLevelAtTime',
            key: 'kycLevelAtTime',
        },
        {
            title: 'Status',
            dataIndex: 'status',
            key: 'status',
            render: (status: string) => getStatusTag(status),
        },
        {
            title: 'Actions',
            key: 'actions',
            render: (_: any, record: Withdrawal) => (
                <Button
                    type="primary"
                    onClick={() => showProcessModal(record)}
                >
                    Process
                </Button>
            ),
        },
    ];

    if (loading && withdrawals.length === 0) {
        return (
            <Card title="Withdrawal Management" className="mb-5">
                <Skeleton active />
            </Card>
        );
    }

    return (
        <Card title="Withdrawal Management" className="mb-5">
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

            <Table
                columns={columns}
                dataSource={withdrawals}
                rowKey="id"
                pagination={pagination}
                loading={loading}
                onChange={handleTableChange}
            />

            <Modal
                title="Process Withdrawal"
                open={modalVisible}
                onCancel={handleModalCancel}
                onOk={handleModalSubmit}
                okButtonProps={{ loading: processing }}
                width={700}
            >
                {currentWithdrawal && (
                    <div>
                        <div className="mb-4">
                            <div className="grid grid-cols-2 gap-4">
                                <div>
                                    <p><strong>User ID:</strong> {currentWithdrawal.userId}</p>
                                    <p><strong>Amount:</strong> {currentWithdrawal.amount} {currentWithdrawal.currency}</p>
                                    <p><strong>Requested By:</strong> {currentWithdrawal.requestedBy}</p>
                                    <p><strong>Date:</strong> {new Date(currentWithdrawal.createdAt).toLocaleString()}</p>
                                </div>
                                <div>
                                    <p><strong>Method:</strong> {currentWithdrawal.withdrawalMethod}</p>
                                    <p><strong>KYC Level:</strong> {currentWithdrawal.kycLevelAtTime}</p>
                                    <p><strong>Withdrawal Address:</strong> {currentWithdrawal.withdrawalAddress}</p>
                                    <p><strong>Status:</strong> {getStatusTag(currentWithdrawal.status)}</p>
                                </div>
                            </div>

                            {currentWithdrawal.withdrawalMethod === 'BANK_TRANSFER' && currentWithdrawal.additionalDetails && (
                                <div className="mt-4 p-3 bg-gray-50 rounded">
                                    <p><strong>Bank Details:</strong></p>
                                    <p>Bank Name: {currentWithdrawal.additionalDetails.bankName}</p>
                                    <p>Account Holder: {currentWithdrawal.additionalDetails.accountHolder}</p>
                                    <p>Account Number: {currentWithdrawal.additionalDetails.accountNumber}</p>
                                    <p>Routing Number: {currentWithdrawal.additionalDetails.routingNumber}</p>
                                </div>
                            )}
                        </div>

                        <Form form={form} layout="vertical">
                            <Form.Item
                                name="status"
                                label="Update Status"
                                rules={[{ required: true, message: 'Please select a status' }]}
                            >
                                <Select>
                                    <Option value="APPROVED">Approve Withdrawal</Option>
                                    <Option value="COMPLETED">Mark as Completed</Option>
                                    <Option value="REJECTED">Reject Withdrawal</Option>
                                </Select>
                            </Form.Item>

                            <Form.Item
                                name="comment"
                                label="Comment"
                                rules={[{ required: true, message: 'Please enter a comment' }]}
                            >
                                <TextArea rows={4} placeholder="Enter reason for approval/rejection or transaction details" />
                            </Form.Item>

                            <Form.Item
                                name="transactionHash"
                                label="Transaction Hash (for crypto withdrawals)"
                                rules={[{ required: false }]}
                            >
                                <Input placeholder="Enter transaction hash if applicable" />
                            </Form.Item>
                        </Form>
                    </div>
                )}
            </Modal>
        </Card>
    );
};

export default WithdrawalManagement;