import React, { useState, useEffect } from 'react';
import { Table, Tag, Button, Card, Tooltip, Modal, Alert, Skeleton, Typography } from 'antd';
import { ClockCircleOutlined, CheckCircleOutlined, CloseCircleOutlined, ExclamationCircleOutlined } from '@ant-design/icons';
import { useAuth } from '../../context/AuthContext';

const { Text } = Typography;
const { confirm } = Modal;

// Define interfaces for type safety
interface WithdrawalData {
    id: string;
    amount: number;
    currency: string;
    withdrawalMethod: string;
    withdrawalAddress: string;
    status: string;
    createdAt: string;
    transactionHash?: string;
}

interface AuthContextType {
    user: any;
    // Add other auth properties as needed
}

const WithdrawalHistory: React.FC = () => {
    const { user } = useAuth() as AuthContextType;
    const [withdrawals, setWithdrawals] = useState<WithdrawalData[]>([]);
    const [loading, setLoading] = useState<boolean>(true);
    const [error, setError] = useState<string | null>(null);
    const [cancelLoading, setCancelLoading] = useState<boolean>(false);

    useEffect(() => {
        fetchWithdrawalHistory();
    }, []);

    const fetchWithdrawalHistory = async (): Promise<void> => {
        try {
            setLoading(true);
            const response = await fetch('/api/withdrawal/history', {
                headers: {
                    'Authorization': `Bearer ${localStorage.getItem('token')}`,
                },
            });

            if (!response.ok) {
                throw new Error('Failed to fetch withdrawal history');
            }

            const data = await response.json();
            setWithdrawals(data);
        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : 'An unknown error occurred';
            setError(errorMessage);
        } finally {
            setLoading(false);
        }
    };

    const handleCancelWithdrawal = async (withdrawalId: string): Promise<void> => {
        confirm({
            title: 'Are you sure you want to cancel this withdrawal request?',
            icon: <ExclamationCircleOutlined />,
            content: 'This action cannot be undone.',
            okText: 'Yes, Cancel',
            okType: 'danger',
            cancelText: 'No',
            onOk: async () => {
                try {
                    setCancelLoading(true);
                    const response = await fetch(`/api/withdrawal/${withdrawalId}/cancel`, {
                        method: 'PUT',
                        headers: {
                            'Authorization': `Bearer ${localStorage.getItem('token')}`,
                        },
                    });

                    if (!response.ok) {
                        const errorData = await response.json();
                        throw new Error(errorData.message || 'Failed to cancel withdrawal request');
                    }

                    // Refresh withdrawal history
                    fetchWithdrawalHistory();
                } catch (err) {
                    const errorMessage = err instanceof Error ? err.message : 'An unknown error occurred';
                    setError(errorMessage);
                } finally {
                    setCancelLoading(false);
                }
            },
        });
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
            render: (text: string) => new Date(text).toLocaleString(),
        },
        {
            title: 'Amount',
            key: 'amount',
            render: (_: any, record: WithdrawalData) => (
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
            title: 'Status',
            dataIndex: 'status',
            key: 'status',
            render: (status: string) => getStatusTag(status),
        },
        {
            title: 'Withdrawal Address',
            dataIndex: 'withdrawalAddress',
            key: 'withdrawalAddress',
            ellipsis: true,
            render: (text: string) => (
                <Tooltip title={text}>
                    <span>{text?.substring(0, 15)}...</span>
                </Tooltip>
            ),
        },
        {
            title: 'Transaction Hash',
            dataIndex: 'transactionHash',
            key: 'transactionHash',
            ellipsis: true,
            render: (text: string) => text ? (
                <Tooltip title={text}>
                    <span>{text?.substring(0, 15)}...</span>
                </Tooltip>
            ) : '-',
        },
        {
            title: 'Actions',
            key: 'actions',
            render: (_: any, record: WithdrawalData) => (
                record.status === 'PENDING' && (
                    <Button
                        danger
                        onClick={() => handleCancelWithdrawal(record.id)}
                        loading={cancelLoading}
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

            {withdrawals.length === 0 ? (
                <div className="text-center py-10">
                    <Text type="secondary">No withdrawal history found.</Text>
                </div>
            ) : (
                <Table
                    dataSource={withdrawals}
                    columns={columns}
                    rowKey="id"
                    pagination={{ pageSize: 10 }}
                />
            )}
        </Card>
    );
};

export default WithdrawalHistory;