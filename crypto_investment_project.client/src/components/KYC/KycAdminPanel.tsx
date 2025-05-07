// crypto_investment_project.client/src/components/KYC/KycAdminPanel.tsx
import React, { useState, useEffect } from 'react';
import { Table, Button, Modal, Form, Input, Select, Card, Tag, Badge } from 'antd';
import { CheckCircleOutlined, CloseCircleOutlined, QuestionCircleOutlined, ExclamationCircleOutlined } from '@ant-design/icons';

const { Option } = Select;
const { TextArea } = Input;

// Define types for the KYC verification record
interface KycVerification {
    id: string;
    userId: string;
    status: VerificationStatus;
    verificationLevel: VerificationLevel;
    isPoliticallyExposed: boolean;
    isHighRisk: boolean;
    riskScore?: number;
    submittedAt?: string;
    lastCheckedAt?: string;
}

// Define types for status and verification level
type VerificationStatus = 'APPROVED' | 'REJECTED' | 'PENDING_VERIFICATION' | 'NEEDS_REVIEW' | 'ADDITIONAL_INFO_REQUIRED';
type VerificationLevel = 'NONE' | 'BASIC' | 'STANDARD' | 'ADVANCED' | 'ENHANCED';

// Define pagination type
interface PaginationConfig {
    current: number;
    pageSize: number;
    total: number;
}

