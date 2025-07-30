import React, { useState, useEffect } from 'react';
import { Table, Tag, Button, Card, Tooltip, Modal, Alert, Skeleton, Typography, message, Space, Spin } from 'antd';
import { ClockCircleOutlined, CheckCircleOutlined, CloseCircleOutlined, ExclamationCircleOutlined, CopyOutlined } from '@ant-design/icons';
import { WithdrawalResponse } from '../../types/withdrawal';
import withdrawalService from '../../services/withdrawalService';

const { Text } = Typography;

interface WithdrawalHistoryProps {
    onWithdrawalCancelled?: () => void;
}

const WithdrawalHistory: React.FC<WithdrawalHistoryProps> = ({ onWithdrawalCancelled }) => {
    const [withdrawals, setWithdrawals] = useState<WithdrawalResponse[]>([]);
    const [loading, setLoading] = useState<boolean>(true);
    const [error, setError] = useState<string | null>(null);
    const [cancelLoadingMap, setCancelLoadingMap] = useState<Record<string, boolean>>({});
    const [cancelModalOpen, setCancelModalOpen] = useState<boolean>(false);
    const [confirmModalOpen, setConfirmModalOpen] = useState<boolean>(false);
    const [withdrawalToCancel, setWithdrawalToCancel] = useState<string | null>(null);

    useEffect(() => {
        fetchWithdrawalHistory();
    }, []);

    const fetchWithdrawalHistory = async (): Promise<void> => {
        try {
            setLoading(true);
            const response = await withdrawalService.getHistory();
            setWithdrawals(response);
        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : 'An unknown error occurred';
            setError(errorMessage);
        } finally {
            setLoading(false);
        }
    };

    const cancelWithdrawal = async (withdrawalId: string): Promise<void> => {
        try {
            setCancelLoadingMap(prev => ({ ...prev, [withdrawalId]: true }));

            const success = await withdrawalService.cancelWithdrawal(withdrawalId);

            if (success) {
                await fetchWithdrawalHistory();
                message.success('Withdrawal request has been cancelled successfully.');
                // Trigger refresh of withdrawal limits
                if (onWithdrawalCancelled) {
                    onWithdrawalCancelled();
                }
            } else {
                throw new Error('Failed to cancel withdrawal request');
            }
        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : 'An unknown error occurred';
            setError(errorMessage);
            message.error(errorMessage);
        } finally {
            setCancelLoadingMap(prev => ({ ...prev, [withdrawalId]: false }));
        }
    };

    const handleConfirmCancel = async (): Promise<void> => {
        if (withdrawalToCancel) {
            setCancelModalOpen(false);
            await cancelWithdrawal(withdrawalToCancel);
            setWithdrawalToCancel(null);
        }
    };

    const handleCancelWithdrawal = (withdrawalId: string): void => {
        setWithdrawalToCancel(withdrawalId);
        setCancelModalOpen(true);
    };

    const handleCloseCancelModal = (): void => {
        setCancelModalOpen(false);
        setWithdrawalToCancel(null);
    };

    const copyToClipboard = async (text: string): Promise<void> => {
        try {
            await navigator.clipboard.writeText(text);
            message.success('Address copied to clipboard');
        } catch (err) {
            // Fallback for older browsers
            const textArea = document.createElement('textarea');
            textArea.value = text;
            document.body.appendChild(textArea);
            textArea.select();
            document.execCommand('copy');
            document.body.removeChild(textArea);
            message.success('Address copied to clipboard');
        }
    };

    const formatHashString = (address: string, count: number = 4): string => {
        if (!address || address.length <= 8) return address;
        return `${address.slice(0, count + 2)}...${address.slice(-count)}`;
    };

    const getStatusTag = (status: string): React.ReactNode => {
        switch (status) {
            case 'PENDING':
                return <Tag icon={<ClockCircleOutlined />} color="processing">Pending</Tag>;
            case 'APPROVED':
                return <Tag icon={<CheckCircleOutlined />} color="success">Approved</Tag>;
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
            render: (text: string) => new Date(text).toLocaleString(),
        },
        {
            title: 'Amount',
            key: 'amount',
            render: (_: any, record: WithdrawalResponse) => (
                <span>
                    {record.amount} {record.currency}
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
            title: 'Status',
            dataIndex: 'status',
            key: 'status',
            render: (status: string) => getStatusTag(status),
        },
        {
            title: 'Withdrawal Address',
            dataIndex: 'withdrawalAddress',
            key: 'withdrawalAddress',
            ellipsis: false,
            render: (text: string) => text ? (
                <Tooltip title={text} placement="top">
                    <Space size="small" onClick={(e) => {
                        e.stopPropagation();
                        copyToClipboard(text);
                    }}>
                        <span style={{ fontFamily: 'monospace', fontSize: '13px' }}>
                            {formatHashString(text)}
                        </span>
                        <Button
                            type="text"
                            size="small"
                            icon={<CopyOutlined />}
                            onClick={(e) => {
                                e.stopPropagation();
                                copyToClipboard(text);
                            }}
                            style={{
                                padding: '0 4px',
                                height: '20px',
                                width: '20px',
                                display: 'flex',
                                alignItems: 'center',
                                justifyContent: 'center'
                            }}
                        />
                    </Space>
                </Tooltip>
            ) : '-',
        },
        {
            title: 'Transaction Hash',
            dataIndex: 'transactionHash',
            key: 'transactionHash',
            ellipsis: true,
            render: (text: string) => text ? (
                <Tooltip title={text} placement="top">
                    <Space size="small" onClick={(e) => {
                        e.stopPropagation();
                        copyToClipboard(text);
                    }}>
                        <span style={{ fontFamily: 'monospace', fontSize: '13px' }}>
                            {formatHashString(text)}
                        </span>
                        <Button
                            type="text"
                            size="small"
                            icon={<CopyOutlined />}
                            onClick={(e) => {
                                e.stopPropagation();
                                copyToClipboard(text);
                            }}
                            style={{
                                padding: '0 4px',
                                height: '20px',
                                width: '20px',
                                display: 'flex',
                                alignItems: 'center',
                                justifyContent: 'center'
                            }}
                        />
                    </Space>
                </Tooltip>
            ) : '-',
        },
        {
            title: 'Actions',
            key: 'actions',
            render: (_: any, record: WithdrawalResponse) => (
                record.status === 'PENDING' && (
                    <Button
                        danger
                        size="small"
                        onClick={() => handleCancelWithdrawal(record.id)}
                        loading={cancelLoadingMap[record.id] || false}
                        disabled={cancelLoadingMap[record.id]}
                    >
                        Cancel
                    </Button>
                )
            ),
        },
    ];

    if (loading) {
        return (
            <Card title="Withdrawal History" className="max-w-6xl mx-auto my-5">
                <Skeleton active />
            </Card>
        );
    }

    return (
        <Spin spinning={loading} tip="Loading Transaction History..." className="max-w-3xl mx-auto my-5" >
            <Card title="Withdrawal History" className="max-w-6xl mx-auto my-5">
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

                {withdrawals && withdrawals.length > 0 ? (
                    <Table
                        dataSource={withdrawals}
                        columns={columns}
                        rowKey="id"
                        pagination={{ pageSize: 10 }}
                        scroll={{ x: 800 }}
                    />
                ) : (
                    <div className="text-center py-10">
                        <Text type="secondary">No withdrawal history found.</Text>
                    </div>
                )}

                {/* Cancel confirmation modal */}
                <Modal
                    title="Cancel Withdrawal Request"
                    open={cancelModalOpen}
                    onOk={handleConfirmCancel}
                    onCancel={handleCloseCancelModal}
                    okText="Yes, Cancel"
                    cancelText="No"
                    okType="danger"
                    confirmLoading={withdrawalToCancel ? cancelLoadingMap[withdrawalToCancel] : false}
                >
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                        <ExclamationCircleOutlined style={{ color: '#faad14', fontSize: '16px' }} />
                        <span>Are you sure you want to cancel this withdrawal request? This action cannot be undone.</span>
                    </div>
                </Modal>

            </Card>
        </Spin>
    );
};

export default WithdrawalHistory;