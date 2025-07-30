// src/components/KYC/EnhancedVerification.tsx - Enhanced Level KYC Verification Component
import React, { useState, useCallback } from 'react';
import {
    Card, Form, Input, Button, Steps, Row, Col, Typography, Alert, Space, Progress, Tag,
    Select, DatePicker, InputNumber
} from 'antd';
import {
    UserOutlined, BankOutlined, SafetyOutlined, CheckCircleOutlined,
    VideoCameraOutlined, CrownOutlined, SecurityScanOutlined
} from '@ant-design/icons';

const { Title, Text } = Typography;
const { Step } = Steps;
const { Option } = Select;
const { TextArea } = Input;

interface EnhancedVerificationProps {
    userId: string;
    sessionId: string;
    level: string;
    onComplete: (result: { success: boolean; level: string }) => void;
}

const EnhancedVerification: React.FC<EnhancedVerificationProps> = ({
    userId,
    sessionId,
    level,
    onComplete
}) => {
    const [currentStep, setCurrentStep] = useState(0);
    const [loading, setLoading] = useState(false);
    const [form] = Form.useForm();

    const steps = [
        {
            title: 'Comprehensive Profile',
            icon: <UserOutlined />,
            description: 'Complete comprehensive personal and professional profile'
        },
        {
            title: 'Financial Information',
            icon: <BankOutlined />,
            description: 'Detailed financial background and source of wealth'
        },
        {
            title: 'Video Verification',
            icon: <VideoCameraOutlined />,
            description: 'Live video call with verification specialist'
        },
        {
            title: 'Enhanced Due Diligence',
            icon: <SecurityScanOutlined />,
            description: 'Comprehensive background and compliance checks'
        },
        {
            title: 'Final Approval',
            icon: <CrownOutlined />,
            description: 'Senior review and final approval process'
        }
    ];

    const handleNext = useCallback(async () => {
        try {
            if (currentStep === 0 || currentStep === 1) {
                await form.validateFields();
            }

            if (currentStep < steps.length - 1) {
                setCurrentStep(currentStep + 1);
            } else {
                handleSubmit();
            }
        } catch (error) {
            console.error('Validation failed:', error);
        }
    }, [currentStep, form]);

    const handlePrev = useCallback(() => {
        setCurrentStep(currentStep - 1);
    }, [currentStep]);

    const handleSubmit = async () => {
        setLoading(true);
        try {
            // Simulate API call with extended processing for enhanced verification
            await new Promise(resolve => setTimeout(resolve, 4000));

            console.log('Enhanced verification completed for user:', userId, 'session:', sessionId, 'level:', level);

            onComplete({
                success: true,
                level: 'ENHANCED'
            });
        } catch (error) {
            console.error('Enhanced verification failed:', error);
            onComplete({
                success: false,
                level: 'ENHANCED'
            });
        } finally {
            setLoading(false);
        }
    };

    const renderStepContent = () => {
        switch (currentStep) {
            case 0:
                return (
                    <Form form={form} layout="vertical" requiredMark={false}>
                        <Alert
                            message="Enhanced Profile Information"
                            description="Enhanced verification requires comprehensive personal and professional information for institutional-grade compliance."
                            type="info"
                            showIcon
                            style={{ marginBottom: 24 }}
                        />
                        <Row gutter={16}>
                            <Col span={8}>
                                <Form.Item
                                    name="firstName"
                                    label="First Name"
                                    rules={[{ required: true, message: 'Please enter your first name' }]}
                                >
                                    <Input placeholder="Enter your first name" />
                                </Form.Item>
                            </Col>
                            <Col span={8}>
                                <Form.Item
                                    name="middleName"
                                    label="Middle Name(s)"
                                >
                                    <Input placeholder="Enter your middle name(s)" />
                                </Form.Item>
                            </Col>
                            <Col span={8}>
                                <Form.Item
                                    name="lastName"
                                    label="Last Name"
                                    rules={[{ required: true, message: 'Please enter your last name' }]}
                                >
                                    <Input placeholder="Enter your last name" />
                                </Form.Item>
                            </Col>
                        </Row>
                        <Row gutter={16}>
                            <Col span={12}>
                                <Form.Item
                                    name="dateOfBirth"
                                    label="Date of Birth"
                                    rules={[{ required: true, message: 'Please select your date of birth' }]}
                                >
                                    <DatePicker style={{ width: '100%' }} />
                                </Form.Item>
                            </Col>
                            <Col span={12}>
                                <Form.Item
                                    name="nationality"
                                    label="Nationality"
                                    rules={[{ required: true, message: 'Please select your nationality' }]}
                                >
                                    <Select placeholder="Select your nationality">
                                        <Option value="US">United States</Option>
                                        <Option value="UK">United Kingdom</Option>
                                        <Option value="CA">Canada</Option>
                                        <Option value="DE">Germany</Option>
                                    </Select>
                                </Form.Item>
                            </Col>
                        </Row>
                        <Row gutter={16}>
                            <Col span={12}>
                                <Form.Item
                                    name="profession"
                                    label="Profession/Title"
                                    rules={[{ required: true, message: 'Please enter your profession' }]}
                                >
                                    <Input placeholder="Your professional title" />
                                </Form.Item>
                            </Col>
                            <Col span={12}>
                                <Form.Item
                                    name="employer"
                                    label="Employer/Company"
                                    rules={[{ required: true, message: 'Please enter your employer' }]}
                                >
                                    <Input placeholder="Company name" />
                                </Form.Item>
                            </Col>
                        </Row>
                        <Form.Item
                            name="businessAddress"
                            label="Business Address"
                            rules={[{ required: true, message: 'Please enter your business address' }]}
                        >
                            <TextArea rows={3} placeholder="Complete business address" />
                        </Form.Item>
                    </Form>
                );

            case 1:
                return (
                    <Form form={form} layout="vertical" requiredMark={false}>
                        <Alert
                            message="Financial Information Required"
                            description="Please provide detailed financial information for enhanced due diligence and compliance screening."
                            type="warning"
                            showIcon
                            style={{ marginBottom: 24 }}
                        />
                        <Row gutter={16}>
                            <Col span={12}>
                                <Form.Item
                                    name="annualIncome"
                                    label="Annual Income (USD)"
                                    rules={[{ required: true, message: 'Please enter your annual income' }]}
                                >
                                    <InputNumber
                                        style={{ width: '100%' }}
                                        formatter={value => `$ ${value}`.replace(/\B(?=(\d{3})+(?!\d))/g, ',')}
                                        parser={value => value!.replace(/\$\s?|(,*)/g, '')}
                                        placeholder="Annual income"
                                    />
                                </Form.Item>
                            </Col>
                            <Col span={12}>
                                <Form.Item
                                    name="netWorth"
                                    label="Estimated Net Worth (USD)"
                                    rules={[{ required: true, message: 'Please enter your net worth' }]}
                                >
                                    <InputNumber
                                        style={{ width: '100%' }}
                                        formatter={value => `$ ${value}`.replace(/\B(?=(\d{3})+(?!\d))/g, ',')}
                                        parser={value => value!.replace(/\$\s?|(,*)/g, '')}
                                        placeholder="Net worth"
                                    />
                                </Form.Item>
                            </Col>
                        </Row>
                        <Row gutter={16}>
                            <Col span={12}>
                                <Form.Item
                                    name="sourceOfWealth"
                                    label="Primary Source of Wealth"
                                    rules={[{ required: true, message: 'Please select your source of wealth' }]}
                                >
                                    <Select placeholder="Select source of wealth">
                                        <Option value="employment">Employment/Salary</Option>
                                        <Option value="business">Business Ownership</Option>
                                        <Option value="investments">Investment Returns</Option>
                                        <Option value="inheritance">Inheritance</Option>
                                        <Option value="real_estate">Real Estate</Option>
                                        <Option value="other">Other</Option>
                                    </Select>
                                </Form.Item>
                            </Col>
                            <Col span={12}>
                                <Form.Item
                                    name="investmentExperience"
                                    label="Investment Experience"
                                    rules={[{ required: true, message: 'Please select your investment experience' }]}
                                >
                                    <Select placeholder="Select experience level">
                                        <Option value="none">No Experience</Option>
                                        <Option value="limited">Limited (1-3 years)</Option>
                                        <Option value="intermediate">Intermediate (3-10 years)</Option>
                                        <Option value="advanced">Advanced (10+ years)</Option>
                                        <Option value="professional">Professional</Option>
                                    </Select>
                                </Form.Item>
                            </Col>
                        </Row>
                        <Form.Item
                            name="sourceOfFundsDetail"
                            label="Detailed Source of Funds Explanation"
                            rules={[{ required: true, message: 'Please provide detailed explanation' }]}
                        >
                            <TextArea
                                rows={4}
                                placeholder="Please provide a detailed explanation of the source of funds you plan to use for cryptocurrency investments..."
                            />
                        </Form.Item>
                    </Form>
                );

            case 2:
                return (
                    <div style={{ textAlign: 'center' }}>
                        <Alert
                            message="Video Verification Required"
                            description="A live video call with our verification specialist is required for enhanced verification. This ensures the highest level of security and compliance."
                            type="info"
                            showIcon
                            style={{ marginBottom: 24 }}
                        />
                        <div style={{
                            border: '2px dashed #d9d9d9',
                            borderRadius: 8,
                            padding: 40,
                            minHeight: 300,
                            display: 'flex',
                            flexDirection: 'column',
                            justifyContent: 'center',
                            alignItems: 'center'
                        }}>
                            <VideoCameraOutlined style={{ fontSize: 64, color: '#1890ff', marginBottom: 16 }} />
                            <Title level={3}>Video Verification Call</Title>
                            <Text type="secondary" style={{ marginBottom: 24, maxWidth: 400 }}>
                                Schedule a live video call with our compliance team. The call typically takes 15-30 minutes and covers identity verification and compliance questions.
                            </Text>
                            <Space direction="vertical">
                                <Button
                                    type="primary"
                                    icon={<VideoCameraOutlined />}
                                    size="large"
                                >
                                    Schedule Video Call
                                </Button>
                                <Text type="secondary">Available Mon-Fri, 9AM-6PM EST</Text>
                            </Space>
                        </div>
                    </div>
                );

            case 3:
                return (
                    <div>
                        <Alert
                            message="Enhanced Due Diligence in Progress"
                            description="Comprehensive background checks, enhanced AML screening, and regulatory compliance verification are being performed."
                            type="info"
                            showIcon
                            style={{ marginBottom: 24 }}
                        />
                        <Card title="Due Diligence Progress" style={{ marginBottom: 16 }}>
                            <Space direction="vertical" style={{ width: '100%' }}>
                                <div>
                                    <Text>Identity & Document Verification:</Text>
                                    <Progress percent={100} size="small" status="success" />
                                </div>
                                <div>
                                    <Text>Enhanced AML Screening:</Text>
                                    <Progress percent={95} size="small" status="active" />
                                </div>
                                <div>
                                    <Text>Sanctions & PEP Screening:</Text>
                                    <Progress percent={90} size="small" status="active" />
                                </div>
                                <div>
                                    <Text>Source of Funds Verification:</Text>
                                    <Progress percent={85} size="small" status="active" />
                                </div>
                                <div>
                                    <Text>Regulatory Compliance Check:</Text>
                                    <Progress percent={80} size="small" status="active" />
                                </div>
                                <div>
                                    <Text>Risk Assessment:</Text>
                                    <Progress percent={75} size="small" status="active" />
                                </div>
                            </Space>
                        </Card>
                        <Alert
                            message="Additional Documentation May Be Required"
                            description="Based on our enhanced due diligence, we may request additional documentation to complete your verification."
                            type="warning"
                            showIcon
                        />
                    </div>
                );

            case 4:
                return (
                    <div>
                        <Alert
                            message="Enhanced Verification Under Senior Review"
                            description="Your application has passed all automated checks and is now under review by our senior compliance team for final approval."
                            type="success"
                            showIcon
                            style={{ marginBottom: 24 }}
                        />
                        <Card title="Verification Summary" style={{ marginBottom: 16 }}>
                            <Row gutter={[16, 16]}>
                                <Col span={8}>
                                    <Text strong>Comprehensive Profile:</Text>
                                    <br />
                                    <Text type="success">✓ Complete</Text>
                                </Col>
                                <Col span={8}>
                                    <Text strong>Financial Information:</Text>
                                    <br />
                                    <Text type="success">✓ Verified</Text>
                                </Col>
                                <Col span={8}>
                                    <Text strong>Video Verification:</Text>
                                    <br />
                                    <Text type="success">✓ Completed</Text>
                                </Col>
                                <Col span={8}>
                                    <Text strong>Enhanced Due Diligence:</Text>
                                    <br />
                                    <Text type="success">✓ Passed</Text>
                                </Col>
                                <Col span={8}>
                                    <Text strong>Risk Assessment:</Text>
                                    <br />
                                    <Text type="success">✓ Low Risk</Text>
                                </Col>
                                <Col span={8}>
                                    <Text strong>Compliance Status:</Text>
                                    <br />
                                    <Text type="success">✓ Compliant</Text>
                                </Col>
                            </Row>
                        </Card>
                        <Card title="What's Next?" style={{ marginBottom: 16 }}>
                            <Text>
                                Your enhanced verification application will be reviewed by our senior compliance team within 2-5 business days.
                                You will receive email notifications regarding the status of your application. Once approved, you will have
                                access to all institutional-grade features and higher transaction limits.
                            </Text>
                        </Card>
                    </div>
                );

            default:
                return null;
        }
    };

    return (
        <div style={{ maxWidth: 1000, margin: '0 auto' }}>
            <div style={{ marginBottom: 24, textAlign: 'center' }}>
                <Tag color="gold" style={{ marginBottom: 16 }}>Enhanced Verification</Tag>
                <Title level={3}>Institutional-Grade Enhanced Verification</Title>
                <Text type="secondary">
                    The highest level of verification with comprehensive due diligence, video verification, and senior compliance review.
                </Text>
            </div>

            <Card>
                <Steps current={currentStep} style={{ marginBottom: 32 }}>
                    {steps.map((step, index) => (
                        <Step
                            key={index}
                            title={step.title}
                            description={step.description}
                            icon={step.icon}
                        />
                    ))}
                </Steps>

                <div style={{ minHeight: 400, marginBottom: 24 }}>
                    {renderStepContent()}
                </div>

                {loading && (
                    <div style={{ textAlign: 'center', marginBottom: 24 }}>
                        <Progress percent={95} status="active" />
                        <Text>Processing enhanced verification with senior compliance review...</Text>
                    </div>
                )}

                <div style={{ textAlign: 'center' }}>
                    <Space>
                        {currentStep > 0 && (
                            <Button onClick={handlePrev} disabled={loading}>
                                Previous
                            </Button>
                        )}
                        <Button
                            type="primary"
                            onClick={handleNext}
                            loading={loading}
                            disabled={
                                (currentStep === 2 && !loading) || // Video verification step requires scheduling
                                (currentStep === 3 && !loading)    // Due diligence step is automated
                            }
                        >
                            {currentStep === steps.length - 1
                                ? 'Submit for Senior Review'
                                : currentStep === 2
                                    ? 'Continue After Video Call'
                                    : currentStep === 3
                                        ? 'Proceed to Review'
                                        : 'Next'
                            }
                        </Button>
                    </Space>
                </div>

                {/* Enhanced Verification Information Panel */}
                <div style={{
                    marginTop: 24,
                    padding: 16,
                    backgroundColor: '#f6f8fa',
                    borderRadius: 8,
                    border: '1px solid #e1e4e8'
                }}>
                    <Row gutter={24}>
                        <Col span={8}>
                            <div style={{ textAlign: 'center' }}>
                                <SecurityScanOutlined style={{ fontSize: 24, color: '#52c41a', marginBottom: 8 }} />
                                <div>
                                    <Text strong>Institutional Grade</Text>
                                </div>
                                <Text type="secondary">Highest security standards</Text>
                            </div>
                        </Col>
                        <Col span={8}>
                            <div style={{ textAlign: 'center' }}>
                                <CrownOutlined style={{ fontSize: 24, color: '#faad14', marginBottom: 8 }} />
                                <div>
                                    <Text strong>Senior Review</Text>
                                </div>
                                <Text type="secondary">Manual compliance review</Text>
                            </div>
                        </Col>
                        <Col span={8}>
                            <div style={{ textAlign: 'center' }}>
                                <SafetyOutlined style={{ fontSize: 24, color: '#1890ff', marginBottom: 8 }} />
                                <div>
                                    <Text strong>Full Compliance</Text>
                                </div>
                                <Text type="secondary">Regulatory compliant</Text>
                            </div>
                        </Col>
                    </Row>
                </div>

                {/* Processing Timeline for Enhanced Verification */}
                {currentStep >= 3 && (
                    <Alert
                        message="Enhanced Verification Timeline"
                        description={
                            <div>
                                <Text>Expected processing time: 2-5 business days</Text>
                                <br />
                                <Text type="secondary">
                                    • Initial automated screening: 1-2 hours
                                    <br />
                                    • Senior compliance review: 2-4 business days
                                    <br />
                                    • Final approval notification: Same business day
                                </Text>
                            </div>
                        }
                        type="info"
                        showIcon
                        style={{ marginTop: 16 }}
                    />
                )}
            </Card>

            {/* Enhanced Features Access Information */}
            <Card
                title="Enhanced Verification Benefits"
                style={{ marginTop: 24 }}
                size="small"
            >
                <Row gutter={[16, 16]}>
                    <Col span={12}>
                        <Space>
                            <CheckCircleOutlined style={{ color: '#52c41a' }} />
                            <Text>Unlimited transaction limits</Text>
                        </Space>
                    </Col>
                    <Col span={12}>
                        <Space>
                            <CheckCircleOutlined style={{ color: '#52c41a' }} />
                            <Text>Priority customer support</Text>
                        </Space>
                    </Col>
                    <Col span={12}>
                        <Space>
                            <CheckCircleOutlined style={{ color: '#52c41a' }} />
                            <Text>Access to institutional products</Text>
                        </Space>
                    </Col>
                    <Col span={12}>
                        <Space>
                            <CheckCircleOutlined style={{ color: '#52c41a' }} />
                            <Text>Dedicated account manager</Text>
                        </Space>
                    </Col>
                    <Col span={12}>
                        <Space>
                            <CheckCircleOutlined style={{ color: '#52c41a' }} />
                            <Text>Advanced trading features</Text>
                        </Space>
                    </Col>
                    <Col span={12}>
                        <Space>
                            <CheckCircleOutlined style={{ color: '#52c41a' }} />
                            <Text>Regulatory compliance certification</Text>
                        </Space>
                    </Col>
                </Row>
            </Card>
        </div>
    );
};

export default EnhancedVerification;