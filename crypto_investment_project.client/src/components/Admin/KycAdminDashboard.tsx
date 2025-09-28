import React, { useState, useEffect } from 'react';
import {
    Card, Table, Button, Tag, Modal, Form, Input, Select, DatePicker,
    Space, Tooltip, Avatar, Badge, Statistic, Row, Col,
    Typography, Descriptions, Timeline, Alert, message,
    Popconfirm, Tabs, Progress,
} from 'antd';
import {
    UserOutlined, EyeOutlined, CheckCircleOutlined, CloseCircleOutlined,
    WarningOutlined, ReloadOutlined, SearchOutlined, FileTextOutlined,
    ClockCircleOutlined, EditOutlined,
    CameraOutlined
} from '@ant-design/icons';
import dayjs from 'dayjs';
import type { ColumnsType } from 'antd/es/table';
import type { Dayjs } from 'dayjs';
import api from '../../services/api';

const { Title, Text, Paragraph } = Typography;
const { Option } = Select;
const { RangePicker } = DatePicker;
const { TextArea } = Input;
const { TabPane } = Tabs;

// Enhanced interfaces for production
interface KycVerification {
    id: string;
    userId: string;
    userEmail?: string;
    fullName?: string;
    status: 'PENDING' | 'APPROVED' | 'REJECTED' | 'NEEDS_REVIEW' | 'BLOCKED';
    verificationLevel: 'BASIC' | 'STANDARD' | 'ADVANCED';
    submittedAt: string;
    lastCheckedAt?: string;
    verifiedAt?: string;
    expiresAt?: string;
    riskLevel: 'LOW' | 'MEDIUM' | 'HIGH' | 'CRITICAL';
    riskScore: number;
    isPoliticallyExposed: boolean;
    isHighRisk: boolean;
    amlStatus?: 'CLEARED' | 'REVIEW_REQUIRED' | 'BLOCKED';
    personalInfo: Record<string, object>,
    liveCaptures: LiveCaptureInfo[];
    documents: DocumentInfo[];
    history: HistoryEntry[];
    securityFlags?: {
        requiresReview: boolean;
        failureReasons?: string[];
        highRiskIndicators?: string[];
        fraudIndicators?: string[];
    };
    verificationResults?: VerificationResults;
}

interface PaginatedResult {
    items: KycVerification[];
    page: number;
    pageSize: number;
    totalCount: number;
    totalPages: number;
    hasPreviousPage: boolean;
    hasNextPage: boolean;
}
interface LiveCaptureInfo {
    id: string;
    type: string;
    fileSize: number;
    uploadedAt: string;
    status: 'UPLOADED' | 'VERIFIED' | 'REJECTED';
    issues?: string[];
}

interface DocumentInfo {
    id: string;
    type: string;
    fileName: string;
    fileSize: number;
    uploadedAt: string;
    status: 'UPLOADED' | 'VERIFIED' | 'REJECTED';
    issues?: string[];
}

interface BiometricData {
    faceMatchScore?: number;
    livenessScore?: number;
    qualityScore?: number;
    verifiedAt?: string;
}

interface HistoryEntry {
    timestamp: string;
    action: string;
    performedBy: string;
    details?: Record<string, object>;
    previousStatus?: string;
    newStatus?: string;
}

interface VerificationResults {
    checks: VerificationCheck[];
    processingTime: number;
}

interface VerificationCheck {
    type: string;
    name: string;
    passed: boolean;
    details?: any;
}

interface DashboardStats {
    totalPending: number;
    totalApproved: number;
    totalRejected: number;
    highRiskCount: number;
    averageProcessingTime: number;
    automationRate: number;
}

// Helper function to safely render values
const safeRender = (value: any, fallback: string = 'N/A'): string => {
    if (typeof (value) === "string") return value;
    if (value === null || value === undefined) return fallback;
    try {
        return JSON.stringify(value);
    }
    catch {
        return value.toString();
    }
    
};

// Helper function to safely get numeric values
const safeNumber = (value: any, fallback: number = 0): number => {
    if (typeof value === 'number' && !isNaN(value)) return value;
    const parsed = parseFloat(value);
    return isNaN(parsed) ? fallback : parsed;
};

