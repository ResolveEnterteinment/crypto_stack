import React, { useState, useMemo } from 'react';
import {
    Card,
    Form,
    Input,
    Button,
    Row,
    Col,
    Typography,
    Alert,
    Select,
    Space
} from 'antd';
import {
    UserOutlined,
    FileTextOutlined,
    HomeOutlined
} from '@ant-design/icons';
import dayjs from 'dayjs';
import { BasicVerificationData } from '../../types/kyc';
import { Country, State } from 'country-state-city';

const { Title, Text } = Typography;
const { Option } = Select;

interface BasicVerificationProps {
    userId: string;
    sessionId: string;
    level: string;
    onSubmit: (data: any) => void;
}

const BasicVerification: React.FC<BasicVerificationProps> = ({
    userId,
    sessionId,
    onSubmit,
}) => {
    const [form] = Form.useForm();
    const [submitting, setSubmitting] = useState(false);
    const [loading, setLoading] = useState(false);
    const [selectedCountry, setSelectedCountry] = useState<string>('');
    const [personalInfo, setPersonalInfo] = useState<BasicVerificationData>({
        personalInfo: {
            fullName: '',
            dateOfBirth: '',
            address: {
                street: '',
                city: '',
                state: '',
                zipCode: '',
                country: ''
            }
        }
    });

    // Get all countries using country-state-city package
    const countries = useMemo(() => {
        return Country.getAllCountries().map(country => ({
            value: country.isoCode,
            label: country.name,
            name: country.name
        }));
    }, []);

    // Get states/provinces for selected country
    const states = useMemo(() => {
        if (!selectedCountry) return [];
        return State.getStatesOfCountry(selectedCountry).map(state => ({
            value: state.isoCode,
            label: state.name,
            name: state.name
        }));
    }, [selectedCountry]);

    const handleCountryChange = (countryCode: string) => {
        setSelectedCountry(countryCode);
        // Reset state field when country changes
        form.setFieldsValue({ state: undefined });

        const country = countries.find(c => c.value === countryCode);
        setPersonalInfo(prev => ({
            ...prev,
            address: { ...prev.personalInfo.address, country: country?.name || '', state: '' }
        }));
    };

    const handleStateChange = (stateCode: string) => {
        const state = states.find(s => s.value === stateCode);
        setPersonalInfo(prev => ({
            ...prev,
            address: { ...prev.personalInfo.address, state: state?.name || '' }
        }));
    };

    const handleSubmit = async (values: any) => {
        try {
            setSubmitting(true);

            const selectedCountryData = countries.find(c => c.value === values.country);
            const selectedStateData = states.find(s => s.value === values.state);

            const data: BasicVerificationData = {
                personalInfo: {
                    fullName: values.fullName,
                    dateOfBirth: values.dateOfBirth ? dayjs(values.dateOfBirth).format('YYYY-MM-DD') : '',
                    address: {
                        street: values.street,
                        city: values.city,
                        state: selectedStateData?.name || values.state,
                        zipCode: values.zipCode,
                        country: selectedCountryData?.name || values.country
                    }
                }
            };

            await onSubmit(data);
        } catch (error) {
            console.error('Basic verification submission error:', error);
        } finally {
            setSubmitting(false);
        }
    };

    const validateAge = (_: any, value: any) => {
        if (!value) {
            return Promise.reject(new Error('Date of birth is required'));
        }

        const age = dayjs().diff(dayjs(value), 'year');
        if (age < 18) {
            return Promise.reject(new Error('You must be at least 18 years old'));
        }
        if (age > 120) {
            return Promise.reject(new Error('Please enter a valid date of birth'));
        }

        return Promise.resolve();
    };

    const validateName = (_: any, value: string) => {
        if (!value || value.trim().length === 0) {
            return Promise.reject(new Error('This field is required'));
        }

        // Only allow letters, spaces, hyphens, and apostrophes
        const nameRegex = /^[a-zA-Z\s\-']+$/;
        if (!nameRegex.test(value)) {
            return Promise.reject(new Error('Only letters, spaces, hyphens, and apostrophes are allowed'));
        }

        if (value.length < 2 || value.length > 50) {
            return Promise.reject(new Error('Name must be between 2 and 50 characters'));
        }

        return Promise.resolve();
    };

    return (
        <div style={{ maxWidth: 800, margin: '0 auto' }}>
            <Card>
                <div style={{ marginBottom: 24, textAlign: 'center' }}>
                    <UserOutlined style={{ fontSize: 48, color: '#1890ff', marginBottom: 16 }} />
                    <Title level={3}>Basic Identity Verification</Title>
                    <Text type="secondary">
                        Please provide your basic personal information to start the verification process.
                        All information must match your government-issued ID.
                    </Text>
                </div>

                <Alert
                    message="Information Required"
                    description="Please ensure all information matches exactly with your government-issued identification document."
                    type="info"
                    showIcon
                    style={{ marginBottom: 24 }}
                />

                <Form
                    form={form}
                    layout="vertical"
                    onFinish={handleSubmit}
                    requiredMark={false}
                    initialValues={{
                        country: 'US'
                    }}
                >
                    {/* Personal Information Section */}
                    <div style={{ marginBottom: 32 }}>
                        <Title level={4} style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                            <UserOutlined />
                            Personal Information
                        </Title>

                        <Row gutter={16}>
                            <Col xs={24} sm={12}>
                                <Form.Item
                                    name="fullName"
                                    label="Full Name"
                                    rules={[
                                        { validator: validateName }
                                    ]}
                                >
                                    <Input
                                        placeholder="Enter your full name"
                                        maxLength={50}
                                        onChange={(e) => setPersonalInfo((prev: BasicVerificationData) => ({ ...prev, fullName: e.target.value }))}
                                    />
                                </Form.Item>
                            </Col>
                        </Row>

                        <Row gutter={16}>
                            <Col xs={24} sm={12}>
                                <Form.Item
                                    label="Date of Birth"
                                    name="dateOfBirth"
                                    rules={[{ validator: validateAge }]}
                                >
                                    <Input
                                        size="large"
                                        type="date"
                                        max={new Date(Date.now() - 18 * 365 * 24 * 60 * 60 * 1000).toISOString().split('T')[0]}
                                        onChange={(e) => setPersonalInfo((prev: BasicVerificationData) => ({ ...prev, dateOfBirth: e.target.value }))}
                                    />
                                </Form.Item>
                            </Col>
                        </Row>
                    </div>

                    {/* Address Information Section */}
                    <div style={{ marginBottom: 32 }}>
                        <Title level={4} style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                            <HomeOutlined />
                            Address Information
                        </Title>

                        <Form.Item
                            name="street"
                            label="Street Address"
                            rules={[
                                { required: true, message: 'Please enter your street address' },
                                { min: 5, message: 'Street address must be at least 5 characters' }
                            ]}
                        >
                            <Input
                                placeholder="Enter your street address"
                                maxLength={100}
                                onChange={(e) => setPersonalInfo((prev: BasicVerificationData) => ({ ...prev, address: { ...prev.personalInfo.address, street: e.target.value } }))}
                            />
                        </Form.Item>

                        <Row gutter={16}>
                            <Col xs={24} sm={12}>
                                <Form.Item
                                    name="city"
                                    label="City"
                                    rules={[
                                        { required: true, message: 'Please enter your city' },
                                        { min: 2, message: 'City must be at least 2 characters' }
                                    ]}
                                >
                                    <Input
                                        placeholder="Enter your city"
                                        maxLength={50}
                                        onChange={(e) => setPersonalInfo((prev: BasicVerificationData) => ({ ...prev, address: { ...prev.personalInfo.address, city: e.target.value } }))}
                                    />
                                </Form.Item>
                            </Col>
                            <Col xs={24} sm={12}>
                                <Form.Item
                                    name="state"
                                    label="State/Province"
                                    rules={[
                                        { required: true, message: 'Please enter your state/province' }
                                    ]}
                                >
                                    {states.length > 0 ? (
                                        <Select
                                            placeholder="Select your state/province"
                                            showSearch
                                            filterOption={(input, option) =>
                                                (option?.children as unknown as string)?.toLowerCase().includes(input.toLowerCase())
                                            }
                                            onChange={handleStateChange}
                                        >
                                            {states.map(state => (
                                                <Option key={state.value} value={state.value}>
                                                    {state.label}
                                                </Option>
                                            ))}
                                        </Select>
                                    ) : (
                                        <Input
                                            placeholder="Enter your state or province"
                                            maxLength={50}
                                            onChange={(e) => setPersonalInfo((prev: BasicVerificationData) => ({
                                                ...prev,
                                                address: { ...prev.personalInfo.address, state: e.target.value }
                                            }))}
                                        />
                                    )}
                                </Form.Item>
                            </Col>
                        </Row>

                        <Row gutter={16}>
                            <Col xs={24} sm={12}>
                                <Form.Item
                                    name="zipCode"
                                    label="ZIP/Postal Code"
                                    rules={[
                                        { required: true, message: 'Please enter your ZIP/postal code' },
                                        { min: 3, message: 'ZIP/postal code must be at least 3 characters' }
                                    ]}
                                >
                                    <Input
                                        placeholder="Enter your ZIP or postal code"
                                        maxLength={20}
                                        onChange={(e) => setPersonalInfo((prev: BasicVerificationData) => ({ ...prev, address: { ...prev.personalInfo.address, zipCode: e.target.value } }))}
                                    />
                                </Form.Item>
                            </Col>
                            <Col xs={24} sm={12}>
                                <Form.Item
                                    name="country"
                                    label="Country"
                                    rules={[
                                        { required: true, message: 'Please select your country' }
                                    ]}
                                >
                                    <Select
                                        placeholder="Select your country"
                                        showSearch
                                        filterOption={(input, option) =>
                                            (option?.children as unknown as string)?.toLowerCase().includes(input.toLowerCase())
                                        }
                                        onChange={handleCountryChange}
                                    >
                                        {countries.map(country => (
                                            <Option key={country.value} value={country.value}>
                                                {country.label}
                                            </Option>
                                        ))}
                                    </Select>
                                </Form.Item>
                            </Col>
                        </Row>
                    </div>

                    <Alert
                        message="Data Security"
                        description="Your personal information is encrypted and stored securely. We use this information only for identity verification purposes in compliance with regulatory requirements."
                        type="success"
                        showIcon
                        style={{ marginBottom: 24 }}
                    />

                    <Form.Item>
                        <Space style={{ width: '100%', justifyContent: 'center' }}>
                            <Button
                                type="primary"
                                htmlType="submit"
                                loading={submitting || loading}
                                size="large"
                                icon={<FileTextOutlined />}
                                style={{ minWidth: 200 }}
                            >
                                {submitting || loading ? 'Processing...' : 'Submit'}
                            </Button>
                        </Space>
                    </Form.Item>
                </Form>
            </Card>
        </div>
    );
};

export default BasicVerification;