// src/components/KYC/KycVerification.tsx
import { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../../context/AuthContext';
import { Card, Button, Alert, Form, Input, Steps, Select, Row, Col } from 'antd';
import { UserOutlined, IdcardOutlined, CheckCircleOutlined, SafetyCertificateOutlined } from '@ant-design/icons';
import { Spin } from 'antd';
import { kycProviders, getKycProviderConfig } from '../../config/kycProviders';

const { Step } = Steps;
const { Option } = Select;

// Define TypeScript interfaces for better type safety
interface KycStatus {
    status: string;
    verificationLevel: string;
    verifiedAt: string;
    providerName?: string;
}

interface FormData {
    firstName: string;
    lastName: string;
    dateOfBirth: string;
    address: string;
    country: string;
    city: string;
    postalCode: string;
    provider?: string;
}

interface AuthUser {
    id: string;
    roles?: string[];
}

const KycVerification = () => {
    const { user } = useAuth() as { user: AuthUser | null };
    const navigate = useNavigate();
    const [kycStatus, setKycStatus] = useState<KycStatus | null>(null);
    const [loading, setLoading] = useState<boolean>(true);
    const [error, setError] = useState<string | null>(null);
    const [currentStep, setCurrentStep] = useState<number>(0);
    const [formData, setFormData] = useState<FormData>({
        firstName: '',
        lastName: '',
        dateOfBirth: '',
        address: '',
        country: '',
        city: '',
        postalCode: '',
        provider: undefined
    });
    const [verificationUrl, setVerificationUrl] = useState<string | null>(null);
    const [isAdmin, setIsAdmin] = useState<boolean>(false);

    useEffect(() => {
        fetchKycStatus();
        // Check if user is admin
        setIsAdmin(user?.roles?.includes('ADMIN') || false);
    }, [user]);

    const fetchKycStatus = async () => {
        try {
            setLoading(true);
            const response = await fetch('/api/kyc/status', {
                headers: {
                    'Authorization': `Bearer ${localStorage.getItem('token')}`,
                },
            });

            if (!response.ok) {
                throw new Error('Failed to fetch KYC status');
            }

            const data = await response.json();
            setKycStatus(data);

            // Set current step based on status
            if (data.status === 'APPROVED') {
                setCurrentStep(3); // Completed
            } else if (data.status === 'PENDING_VERIFICATION' || data.status === 'IN_PROGRESS') {
                setCurrentStep(2); // Verification in progress
            } else if (data.status === 'NOT_STARTED') {
                setCurrentStep(0); // Start
            }
        } catch (error) {
            const errorMessage = error instanceof Error ? error.message : 'An unknown error occurred';
            setError(errorMessage);
        } finally {
            setLoading(false);
        }
    };

    const handleFormChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const { name, value } = e.target;
        setFormData(prev => ({ ...prev, [name]: value }));
    };

    const handleSelectChange = (name: string, value: string) => {
        setFormData(prev => ({ ...prev, [name]: value }));
    };

    const submitBasicInfo = async () => {
        try {
            setLoading(true);
            setCurrentStep(1);
            setLoading(false);
        } catch (error) {
            const errorMessage = error instanceof Error ? error.message : 'An unknown error occurred';
            setError(errorMessage);
            setLoading(false);
        }
    };

    const startVerification = async () => {
        try {
            if (!user) {
                throw new Error('User not authenticated');
            }

            setLoading(true);

            // Prepare the API URL - include provider if admin has selected one
            let url = '/api/kyc/verify';
            if (isAdmin && formData.provider) {
                url += `?provider=${formData.provider}`;
            }

            const response = await fetch(url, {
                method: 'POST',
                headers: {
                    'Authorization': `Bearer ${localStorage.getItem('token')}`,
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify({
                    userId: user.id,
                    verificationLevel: 'STANDARD',
                    userData: formData,
                }),
            });

            if (!response.ok) {
                throw new Error('Failed to initiate verification');
            }

            const data = await response.json();
            setVerificationUrl(data.verificationUrl);
            setCurrentStep(2);
            await fetchKycStatus(); // Refresh status
        } catch (error) {
            const errorMessage = error instanceof Error ? error.message : 'An unknown error occurred';
            setError(errorMessage);
        } finally {
            setLoading(false);
        }
    };

    const renderBasicInfoForm = () => (
        <Form layout="vertical" onFinish={submitBasicInfo}>
            <Form.Item
                label="First Name"
                rules={[{ required: true, message: 'Please enter your first name' }]}
            >
                <Input
                    name="firstName"
                    value={formData.firstName}
                    onChange={handleFormChange}
                    placeholder="Enter your first name"
                />
            </Form.Item>
            <Form.Item
                label="Last Name"
                rules={[{ required: true, message: 'Please enter your last name' }]}
            >
                <Input
                    name="lastName"
                    value={formData.lastName}
                    onChange={handleFormChange}
                    placeholder="Enter your last name"
                />
            </Form.Item>
            <Form.Item
                label="Date of Birth"
                rules={[{ required: true, message: 'Please enter your date of birth' }]}
            >
                <Input
                    name="dateOfBirth"
                    value={formData.dateOfBirth}
                    onChange={handleFormChange}
                    placeholder="YYYY-MM-DD"
                />
            </Form.Item>
            <Form.Item
                label="Country"
                rules={[{ required: true, message: 'Please enter your country' }]}
            >
                <Input
                    name="country"
                    value={formData.country}
                    onChange={handleFormChange}
                    placeholder="Enter your country"
                />
            </Form.Item>
            <Form.Item
                label="Address"
                rules={[{ required: true, message: 'Please enter your address' }]}
            >
                <Input
                    name="address"
                    value={formData.address}
                    onChange={handleFormChange}
                    placeholder="Enter your address"
                />
            </Form.Item>
            <Form.Item
                label="City"
                rules={[{ required: true, message: 'Please enter your city' }]}
            >
                <Input
                    name="city"
                    value={formData.city}
                    onChange={handleFormChange}
                    placeholder="Enter your city"
                />
            </Form.Item>
            <Form.Item
                label="Postal Code"
                rules={[{ required: true, message: 'Please enter your postal code' }]}
            >
                <Input
                    name="postalCode"
                    value={formData.postalCode}
                    onChange={handleFormChange}
                    placeholder="Enter your postal code"
                />
            </Form.Item>
            <Button type="primary" htmlType="submit">
                Next
            </Button>
        </Form>
    );

    const renderIDVerification = () => (
        <div className="text-center">
            <h3>ID Verification</h3>
            <p>Please verify your identity by uploading your ID documents.</p>
            <p>This helps us comply with KYC/AML regulations and protect your account.</p>

            {isAdmin && (
                <div className="mb-4">
                    <h4>Select KYC Provider</h4>
                    <Select
                        style={{ width: '100%', maxWidth: '300px' }}
                        placeholder="Select KYC Provider"
                        onChange={(value) => handleSelectChange('provider', value)}
                        value={formData.provider}
                    >
                        {kycProviders.map(provider => (
                            <Option key={provider.name} value={provider.name}>
                                {provider.displayName}
                            </Option>
                        ))}
                    </Select>
                </div>
            )}

            {/* Display provider cards */}
            {!isAdmin && (
                <Row gutter={16} className="mb-4">
                    {kycProviders.map(provider => (
                        <Col span={12} key={provider.name}>
                            <Card hoverable className="text-center">
                                <div className="flex justify-center mb-4">
                                    <SafetyCertificateOutlined style={{ fontSize: 36, color: '#1890ff' }} />
                                </div>
                                <h4>{provider.displayName}</h4>
                                <p className="text-sm">{provider.description}</p>
                            </Card>
                        </Col>
                    ))}
                </Row>
            )}

            <Button type="primary" onClick={startVerification}>
                Start ID Verification
            </Button>
        </div>
    );

    const renderVerificationProcess = () => {
        // Determine which provider is being used
        const providerName = kycStatus?.providerName || "Verification Service";
        const providerConfig = getKycProviderConfig(kycStatus?.providerName || "");

        return (
            <div className="text-center">
                <h3>Verification in Progress</h3>
                {providerConfig && (
                    <div className="mb-4">
                        <p>Using {providerConfig.displayName} for verification</p>
                    </div>
                )}

                {verificationUrl ? (
                    <div>
                        <p>Please complete your verification by clicking the button below:</p>
                        <Button type="primary" href={verificationUrl} target="_blank">
                            Complete Verification with {providerName}
                        </Button>
                        <Button className="mt-3" onClick={fetchKycStatus}>
                            I've Completed Verification
                        </Button>
                    </div>
                ) : (
                    <div>
                        <p>Your verification is being processed. This may take some time.</p>
                        <p>Current status: {kycStatus?.status}</p>
                        <Button onClick={fetchKycStatus}>
                            Check Status
                        </Button>
                    </div>
                )}
            </div>
        );
    };

    const renderVerificationComplete = () => (
        <div className="text-center">
            <h3>Verification Complete</h3>
            <CheckCircleOutlined style={{ fontSize: 64, color: '#52c41a' }} />
            <p className="mt-3">Your identity has been verified successfully.</p>
            <p>Verification level: {kycStatus?.verificationLevel}</p>
            <p>Verified on: {kycStatus?.verifiedAt ? new Date(kycStatus.verifiedAt).toLocaleDateString() : 'N/A'}</p>
            {kycStatus?.providerName && (
                <p>Verified via: {kycStatus.providerName}</p>
            )}
            <Button type="primary" onClick={() => navigate('/dashboard')}>
                Go to Dashboard
            </Button>
        </div>
    );

    if (loading) {
        return (
            <div className="text-center p-5">
                <Spin size="large" />
                <p>Loading KYC status...</p>
            </div>
        );
    }

    return (
        <Card title="Identity Verification" className="max-w-3xl mx-auto my-5">
            {error && (
                <Alert
                    message="Error"
                    description={error}
                    type="error"
                    closable
                    className="mb-4"
                />
            )}

            <Steps current={currentStep}>
                <Step title="Personal Info" icon={<UserOutlined />} />
                <Step title="ID Verification" icon={<IdcardOutlined />} />
                <Step title="Processing" icon={<Spin size="small" />} />
                <Step title="Complete" icon={<CheckCircleOutlined />} />
            </Steps>

            <div className="mt-5">
                {currentStep === 0 && renderBasicInfoForm()}
                {currentStep === 1 && renderIDVerification()}
                {currentStep === 2 && renderVerificationProcess()}
                {currentStep === 3 && renderVerificationComplete()}
            </div>
        </Card>
    );
};

export default KycVerification;