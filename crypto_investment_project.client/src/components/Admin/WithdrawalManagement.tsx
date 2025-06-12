// First, let's update the WithdrawalManagement component to properly handle the API response
import React, { useState, useEffect } from 'react';
import { Table, Tag, Button, Modal, Form, Input, Select, Card, Tooltip, Alert } from 'antd';
import { ClockCircleOutlined, CheckCircleOutlined, CloseCircleOutlined } from '@ant-design/icons';
import type { TablePaginationConfig } from 'antd/es/table';
import { Withdrawal } from '../../types/withdrawal';
import api from '../../services/api';
import * as balanceService from '../../services/balance';
import withdrawalService from '../../services/withdrawalService';
import { Balance } from '../../types/balanceTypes';

const { Option } = Select;
const { TextArea } = Input;

// Properly define the pagination interface
interface PaginatedResult<T> {
    page: number;
    pageSize: number;
    totalCount: number;
    totalPages: number;
    items: T[];
    hasPreviousPage: boolean;
    hasNextPage: boolean;
}

const WithdrawalManagement: React.FC = () => {
    const [withdrawals, setWithdrawals] = useState<Withdrawal[]>([]);
    const [loading, setLoading] = useState<boolean>(true);
    const [isModalLoading, setModalLoading] = useState<boolean>(true);
    const [pagination, setPagination] = useState<TablePaginationConfig>({
        current: 1,
        pageSize: 10,
        total: 0
    });
    const [modalVisible, setModalVisible] = useState<boolean>(false);
    const [currentWithdrawal, setCurrentWithdrawal] = useState<Withdrawal | null>(null);
    const [currentBalance, setCurrentBalance] = useState<Balance | null>(null);
    const [currentPendingsTotal, setPendingsTotal] = useState<number | null>(null);
    const [processing, setProcessing] = useState<boolean>(false);
    const [error, setError] = useState<string | null>(null);
    const [form] = Form.useForm();

    useEffect(() => {
        fetchPendingWithdrawals();
    }, [pagination.current]);

    const fetchPendingWithdrawals = async (): Promise<void> => {
        try {
            setLoading(true);

            const response = await api.safeRequest('get', `/withdrawal/pending?page=${pagination.current || 1}&pageSize=${pagination.pageSize || 10}`);

            console.log("WithdrawalManagement::fetchPendingWithdrawals => response: ", response)

            if (!response || !response.success || !response.data) {
                throw new Error(response.message || `Failed to fetch pending withdrawals: ${response.statusCode} ${response.message}`);
            }

            var data : PaginatedResult<Withdrawal> = response.data;


            if (Array.isArray(data.items)) {
                setWithdrawals(data.items);
                setPagination({
                    ...pagination,
                    total: data.items.length,
                });
            } else {
                console.error('Unexpected response structure:', data);
                throw new Error('Unexpected response structure from server');
            }
        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : 'An unknown error occurred';
            setError(errorMessage);
            console.error('Error fetching pending withdrawals:', err);
        } finally {
            setLoading(false);
        }
    };

    // Handle table pagination changes
    const handleTableChange = (newPagination: TablePaginationConfig): void => {
        setPagination({
            current: newPagination.current,
            pageSize: newPagination.pageSize,
            total: pagination.total
        });
    };

    const showProcessModal = async (withdrawal: Withdrawal): Promise<void> => {
        try {
            setModalVisible(true);

            setModalLoading(true);
            setCurrentWithdrawal(withdrawal);

            var currentBalance = await balanceService.getBalance(withdrawal.currency);
            setCurrentBalance(currentBalance);

            var pendingsTotal = await withdrawalService.getPendingTotals(withdrawal.currency);
            setPendingsTotal(pendingsTotal);

            form.setFieldsValue({
                status: '',
                comment: '',
                transactionHash: withdrawal.transactionHash || ''
            });

        } catch (e) {
            const errorMessage = e instanceof Error ? e.message : 'An unknown error occurred';
            setError(errorMessage);
            console.error('Error fetching withdrawal details: ', e);
        } finally {
            setModalLoading(false);
        }
        
        
    };

    const handleModalCancel = (): void => {
        setModalVisible(false);
    };

    const handleModalSubmit = async (): Promise<void> => {
        try {
            setError(null);
            const values = await form.validateFields();
            setProcessing(true);

            if (!currentWithdrawal) {
                throw new Error('No withdrawal selected');
            }

            const payload = {
                status: values.status,
                comment: values.comment,
                transactionHash: null
            };

            // Add transaction hash if provided
            if (values.transactionHash) {
                payload.transactionHash = values.transactionHash;
            }

            const response = await api.safeRequest('put', `/withdrawal/${currentWithdrawal.id}/update-status`, payload);

            if (!response || !response.success || response.data == null) {
                throw new Error(response.message || `Failed to update withdrawal status: ${response.statusCode} ${response.message}`);
            }

            setModalVisible(false);
            fetchPendingWithdrawals();
        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : 'An unknown error occurred';
            setError(errorMessage);
            console.error('Error updating withdrawal status:', err);
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
            case 'CANCELLED':
                return <Tag icon={<CloseCircleOutlined />} color="default">Cancelled</Tag>;
            case 'FAILED':
                return <Tag icon={<CloseCircleOutlined />} color="volcano">Failed</Tag>;
            default:
                return <Tag>{status}</Tag>;
        }
    };

    const columns = [
        {
            title: 'Date',
            dataIndex: 'createdAt',
            key: 'createdAt',
            render: (text: string) => text ? new Date(text).toLocaleString() : '-',
        },
        {
            title: 'User ID',
            dataIndex: 'userId',
            key: 'userId',
            ellipsis: true,
            render: (text: string) => text ? (
                <Tooltip title={text}>
                    <span>{text.substring(0, 8)}...</span>
                </Tooltip>
            ) : '-',
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
            render: (_: any, record: Withdrawal) => record.amount ? (
                <span>
                    {record.amount} {record.currency}
                </span>
            ) : '-',
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
                    case 'PAYPAL':
                        return 'PayPal';
                    case 'CREDIT_CARD':
                        return 'Credit Card';
                    default:
                        return method || '-';
                }
            },
        },
        {
            title: 'KYC Level',
            dataIndex: 'kycLevelAtTime',
            key: 'kycLevelAtTime',
            render: (text: string) => text || '-',
        },
        {
            title: 'Status',
            dataIndex: 'status',
            key: 'status',
            render: (status: string) => status ? getStatusTag(status) : '-',
        },
        {
            title: 'Actions',
            key: 'actions',
            render: (_: any, record: Withdrawal) => (
                <Button
                    type="primary"
                    onClick={() => showProcessModal(record)}
                    disabled={record.status !== 'PENDING'}
                >
                    Process
                </Button>
            ),
        },
    ];

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
                scroll={{ x: 'max-content' }}
            />

            <Modal
                title="Process Withdrawal"
                open={modalVisible}
                onCancel={handleModalCancel}
                onOk={handleModalSubmit}
                okButtonProps={{ loading: processing }}
                width={700}
                loading={isModalLoading }
            >
                {currentWithdrawal && (
                    <div>
                        <div className="mb-4">
                            <div className="grid grid-cols-2 gap-4">
                                <div>
                                    <p><strong>User ID:</strong> {currentWithdrawal.userId}</p>
                                    <p><strong>Withdrawal Amount:</strong> {currentWithdrawal.amount} {currentWithdrawal.currency}</p>
                                    <p><strong>Available Amount:</strong> {currentBalance?.available} {currentWithdrawal.currency}</p>
                                    <p><strong>Pending Withdrawals Total:</strong> {currentPendingsTotal} {currentWithdrawal.currency}</p>
                                    <p><strong>Requested By:</strong> {currentWithdrawal.requestedBy}</p>
                                    <p><strong>Date:</strong> {currentWithdrawal.createdAt ? new Date(currentWithdrawal.createdAt).toLocaleString() : '-'}</p>
                                </div>
                                <div>
                                    <p><strong>Method:</strong> {currentWithdrawal.withdrawalMethod}</p>
                                    <p><strong>KYC Level:</strong> {currentWithdrawal.kycLevelAtTime || '-'}</p>
                                    <p><strong>Withdrawal Address:</strong> {currentWithdrawal.withdrawalAddress}</p>
                                    <p><strong>Status:</strong> {getStatusTag(currentWithdrawal.status)}</p>
                                </div>
                            </div>

                            {currentWithdrawal.withdrawalMethod === 'BANK_TRANSFER' && currentWithdrawal.additionalDetails && (
                                <div className="mt-4 p-3 bg-gray-50 rounded">
                                    <p><strong>Bank Details:</strong></p>
                                    <p>Bank Name: {currentWithdrawal.additionalDetails.bankName || '-'}</p>
                                    <p>Account Holder: {currentWithdrawal.additionalDetails.accountHolder || '-'}</p>
                                    <p>Account Number: {currentWithdrawal.additionalDetails.accountNumber || '-'}</p>
                                    <p>Routing Number: {currentWithdrawal.additionalDetails.routingNumber || '-'}</p>
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
                                rules={[{ required: false, message: 'Please enter a comment' }]}
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