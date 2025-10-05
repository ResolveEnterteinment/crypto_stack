import {
    CheckCircleOutlined,
    ClockCircleOutlined,
    CloseCircleOutlined,
    CopyOutlined, DeleteOutlined, ReloadOutlined
} from '@ant-design/icons';
import { Button, Card, Empty, message, Table, Typography } from 'antd';
import React, { useEffect, useState } from 'react';
import withdrawalService from '../../services/withdrawalService';
import { WithdrawalResponse } from '../../types/withdrawal';
import './WithdrawalHistory.css';

const { Text, Title } = Typography;

interface WithdrawalHistoryProps {
    onWithdrawalCancelled?: () => void;
}

const WithdrawalHistory: React.FC<WithdrawalHistoryProps> = ({ onWithdrawalCancelled }) => {
    const [withdrawals, setWithdrawals] = useState<WithdrawalResponse[]>([]);
    const [loading, setLoading] = useState<boolean>(true);
    const [cancelLoadingMap, setCancelLoadingMap] = useState<Record<string, boolean>>({});

    useEffect(() => {
        fetchWithdrawalHistory();
    }, []);

    const fetchWithdrawalHistory = async (): Promise<void> => {
        try {
            setLoading(true);
            const response = await withdrawalService.getHistory();
            setWithdrawals(response);
        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : 'Failed to load history';
            message.error(errorMessage);
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
                message.success('Withdrawal cancelled successfully');
                if (onWithdrawalCancelled) {
                    onWithdrawalCancelled();
                }
            } else {
                throw new Error('Failed to cancel withdrawal');
            }
        } catch (err) {
            const errorMessage = err instanceof Error ? err.message : 'Failed to cancel';
            message.error(errorMessage);
        } finally {
            setCancelLoadingMap(prev => ({ ...prev, [withdrawalId]: false }));
        }
    };

    const copyToClipboard = async (text: string): Promise<void> => {
        try {
            await navigator.clipboard.writeText(text);
            message.success('Copied to clipboard');
        } catch (err) {
            const textArea = document.createElement('textarea');
            textArea.value = text;
            document.body.appendChild(textArea);
            textArea.select();
            document.execCommand('copy');
            document.body.removeChild(textArea);
            message.success('Copied to clipboard');
        }
    };

    const formatHash = (hash: string, length: number = 6): string => {
        if (!hash || hash.length <= 12) return hash;
        return `${hash.slice(0, length)}...${hash.slice(-length)}`;
    };

    const getStatusDisplay = (status: string) => {
        const statusConfig: Record<string, { icon: React.ReactNode; color: string; backgroundColor: string, text: string }> = {
            'PENDING': {
                icon: <ClockCircleOutlined />,
                color: '#A39B00',
                backgroundColor: '#F8F5BE',
                text: 'Pending'
            },
            'APPROVED': {
                icon: <CheckCircleOutlined />,
                color: '#4E792A',
                backgroundColor: '#DCF6C1',
                text: 'Approved'
            },
            'COMPLETED': {
                icon: <CheckCircleOutlined />,
                color: '#4E792A',
                backgroundColor: '#DCF6C1',
                text: 'Completed'
            },
            'REJECTED': {
                icon: <CloseCircleOutlined />,
                color: '#FF4500',
                backgroundColor: '#F0B099',
                text: 'Rejected'
            },
            'CANCELLED': {
                icon: <CloseCircleOutlined />,
                color: '#8c8c8c',
                backgroundColor: '#E7E7E7',
                text: 'Cancelled'
            },
            'FAILED': {
                icon: <CloseCircleOutlined />,
                color: '#FF4500',
                backgroundColor: '#F0B099',
                text: 'Failed'
            }
        };

        const config = statusConfig[status] || { icon: null, color: '#d9d9d9', text: status };

        return (
            <div className="status-badge" style={{ borderColor: config.color, backgroundColor: config.backgroundColor}}>
                <span style={{ color: config.color }}>{config.icon}</span>
                <Text style={{ color: config.color, wordBreak: "keep-all" }}>{config.text}</Text>
            </div>
        );
    };

    const columns = [
        {
            title: 'Date',
            dataIndex: 'createdAt',
            key: 'createdAt',
            width: 140,
            render: (text: string) => {
                const date = new Date(text);
                return (
                    <div className="date-cell">
                        <Text strong>{date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' })}</Text>
                        <Text type="secondary">{date.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' })}</Text>
                    </div>
                );
            },
        },
        {
            title: 'Amount',
            key: 'amount',
            width: 150,
            render: (_: any, record: WithdrawalResponse) => (
                <div className="amount-cell">
                    <Text strong className="amount-value">{record.amount}</Text>
                    <Text type="secondary" className="amount-currency">{record.currency}</Text>
                </div>
            ),
        },
        {
            title: 'Method',
            dataIndex: 'withdrawalMethod',
            key: 'withdrawalMethod',
            width: 120,
            render: (method: string) => {
                const methodText = method === 'CRYPTO_TRANSFER' ? 'Crypto' : 'Bank';
                return <Text>{methodText}</Text>;
            },
        },
        {
            title: 'Status',
            dataIndex: 'status',
            key: 'status',
            width: 120,
            render: (status: string) => getStatusDisplay(status),
        },
        {
            title: 'Address',
            dataIndex: 'additionalDetails',
            key: 'address',
            width: 180,
            render: (details: any) => {
                if (!details?.WithdrawalAddress) return <Text type="secondary">-</Text>;

                return (
                    <div className="hash-cell">
                        <Text className="hash-text">{formatHash(details.WithdrawalAddress, 4)}</Text>
                        <Button
                            type="text"
                            size="small"
                            icon={<CopyOutlined />}
                            onClick={() => copyToClipboard(details.WithdrawalAddress)}
                            className="copy-button"
                        />
                    </div>
                );
            },
        },
        {
            title: 'Tx Hash',
            dataIndex: 'transactionHash',
            key: 'transactionHash',
            width: 180,
            render: (hash: string) => {
                if (!hash) return <Text type="secondary">-</Text>;

                return (
                    <div className="hash-cell">
                        <Text className="hash-text">{formatHash(hash, 4)}</Text>
                        <Button
                            type="text"
                            size="small"
                            icon={<CopyOutlined />}
                            onClick={() => copyToClipboard(hash)}
                            className="copy-button"
                        />
                    </div>
                );
            },
        },
        {
            title: 'Actions',
            key: 'actions',
            width: 100,
            fixed: 'right' as const,
            render: (_: any, record: WithdrawalResponse) => {
                if (record.status !== 'PENDING') return null;

                return (
                    <Button
                        danger
                        size="small"
                        icon={<DeleteOutlined />}
                        onClick={() => cancelWithdrawal(record.id)}
                        loading={cancelLoadingMap[record.id]}
                        disabled={cancelLoadingMap[record.id]}
                        className="cancel-button"
                    >
                        Cancel
                    </Button>
                );
            },
        },
    ];

    return (
        <div className="history-wrapper">
            <Card className="history-card" bordered={false}>
                {/* Header */}
                <div className="history-header">
                    <div className="header-content">
                        <div>
                            <Title level={4}>Transaction History</Title>
                            <Text type="secondary">
                                {withdrawals.length} {withdrawals.length === 1 ? 'transaction' : 'transactions'}
                            </Text>
                        </div>
                        <Button
                            icon={<ReloadOutlined />}
                            onClick={fetchWithdrawalHistory}
                            loading={loading}
                            className="refresh-button"
                        >
                            Refresh
                        </Button>
                    </div>
                </div>

                {/* Table */}
                <div className="history-table">
                    <Table
                        dataSource={withdrawals}
                        columns={columns}
                        rowKey="id"
                        loading={loading}
                        pagination={{
                            pageSize: 10,
                            showSizeChanger: false,
                            showTotal: (total) => `Total ${total} transactions`,
                        }}
                        scroll={{ x: 1000 }}
                        locale={{
                            emptyText: (
                                <Empty
                                    image={Empty.PRESENTED_IMAGE_SIMPLE}
                                    description={
                                        <div className="empty-state">
                                            <Text type="secondary">No withdrawal history found</Text>
                                            <Text type="secondary" style={{ fontSize: '12px' }}>
                                                Your completed withdrawals will appear here
                                            </Text>
                                        </div>
                                    }
                                />
                            )
                        }}
                    />
                </div>
            </Card>
        </div>
    );
};

export default WithdrawalHistory;