const KycAdminDashboard: React.FC = () => {
    const [verifications, setVerifications] = useState<KycVerification[]>([]);
    const [loading, setLoading] = useState(true);
    const [selectedVerification, setSelectedVerification] = useState<KycVerification | null>(null);
    const [detailsVisible, setDetailsVisible] = useState(false);
    const [statusUpdateVisible, setStatusUpdateVisible] = useState(false);
    const [bulkActionsVisible, setBulkActionsVisible] = useState(false);
    const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([]);
    const [filters, setFilters] = useState({
        status: 'all',
        riskLevel: 'all',
        verificationLevel: 'all',
        dateRange: null as [Dayjs, Dayjs] | null,
        search: ''
    });
    const [stats, setStats] = useState<DashboardStats | null>(null);
    const [activeTab, setActiveTab] = useState('pending');
    const [form] = Form.useForm();

    // Load data on component mount
    useEffect(() => {
        loadVerifications();
        loadStats();
    }, [filters, activeTab]);

    const loadVerifications = async () => {
        try {
            setLoading(true);
            const response = await api.get <PaginatedResult>('/v1/kyc/admin/pending');

            if (response.data && response.success) {
                // Safely extract data with fallbacks
                const items = response.data.items || [];
                // Ensure all items have required fields with safe fallbacks
                const processedItems = items.map((item: any) => ({
                    ...item,
                    id: safeRender(item.id, Math.random().toString()),
                    userId: safeRender(item.userId, ''),
                    riskScore: safeNumber(item.riskScore, 0),
                    documents: Array.isArray(item.documents) ? item.documents : [],
                    history: Array.isArray(item.history) ? item.history : [],
                    status: item.status || 'PENDING',
                    verificationLevel: item.verificationLevel || 'BASIC',
                    riskLevel: item.riskLevel || 'LOW',
                    submittedAt: item.submittedAt || new Date().toISOString(),
                    isPoliticallyExposed: Boolean(item.isPoliticallyExposed),
                    isHighRisk: Boolean(item.isHighRisk)
                }));

                console.log("KycAdminDashboard::loadVerifications => processedItems: ", processedItems);
                setVerifications(processedItems);
            } else {
                throw new Error('Failed to fetch verifications');
            }
        } catch (error) {
            console.error('Error loading verifications:', error);
            message.error('Failed to load KYC verifications');
            setVerifications([]); // Set empty array on error
        } finally {
            setLoading(false);
        }
    };

    const loadStats = async () => {
        try {
            const response = await api.get<DashboardStats>('/api/admin/kyc/stats');

            if (!response.success || response.data == null)
                throw new Error("");
            if (response.data && response.success) {
                const statsData = response.data || {};
                setStats({
                    totalPending: safeNumber(statsData.totalPending, 0),
                    totalApproved: safeNumber(statsData.totalApproved, 0),
                    totalRejected: safeNumber(statsData.totalRejected, 0),
                    highRiskCount: safeNumber(statsData.highRiskCount, 0),
                    averageProcessingTime: safeNumber(statsData.averageProcessingTime, 0),
                    automationRate: safeNumber(statsData.automationRate, 0)
                });
            }
        } catch (error) {
            console.error('Error loading stats:', error);
        }
    };

    const handleStatusUpdate = async (values: any) => {
        if (!selectedVerification) return;

        try {
            const response = await api.put(`/v1/kyc/admin/status/${selectedVerification.userId}`, {
                status: values.status,
                reason: values.reason,
                comments: values.comments
            });

            if (!response.success) {
                throw new Error('Failed to update status');
            }

            message.success('KYC status updated successfully');
            setStatusUpdateVisible(false);
            form.resetFields();
            loadVerifications();
        } catch (error) {
            console.error('Error updating status:', error);
            message.error('Failed to update KYC status');
        }
    };
    const handleViewDocument = async (id: string) => {
        try {
            // Show loading message
            const loadingMessage = message.loading('Loading document...', 0);

            // ✅ Use the API client with headers
            const response = await api.getBlob(`/v1/kyc/admin/document/${id}`, {
                includeHeaders: true
            });

            if (!response.success || !response.data) {
                throw new Error('Failed to retrieve document');
            }

            // ✅ Access headers from the response
            const headers = response.headers || {};
            const contentType = headers['content-type'] || 'application/octet-stream';

            // Create a blob URL from the response data
            const fileUrl = URL.createObjectURL(response.data);

            // Extract filename from content-disposition header if available
            let fileName = `document-${id}`;
            const contentDisposition = headers['content-disposition'];
            if (contentDisposition) {
                const filenameMatch = contentDisposition.match(/filename[^;=\n]*=((['"]).*?\2|[^;\n]*)/);
                if (filenameMatch && filenameMatch[1]) {
                    fileName = filenameMatch[1].replace(/['"]/g, '');
                }
            }

            // Close loading message
            loadingMessage();

            // Create a fallback download link
            const link = document.createElement('a');
            link.href = fileUrl;
            link.download = fileName;
            document.body.appendChild(link);
            link.click();
            document.body.removeChild(link);

            // Clean up blob URL after a delay
            setTimeout(() => URL.revokeObjectURL(fileUrl), 5000);

            message.success('Document opened successfully');
        } catch (error) {
            console.error('Error viewing document:', error);
            message.error('Failed to view KYC document');
        }
    };

    const handleViewLiveCapture = async (id: string) => {
        try {
            // Show loading message
            const loadingMessage = message.loading('Loading live capture images...', 0);

            // ✅ Use the API client with headers
            const response = await api.getBlob(`/v1/kyc/admin/live-capture/${id}`, {
                includeHeaders: true
            });

            console.log("KycAdminDashboard::handleViewLiveCapture => response: ", response);
            console.log("KycAdminDashboard::handleViewLiveCapture => response.data: ", response.data);

            if (!response.success) {
                throw new Error('Failed to retrieve live capture');
            }

            // Close loading message
            loadingMessage();

            const containerId = id + "_container";
            const containerElement = document.getElementById(containerId);

            if (!containerElement) {
                message.error('Container element not found');
                return;
            }

            // Clear existing content
            containerElement.innerHTML = '';

            // ✅ Access headers from the response
            const headers = response.headers || {};

            console.log("KycAdminDashboard::handleViewLiveCapture => headers: ", headers);

            const contentType = headers['content-type'] || '';

            console.log("KycAdminDashboard::handleViewLiveCapture => contentType: ", contentType);

            // Check if the response is a ZIP file (multiple images)
            if (contentType === 'application/zip' || contentType === 'application/x-zip-compressed') {
                // Handle ZIP file processing...
                const JSZip = (await import('jszip')).default;
                const zip = new JSZip();

                // ✅ Convert Blob to ArrayBuffer before loading into JSZip
                const arrayBuffer = await response.data.arrayBuffer();
                const zipContent = await zip.loadAsync(arrayBuffer);

                console.log("KycAdminDashboard::handleViewLiveCapture => zipContent: ", Object.entries(zipContent.files));

                const imageUrls: string[] = [];
                const imageNames: string[] = [];

                // Extract all image files from the ZIP
                for (const [filename, file] of Object.entries(zipContent.files)) {
                    if (!file.dir && (filename.endsWith('.jpg') || filename.endsWith('.jpeg') || filename.endsWith('.png'))) {
                        const imageBlob = await file.async('blob');
                        const imageUrl = URL.createObjectURL(imageBlob);
                        imageUrls.push(imageUrl);
                        imageNames.push(filename);
                    }
                }

                console.log("KycAdminDashboard::handleViewLiveCapture => imageUrls: ", imageUrls);
                console.log("KycAdminDashboard::handleViewLiveCapture => imageNames: ", imageNames);

                if (imageUrls.length > 0) {
                    // Create container div with grid layout
                    const gridContainer = document.createElement('div');
                    gridContainer.style.cssText = `
                        display: grid;
                        grid-template-columns: ${imageUrls.length === 1 ? '1fr' : 'repeat(auto-fit, minmax(200px, 1fr))'};
                        gap: 10px;
                        margin-top: 10px;
                        max-height: 400px;
                        overflow-y: auto;
                    `;

                    imageUrls.forEach((imageUrl, index) => {
                        const imageContainer = document.createElement('div');
                        imageContainer.style.cssText = 'text-align: center;';

                        const title = document.createElement('h5');
                        title.textContent = imageNames[index];
                        title.style.cssText = 'margin: 0 0 8px 0; color: #666; font-size: 12px;';

                        const img = document.createElement('img');
                        img.src = imageUrl;
                        img.alt = `Live Capture ${index + 1}`;
                        img.style.cssText = `
                            width: 100%;
                            max-width: 200px;
                            height: auto;
                            border: 1px solid #d9d9d9;
                            border-radius: 6px;
                            box-shadow: 0 2px 8px rgba(0,0,0,0.1);
                            cursor: pointer;
                        `;

                        // Add click handler to view full size
                        img.onclick = () => {
                            const fullSizeModal = document.createElement('div');
                            fullSizeModal.style.cssText = `
                                position: fixed;
                                top: 0;
                                left: 0;
                                width: 100%;
                                height: 100%;
                                background: rgba(0,0,0,0.8);
                                display: flex;
                                justify-content: center;
                                align-items: center;
                                z-index: 10000;
                                cursor: pointer;
                            `;

                            const fullSizeImg = document.createElement('img');
                            fullSizeImg.src = imageUrl;
                            fullSizeImg.style.cssText = 'max-width: 90%; max-height: 90%; border-radius: 6px;';

                            fullSizeModal.appendChild(fullSizeImg);
                            fullSizeModal.onclick = () => document.body.removeChild(fullSizeModal);
                            document.body.appendChild(fullSizeModal);
                        };

                        imageContainer.appendChild(title);
                        imageContainer.appendChild(img);
                        gridContainer.appendChild(imageContainer);
                    });

                    containerElement.appendChild(gridContainer);

                    // Clean up blob URLs after 30 seconds
                    setTimeout(() => {
                        imageUrls.forEach(url => URL.revokeObjectURL(url));
                    }, 30000);
                } else {
                    containerElement.innerHTML = '<p style="color: #999; font-style: italic;">No images found in the live capture archive</p>';
                }
            } else {
                // For single image, display in container
                const blob = new Blob([response.data], {
                    type: headers['content-type'] || 'image/jpeg'
                });
                const imageUrl = URL.createObjectURL(blob);

                const imageContainer = document.createElement('div');
                imageContainer.style.cssText = 'text-align: center; margin-top: 10px;';

                const img = document.createElement('img');
                img.src = imageUrl;
                img.alt = 'Live Capture';
                img.style.cssText = `
                    max-width: 100%;
                    max-height: 300px;
                    border: 1px solid #d9d9d9;
                    border-radius: 6px;
                    box-shadow: 0 2px 8px rgba(0,0,0,0.1);
                    cursor: pointer;
                `;

                // Add click handler to view full size
                img.onclick = () => {
                    const fullSizeModal = document.createElement('div');
                    fullSizeModal.style.cssText = `
                        position: fixed;
                        top: 0;
                        left: 0;
                        width: 100%;
                        height: 100%;
                        background: rgba(0,0,0,0.8);
                        display: flex;
                        justify-content: center;
                        align-items: center;
                        z-index: 10000;
                        cursor: pointer;
                    `;

                    const fullSizeImg = document.createElement('img');
                    fullSizeImg.src = imageUrl;
                    fullSizeImg.style.cssText = 'max-width: 90%; max-height: 90%; border-radius: 6px;';

                    fullSizeModal.appendChild(fullSizeImg);
                    fullSizeModal.onclick = () => document.body.removeChild(fullSizeModal);
                    document.body.appendChild(fullSizeModal);
                };

                imageContainer.appendChild(img);
                containerElement.appendChild(imageContainer);

                // Clean up blob URL after 30 seconds
                setTimeout(() => URL.revokeObjectURL(imageUrl), 30000);
            }

            message.success('Live capture images loaded successfully');
        } catch (error) {
            console.error('Error viewing live capture:', error);
            message.error('Failed to view KYC live capture');
        }
    };
    const handleBulkAction = async (action: string) => {
        try {
            const response = await api.post('/admin/kyc/bulk-action', {
                action,
                verificationIds: selectedRowKeys
            });

            if (!response.success) {
                throw new Error('Failed to perform bulk action');
            }

            message.success(`Bulk action completed: ${action}`);
            setBulkActionsVisible(false);
            setSelectedRowKeys([]);
            loadVerifications();
        } catch (error) {
            console.error('Error performing bulk action:', error);
            message.error('Failed to perform bulk action');
        }
    };

    const getStatusColor = (status: string) => {
        const colors = {
            'PENDING': 'orange',
            'APPROVED': 'green',
            'REJECTED': 'red',
            'NEEDS_REVIEW': 'gold',
            'BLOCKED': 'red'
        };
        return colors[status as keyof typeof colors] || 'default';
    };

    const getRiskColor = (level: string) => {
        const colors = {
            'LOW': 'green',
            'MEDIUM': 'orange',
            'HIGH': 'red',
            'CRITICAL': 'purple'
        };
        return colors[level as keyof typeof colors] || 'default';
    };

    const formatProcessingTime = (submittedAt: string, verifiedAt?: string) => {
        try {
            const start = dayjs(submittedAt);
            const end = verifiedAt ? dayjs(verifiedAt) : dayjs();
            const hours = end.diff(start, 'hour');

            if (hours < 24) {
                return `${hours}h`;
            } else {
                const days = Math.floor(hours / 24);
                return `${days}d ${hours % 24}h`;
            }
        } catch (error) {
            return 'N/A';
        }
    };

    const handleQuickStatusUpdate = async (verificationId: string, status: string) => {
        try {
            const verification = verifications.find(v => v.id === verificationId);
            if (!verification) return;

            const response = await api.put(`/v1/kyc/admin/status/${verification.userId}`, {
                status,
                reason: `Quick ${status.toLowerCase()} by admin`
            });

            if (!response.success) {
                throw new Error('Failed to update status');
            }

            message.success(`KYC ${status.toLowerCase()} successfully`);
            loadVerifications();
        } catch (error) {
            console.error('Error updating status:', error);
            message.error('Failed to update status');
        }
    };

    // Table columns configuration
    const columns: ColumnsType<KycVerification> = [
        {
            title: 'User',
            key: 'user',
            width: 200,
            render: (_, record) => (
                <div className="flex items-center space-x-3">
                    <Avatar size="large" icon={<UserOutlined />} />
                    <div>
                        <div className="font-medium text-gray-900">
                            {safeRender(record.personalInfo?.fullName)}
                        </div>
                        <div className="text-sm text-gray-500">{safeRender(record.userEmail)}</div>
                        <div className="text-xs text-gray-400">ID: {safeRender(record.userId).slice(0, 8)}...</div>
                    </div>
                </div>
            )
        },
        {
            title: 'Level',
            dataIndex: 'verificationLevel',
            key: 'verificationLevel',
            width: 100,
            render: (level) => (
                <Tag color={level === 'ADVANCED' ? 'purple' : level === 'STANDARD' ? 'blue' : 'green'}>
                    {safeRender(level)}
                </Tag>
            )
        },
        {
            title: 'Status',
            dataIndex: 'status',
            key: 'status',
            width: 120,
            render: (status) => (
                <Tag color={getStatusColor(status)}>
                    {safeRender(status).replace('_', ' ')}
                </Tag>
            )
        },
        {
            title: 'Risk Assessment',
            key: 'risk',
            width: 150,
            render: (_, record) => (
                <div className="space-y-1">
                    <div className="flex items-center space-x-2">
                        <Tag color={getRiskColor(record.riskLevel)}>
                            {safeRender(record.riskLevel)}
                        </Tag>
                        <Text className="text-xs">{safeNumber(record.riskScore).toFixed(1)}</Text>
                    </div>
                    {record.isPoliticallyExposed && (
                        <Tag color="orange">PEP</Tag>
                    )}
                    {record.isHighRisk && (
                        <Tag color="red">HIGH RISK</Tag>
                    )}
                </div>
            )
        },
        {
            title: 'Submitted',
            dataIndex: 'submittedAt',
            key: 'submittedAt',
            width: 130,
            render: (date) => {
                try {
                    return (
                        <div>
                            <div className="text-sm">{dayjs(date).format('MMM DD, YYYY')}</div>
                            <div className="text-xs text-gray-500">{dayjs(date).format('HH:mm')}</div>
                        </div>
                    );
                } catch (error) {
                    return <div className="text-sm">Invalid Date</div>;
                }
            }
        },
        {
            title: 'Processing Time',
            key: 'processingTime',
            width: 120,
            render: (_, record) => (
                <div className="text-sm">
                    {formatProcessingTime(record.submittedAt, record.verifiedAt)}
                </div>
            )
        },
        {
            title: 'Live Captures',
            key: 'liveCaptures',
            width: 100,
            render: (_, record) => (
                <div className="text-center">
                    <Badge count={record.liveCaptures ? record.liveCaptures.length : 0} showZero>
                        <CameraOutlined className="text-lg text-gray-500" />
                    </Badge>
                </div>
            )
        },
        {
            title: 'Documents',
            key: 'documents',
            width: 100,
            render: (_, record) => (
                <div className="text-center">
                    <Badge count={record.documents ? record.documents.length : 0} showZero>
                        <FileTextOutlined className="text-lg text-gray-500" />
                    </Badge>
                </div>
            )
        },
        {
            title: 'Actions',
            key: 'actions',
            width: 150,
            fixed: 'right',
            render: (_, record) => (
                <Space size="small">
                    <Tooltip title="View Details">
                        <Button
                            type="text"
                            icon={<EyeOutlined />}
                            onClick={() => {
                                setSelectedVerification(record);
                                setDetailsVisible(true);
                            }}
                        />
                    </Tooltip>
                    {(record.status === 'PENDING' || record.status === 'NEEDS_REVIEW') ? (
                        <>
                            <Tooltip title="Approve">
                                <Popconfirm
                                    title="Are you sure to approve this verification?"
                                    onConfirm={() => handleQuickStatusUpdate(record.id, 'APPROVED')}
                                >
                                    <Button
                                        type="text"
                                        icon={<CheckCircleOutlined />}
                                        className="text-green-600 hover:text-green-700"
                                    />
                                </Popconfirm>
                            </Tooltip>
                            <Tooltip title="Reject">
                                <Button
                                    type="text"
                                    icon={<CloseCircleOutlined />}
                                    className="text-red-600 hover:text-red-700"
                                    onClick={() => {
                                        setSelectedVerification(record);
                                        setStatusUpdateVisible(true);
                                        form.setFieldsValue({ status: 'REJECTED' });
                                    }}
                                />
                            </Tooltip>
                        </>
                    ) : (
                        <Tooltip title="Update Status">
                            <Button
                                type="text"
                                icon={<EditOutlined />}
                                onClick={() => {
                                    setSelectedVerification(record);
                                    setStatusUpdateVisible(true);
                                }}
                            />
                        </Tooltip>
                    )}
                </Space>
            )
        }
    ];

    const renderDetailsModal = () => (
        <Modal
            title={
                <div className="flex items-center space-x-3">
                    <Avatar size="large" icon={<UserOutlined />} />
                    <div>
                        <Title level={4} className="m-0">
                            {safeRender(selectedVerification?.personalInfo?.fullName)}
                        </Title>
                        <Text className="text-gray-500">{safeRender(selectedVerification?.userEmail)}</Text>
                    </div>
                </div>
            }
            open={detailsVisible}
            onCancel={() => setDetailsVisible(false)}
            width={1200}
            footer={null}
            className="kyc-details-modal"
        >
            {selectedVerification && (
                <Tabs defaultActiveKey="overview" className="mt-4">
                    <TabPane tab="Overview" key="overview">
                        <Row gutter={24}>
                            <Col span={12}>
                                <Card title="Personal Information" size="small">
                                    <Descriptions column={1} size="small">
                                        {Object.entries(selectedVerification.personalInfo).map((pi) => (
                                            <Descriptions.Item label={pi[0]} key={pi[0]}>
                                                {(() => {
                                                    try {
                                                        if (typeof pi[1] === 'object' && Object.values(pi[1]).length > 0) {
                                                            return (
                                                                <div>
                                                                    {Object.entries(pi[1]).map(([key, value]) => (
                                                                        <div key={key}>
                                                                            <Tag>{safeRender(key)}</Tag>
                                                                            <Descriptions.Item label={key} key={key}>
                                                                                {safeRender(value)}
                                                                            </Descriptions.Item>
                                                                        </div>
                                                                    ))}
                                                                </div>
                                                            );
                                                        }
                                                        // If not a valid JSON object, just render the value
                                                        return safeRender(pi[1]);
                                                    } catch {
                                                        // If JSON parsing fails, render the original value
                                                        console.log("object values of parsed ", Object.values(pi[1]));
                                                        return safeRender(pi[1]);
                                                    }
                                                })()}
                                            </Descriptions.Item>
                                        ))}
                                    </Descriptions>
                                </Card>
                            </Col>

                            <Col span={12}>
                                <Card title="Verification Status" size="small">
                                    <div className="space-y-4">
                                        <div className="flex justify-between">
                                            <Text>Status:</Text>
                                            <Tag color={getStatusColor(selectedVerification.status)}>
                                                {safeRender(selectedVerification.status)}
                                            </Tag>
                                        </div>
                                        <div className="flex justify-between">
                                            <Text>Level:</Text>
                                            <Tag color="blue">{safeRender(selectedVerification.verificationLevel)}</Tag>
                                        </div>
                                        <div className="flex justify-between">
                                            <Text>Risk Level:</Text>
                                            <Tag color={getRiskColor(selectedVerification.riskLevel)}>
                                                {safeRender(selectedVerification.riskLevel)}
                                            </Tag>
                                        </div>
                                        <div className="flex justify-between">
                                            <Text>Risk Score:</Text>
                                            <Text strong>{safeNumber(selectedVerification.riskScore).toFixed(1)}</Text>
                                        </div>
                                        <div className="flex justify-between">
                                            <Text>AML Status:</Text>
                                            <Tag color={selectedVerification.amlStatus === 'CLEARED' ? 'green' : 'orange'}>
                                                {safeRender(selectedVerification.amlStatus, 'PENDING')}
                                            </Tag>
                                        </div>
                                    </div>
                                </Card>
                            </Col>
                        </Row>

                        {selectedVerification.securityFlags?.requiresReview && (
                            <Alert
                                message="Manual Review Required"
                                description={
                                    <div>
                                        {selectedVerification.securityFlags.failureReasons && (
                                            <div>
                                                <strong>Failure Reasons:</strong>
                                                <ul className="mt-1">
                                                    {selectedVerification.securityFlags.failureReasons.map((reason, index) => (
                                                        <li key={index}>{safeRender(reason)}</li>
                                                    ))}
                                                </ul>
                                            </div>
                                        )}
                                        {selectedVerification.securityFlags.highRiskIndicators && (
                                            <div className="mt-2">
                                                <strong>High Risk Indicators:</strong>
                                                <ul className="mt-1">
                                                    {selectedVerification.securityFlags.highRiskIndicators.map((indicator, index) => (
                                                        <li key={index}>{safeRender(indicator)}</li>
                                                    ))}
                                                </ul>
                                            </div>
                                        )}
                                    </div>
                                }
                                type="warning"
                                className="mt-4"
                            />
                        )}
                    </TabPane>

                    <TabPane tab="Live Captures" key="liveCaptures">
                        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                            {selectedVerification.liveCaptures.map((cap) => (
                                <Card key={cap.id} size="small" title={safeRender(cap.type).replace('_', ' ').toUpperCase()}>
                                    <div className="space-y-2">
                                        <div className="flex justify-between">
                                            <Text>Size:</Text>
                                            <Text>{(safeNumber(cap.fileSize) / 1024 / 1024).toFixed(2)} MB</Text>
                                        </div>
                                        <div className="flex justify-between">
                                            <Text>Status:</Text>
                                            <Tag color={cap.status === 'VERIFIED' ? 'green' : cap.status === 'REJECTED' ? 'red' : 'orange'}>
                                                {safeRender(cap.status)}
                                            </Tag>
                                        </div>
                                        <div className="flex justify-between">
                                            <Text>Uploaded:</Text>
                                            <Text>{dayjs(cap.uploadedAt).format('MMM DD, YYYY HH:mm')}</Text>
                                        </div>
                                        {cap.issues && cap.issues.length > 0 && (
                                            <div>
                                                <Text strong className="text-red-600">Issues:</Text>
                                                <ul className="mt-1">
                                                    {cap.issues.map((issue, index) => (
                                                        <li key={index} className="text-red-600 text-sm">{safeRender(issue)}</li>
                                                    ))}
                                                </ul>
                                            </div>
                                        )}
                                        <Button
                                            type="primary"
                                            size="small"
                                            icon={<EyeOutlined />}
                                            className="w-full"
                                            onClick={() => handleViewLiveCapture(cap.id)}
                                        >
                                            View Capture
                                        </Button>
                                    </div>
                                    <div id={cap.id + "_container"}></div>
                                </Card>
                            ))}
                        </div>
                    </TabPane>

                    <TabPane tab="Documents" key="documents">
                        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                            {selectedVerification.documents.map((doc) => (
                                <Card key={doc.id} size="small" title={safeRender(doc.type).replace('_', ' ').toUpperCase()}>
                                    <div className="space-y-2">
                                        <div className="flex justify-between">
                                            <Text>File Name:</Text>
                                            <Text className="text-right">{safeRender(doc.fileName)}</Text>
                                        </div>
                                        <div className="flex justify-between">
                                            <Text>Size:</Text>
                                            <Text>{(safeNumber(doc.fileSize) / 1024 / 1024).toFixed(2)} MB</Text>
                                        </div>
                                        <div className="flex justify-between">
                                            <Text>Status:</Text>
                                            <Tag color={doc.status === 'VERIFIED' ? 'green' : doc.status === 'REJECTED' ? 'red' : 'orange'}>
                                                {safeRender(doc.status)}
                                            </Tag>
                                        </div>
                                        <div className="flex justify-between">
                                            <Text>Uploaded:</Text>
                                            <Text>{dayjs(doc.uploadedAt).format('MMM DD, YYYY HH:mm')}</Text>
                                        </div>
                                        {doc.issues && doc.issues.length > 0 && (
                                            <div>
                                                <Text strong className="text-red-600">Issues:</Text>
                                                <ul className="mt-1">
                                                    {doc.issues.map((issue, index) => (
                                                        <li key={index} className="text-red-600 text-sm">{safeRender(issue)}</li>
                                                    ))}
                                                </ul>
                                            </div>
                                        )}
                                        <Button
                                            type="primary"
                                            size="small"
                                            icon={<EyeOutlined />}
                                            className="w-full"
                                            onClick={() => handleViewDocument(doc.id)}
                                        >
                                            View Document
                                        </Button>
                                    </div>
                                </Card>
                            ))}
                        </div>
                    </TabPane>

                    <TabPane tab="History" key="history">
                        <Timeline>
                            {selectedVerification.history.map((entry, index) => (
                                <Timeline.Item
                                    key={index}
                                    color={entry.action.includes('APPROVED') ? 'green' :
                                        entry.action.includes('REJECTED') ? 'red' : 'blue'}
                                >
                                    <div>
                                        <Text strong>{safeRender(entry.action)}</Text>
                                        <div className="text-sm text-gray-600">
                                            by {safeRender(entry.performedBy)} at {dayjs(entry.timestamp).format('MMM DD, YYYY HH:mm')}
                                        </div>
                                        {entry.details && Object.entries(entry.details).map(entry => (
                                            <Tag className="text-sm mt-1" >{entry[0]}: { safeRender(entry[1])}</Tag>
                                        ))}
                                        {entry.previousStatus && entry.newStatus && (
                                            <div className="text-sm mt-1">
                                                Status changed from <Tag>{safeRender(entry.previousStatus)}</Tag> to <Tag>{safeRender(entry.newStatus)}</Tag>
                                            </div>
                                        )}
                                    </div>
                                </Timeline.Item>
                            ))}
                        </Timeline>
                    </TabPane>

                    {selectedVerification.verificationResults && (
                        <TabPane tab="Verification Results" key="results">
                            <Card>
                                <div className="space-y-4">
                                    {selectedVerification.verificationResults.checks.map((check, index) => (
                                        <Card key={index} size="small">
                                            <div className="flex justify-between items-center">
                                                <div>
                                                    <Text strong>{safeRender(check.name)}</Text>
                                                    <div className="text-sm text-gray-600">{safeRender(check.type)}</div>
                                                </div>
                                                <div className="text-right">
                                                    <div className="flex items-center space-x-2">
                                                        {check.passed ? (
                                                            <CheckCircleOutlined className="text-green-600" />
                                                        ) : (
                                                            <CloseCircleOutlined className="text-red-600" />
                                                        )}
                                                    </div>
                                                </div>
                                            </div>
                                            {check.details && (
                                                <div className="mt-2 text-sm text-gray-600">
                                                    <pre className="whitespace-pre-wrap">
                                                        {safeRender(check.details)}
                                                    </pre>
                                                </div>
                                            )}
                                        </Card>
                                    ))}
                                </div>

                                <div className="mt-4 text-sm text-gray-600">
                                    Processing time: {safeNumber(selectedVerification.verificationResults.processingTime)}ms
                                </div>
                            </Card>
                        </TabPane>
                    )}
                </Tabs>
            )}
        </Modal>
    );

    const renderStatusUpdateModal = () => (
        <Modal
            title="Update KYC Status"
            open={statusUpdateVisible}
            onCancel={() => {
                setStatusUpdateVisible(false);
                form.resetFields();
            }}
            footer={null}
        >
            <Form
                form={form}
                onFinish={handleStatusUpdate}
                layout="vertical"
            >
                <Form.Item
                    name="status"
                    label="New Status"
                    rules={[{ required: true, message: 'Please select a status' }]}
                >
                    <Select>
                        <Option value="APPROVED">Approved</Option>
                        <Option value="REJECTED">Rejected</Option>
                        <Option value="NEEDS_REVIEW">Needs Review</Option>
                        <Option value="BLOCKED">Blocked</Option>
                    </Select>
                </Form.Item>

                <Form.Item
                    name="reason"
                    label="Reason"
                    rules={[{ required: true, message: 'Please provide a reason' }]}
                >
                    <Input placeholder="Enter reason for status change" />
                </Form.Item>

                <Form.Item
                    name="comments"
                    label="Additional Comments"
                >
                    <TextArea rows={4} placeholder="Optional additional comments" />
                </Form.Item>

                <Form.Item>
                    <Space>
                        <Button type="primary" htmlType="submit">
                            Update Status
                        </Button>
                        <Button onClick={() => {
                            setStatusUpdateVisible(false);
                            form.resetFields();
                        }}>
                            Cancel
                        </Button>
                    </Space>
                </Form.Item>
            </Form>
        </Modal>
    );

    return (
        <div className="min-h-screen bg-gray-50 p-6">
            <div className="max-w-full mx-auto">
                {/* Header */}
                <div className="mb-8">
                    <Title level={2} className="text-gray-800 mb-2">
                        KYC Administration Dashboard
                    </Title>
                    <Paragraph className="text-gray-600">
                        Manage and review user identity verifications
                    </Paragraph>
                </div>

                {/* Statistics Cards */}
                {stats && (
                    <Row gutter={24} className="mb-6">
                        <Col xs={24} sm={12} lg={6}>
                            <Card>
                                <Statistic
                                    title="Pending Reviews"
                                    value={stats.totalPending}
                                    prefix={<ClockCircleOutlined className="text-orange-500" />}
                                    valueStyle={{ color: '#fa8c16' }}
                                />
                            </Card>
                        </Col>
                        <Col xs={24} sm={12} lg={6}>
                            <Card>
                                <Statistic
                                    title="Approved"
                                    value={stats.totalApproved}
                                    prefix={<CheckCircleOutlined className="text-green-500" />}
                                    valueStyle={{ color: '#52c41a' }}
                                />
                            </Card>
                        </Col>
                        <Col xs={24} sm={12} lg={6}>
                            <Card>
                                <Statistic
                                    title="High Risk"
                                    value={stats.highRiskCount}
                                    prefix={<WarningOutlined className="text-red-500" />}
                                    valueStyle={{ color: '#ff4d4f' }}
                                />
                            </Card>
                        </Col>
                        <Col xs={24} sm={12} lg={6}>
                            <Card>
                                <Statistic
                                    title="Avg Processing Time"
                                    value={stats.averageProcessingTime}
                                    suffix="hrs"
                                    prefix={<ClockCircleOutlined />}
                                />
                            </Card>
                        </Col>
                    </Row>
                )}

                {/* Filters */}
                <Card className="mb-6">
                    <Row gutter={16} align="middle">
                        <Col xs={24} md={6}>
                            <Input
                                placeholder="Search by name, email, ID..."
                                prefix={<SearchOutlined />}
                                value={filters.search}
                                onChange={(e) => setFilters(prev => ({ ...prev, search: e.target.value }))}
                                allowClear
                            />
                        </Col>
                        <Col xs={24} md={4}>
                            <Select
                                placeholder="Status"
                                value={filters.status}
                                onChange={(value) => setFilters(prev => ({ ...prev, status: value }))}
                                style={{ width: '100%' }}
                            >
                                <Option value="all">All Statuses</Option>
                                <Option value="PENDING">Pending</Option>
                                <Option value="APPROVED">Approved</Option>
                                <Option value="REJECTED">Rejected</Option>
                                <Option value="NEEDS_REVIEW">Needs Review</Option>
                                <Option value="BLOCKED">Blocked</Option>
                            </Select>
                        </Col>
                        <Col xs={24} md={4}>
                            <Select
                                placeholder="Risk Level"
                                value={filters.riskLevel}
                                onChange={(value) => setFilters(prev => ({ ...prev, riskLevel: value }))}
                                style={{ width: '100%' }}
                            >
                                <Option value="all">All Risk Levels</Option>
                                <Option value="LOW">Low</Option>
                                <Option value="MEDIUM">Medium</Option>
                                <Option value="HIGH">High</Option>
                                <Option value="CRITICAL">Critical</Option>
                            </Select>
                        </Col>
                        <Col xs={24} md={6}>
                            <RangePicker
                                value={filters.dateRange}
                                onChange={(dates) => setFilters(prev => ({
                                    ...prev,
                                    dateRange: dates && dates[0] && dates[1] ? [dates[0], dates[1]] : null
                                }))}
                                style={{ width: '100%' }}
                            />
                        </Col>
                        <Col xs={24} md={4}>
                            <Space>
                                <Button
                                    type="primary"
                                    icon={<ReloadOutlined />}
                                    onClick={loadVerifications}
                                    loading={loading}
                                >
                                    Refresh
                                </Button>
                            </Space>
                        </Col>
                    </Row>
                </Card>

                {/* Main Table */}
                <Card>
                    <div className="mb-4 flex justify-between items-center">
                        <Tabs
                            activeKey={activeTab}
                            onChange={setActiveTab}
                            type="card"
                        >
                            <TabPane tab="Pending Reviews" key="pending" />
                            <TabPane tab="All Verifications" key="all" />
                            <TabPane tab="High Risk" key="high-risk" />
                        </Tabs>

                        {selectedRowKeys.length > 0 && (
                            <Button
                                type="primary"
                                onClick={() => setBulkActionsVisible(true)}
                            >
                                Bulk Actions ({selectedRowKeys.length})
                            </Button>
                        )}
                    </div>

                    <Table
                        columns={columns}
                        dataSource={verifications}
                        rowKey="id"
                        loading={loading}
                        scroll={{ x: 1200 }}
                        rowSelection={{
                            selectedRowKeys,
                            onChange: setSelectedRowKeys,
                            type: 'checkbox'
                        }}
                        pagination={{
                            total: verifications.length,
                            pageSize: 20,
                            showSizeChanger: true,
                            showQuickJumper: true,
                            showTotal: (total, range) =>
                                `${range[0]}-${range[1]} of ${total} verifications`
                        }}
                    />
                </Card>

                {/* Modals */}
                {renderDetailsModal()}
                {renderStatusUpdateModal()}

                {/* Bulk Actions Modal */}
                <Modal
                    title="Bulk Actions"
                    open={bulkActionsVisible}
                    onCancel={() => setBulkActionsVisible(false)}
                    footer={null}
                >
                    <div className="space-y-4">
                        <Text>Selected {selectedRowKeys.length} verifications</Text>
                        <div className="space-y-2">
                            <Button
                                block
                                type="primary"
                                onClick={() => handleBulkAction('APPROVE')}
                                className="bg-green-600 border-green-600"
                            >
                                Approve All
                            </Button>
                            <Button
                                block
                                onClick={() => handleBulkAction('REJECT')}
                                danger
                            >
                                Reject All
                            </Button>
                            <Button
                                block
                                onClick={() => handleBulkAction('NEEDS_REVIEW')}
                            >
                                Mark for Review
                            </Button>
                        </div>
                    </div>
                </Modal>
            </div>
        </div>
    );
};

export default KycAdminDashboard;