const KycAdminPanel: React.FC = () => {
    const [verifications, setVerifications] = useState<KycVerification[]>([]);
    const [loading, setLoading] = useState<boolean>(true);
    const [pagination, setPagination] = useState<PaginationConfig>({ current: 1, pageSize: 10, total: 0 });
    const [modalVisible, setModalVisible] = useState<boolean>(false);
    const [currentRecord, setCurrentRecord] = useState<KycVerification | null>(null);
    const [form] = Form.useForm();

    useEffect(() => {
        fetchPendingVerifications();
    }, [pagination.current]);

    const fetchPendingVerifications = async (): Promise<void> => {
        try {
            setLoading(true);
            const response = await fetch(`/api/kyc/pending?page=${pagination.current}&pageSize=${pagination.pageSize}`, {
                headers: {
                    'Authorization': `Bearer ${localStorage.getItem('token')}`,
                },
            });

            if (!response.ok) {
                throw new Error('Failed to fetch pending verifications');
            }

            const data = await response.json();
            setVerifications(data.items);
            setPagination({
                ...pagination,
                total: data.totalCount,
            });
        } catch (err) {
            console.error('Error fetching verifications:', err);
        } finally {
            setLoading(false);
        }
    };

    // Define types for the Table onChange handler parameters
    interface TableChangeParams {
        pagination: PaginationConfig;
        filters: Record<string, (string | number | boolean)[] | null>;
        sorter: any; // Using any for sorter as it can be complex
        extra: {
            currentDataSource: KycVerification[];
            action: 'paginate' | 'sort' | 'filter';
        };
    }

    const handleTableChange = (
        pagination: PaginationConfig,
        _filters: TableChangeParams['filters'],
        _sorter: TableChangeParams['sorter'],
        _extra: TableChangeParams['extra']
    ): void => {
        // Only use the pagination parameter, ignoring filters, sorter, and extra
        setPagination(pagination);
    };

    const showUpdateModal = (record: KycVerification): void => {
        setCurrentRecord(record);
        form.setFieldsValue({
            status: record.status,
            comment: '',
        });
        setModalVisible(true);
    };

    const handleModalCancel = (): void => {
        setModalVisible(false);
    };

    const handleModalSubmit = async (): Promise<void> => {
        try {
            const values = await form.validateFields();

            if (currentRecord === null) {
                throw new Error('No record selected');
            }

            const response = await fetch(`/api/kyc/admin/update/${currentRecord.userId}`, {
                method: 'POST',
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
                throw new Error('Failed to update KYC status');
            }

            setModalVisible(false);
            fetchPendingVerifications();
        } catch (err) {
            console.error('Error updating KYC status:', err);
        }
    };

    const getStatusTag = (status: VerificationStatus): React.ReactNode => {
        switch (status) {
            case 'APPROVED':
                return <Tag color="success" icon={<CheckCircleOutlined />}>Approved</Tag>;
            case 'REJECTED':
                return <Tag color="error" icon={<CloseCircleOutlined />}>Rejected</Tag>;
            case 'PENDING_VERIFICATION':
                return <Tag color="processing" icon={<QuestionCircleOutlined />}>Pending</Tag>;
            case 'NEEDS_REVIEW':
                return <Tag color="warning" icon={<ExclamationCircleOutlined />}>Needs Review</Tag>;
            default:
                return <Tag>{status}</Tag>;
        }
    };

    const getRiskBadge = (record: KycVerification): React.ReactNode => {
        if (record.isPoliticallyExposed) {
            return <Badge status="warning" text="PEP" className="mr-2" />;
        }
        if (record.isHighRisk) {
            return <Badge status="error" text="High Risk" className="mr-2" />;
        }
        return null;
    };

    const columns = [
        {
            title: 'User ID',
            dataIndex: 'userId',
            key: 'userId',
            ellipsis: true,
        },
        {
            title: 'Status',
            dataIndex: 'status',
            key: 'status',
            render: (status: VerificationStatus) => getStatusTag(status),
        },
        {
            title: 'Level',
            dataIndex: 'verificationLevel',
            key: 'verificationLevel',
        },
        {
            title: 'Risk',
            key: 'risk',
            render: (_: any, record: KycVerification) => (
                <>
                    {getRiskBadge(record)}
                    {record.riskScore && <span>Score: {record.riskScore}</span>}
                </>
            ),
        },
        {
            title: 'Submitted',
            dataIndex: 'submittedAt',
            key: 'submittedAt',
            render: (date: string | undefined) => date ? new Date(date).toLocaleString() : '-',
        },
        {
            title: 'Last Checked',
            dataIndex: 'lastCheckedAt',
            key: 'lastCheckedAt',
            render: (date: string | undefined) => date ? new Date(date).toLocaleString() : '-',
        },
        {
            title: 'Actions',
            key: 'actions',
            render: (_: any, record: KycVerification) => (
                <Button type="primary" onClick={() => showUpdateModal(record)}>
                    Review
                </Button>
            ),
        },
    ];

    return (
        <Card title="KYC Verification Management" className="mb-5">
            <Table
                columns={columns}
                dataSource={verifications}
                rowKey="id"
                pagination={pagination}
                loading={loading}
                onChange={handleTableChange as any} // Type assertion to avoid complex typing issues with antd Table
            />

            <Modal
                title="Update KYC Status"
                visible={modalVisible}
                onCancel={handleModalCancel}
                onOk={handleModalSubmit}
                width={600}
            >
                {currentRecord && (
                    <div>
                        <div className="mb-4">
                            <p><strong>User ID:</strong> {currentRecord.userId}</p>
                            <p><strong>Current Status:</strong> {getStatusTag(currentRecord.status)}</p>
                            <p><strong>Verification Level:</strong> {currentRecord.verificationLevel}</p>
                            {currentRecord.isPoliticallyExposed && (
                                <p><strong>PEP Status:</strong> <Tag color="warning">Politically Exposed Person</Tag></p>
                            )}
                            {currentRecord.isHighRisk && (
                                <p><strong>Risk Status:</strong> <Tag color="error">High Risk</Tag></p>
                            )}
                            {currentRecord.riskScore && (
                                <p><strong>Risk Score:</strong> {currentRecord.riskScore}</p>
                            )}
                        </div>

                        <Form form={form} layout="vertical">
                            <Form.Item
                                name="status"
                                label="Update Status"
                                rules={[{ required: true, message: 'Please select a status' }]}
                            >
                                <Select>
                                    <Option value="APPROVED">Approve</Option>
                                    <Option value="REJECTED">Reject</Option>
                                    <Option value="ADDITIONAL_INFO_REQUIRED">Request Additional Info</Option>
                                </Select>
                            </Form.Item>
                            <Form.Item
                                name="comment"
                                label="Comment"
                                rules={[{ required: true, message: 'Please enter a comment' }]}
                            >
                                <TextArea rows={4} placeholder="Enter reason for approval/rejection or additional information needed" />
                            </Form.Item>
                        </Form>
                    </div>
                )}
            </Modal>
        </Card>
    );
};

export default KycAdminPanel;