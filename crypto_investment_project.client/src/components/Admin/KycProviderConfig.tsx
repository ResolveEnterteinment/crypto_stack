// src/components/Admin/KycProviderConfig.tsx
import React, { useState, useEffect } from 'react';
import { Card, Form, Input, Button, Select, Switch, Table, message, Tabs, InputNumber, Space, Divider } from 'antd';
import { CheckCircleOutlined, GlobalOutlined } from '@ant-design/icons';
import { kycProviders } from '../../config/kycProviders';

const { Option } = Select;
const { TabPane } = Tabs;

// Interface for KYC provider settings
interface KycSettings {
    defaultProvider: string;
    enableRoundRobin: boolean;
    providerWeights: {
        [key: string]: number;
    };
}

// Interface for provider configuration
interface ProviderConfig {
    apiUrl: string;
    apiKey?: string;
    appToken?: string;
    secretKey?: string;
    webhookSecret?: string;
    webhookToken?: string;
    allowedReferrers: string[];
    [key: string]: any;
}

// Interface for provider statistics
interface ProviderStats {
    provider: string;
    totalRequests: number;
    successRate: number;
    averageTime: number;
    failureRate: number;
    lastUpdated: string;
}

const KycProviderConfig: React.FC = () => {
    const [settingsForm] = Form.useForm();
    const [onfidoForm] = Form.useForm();
    const [sumsubForm] = Form.useForm();
    const [settings, setSettings] = useState<KycSettings>({
        defaultProvider: 'Onfido',
        enableRoundRobin: false,
        providerWeights: { Onfido: 0.7, SumSub: 0.3 },
    });
    const [onfidoConfig, setOnfidoConfig] = useState<ProviderConfig>({
        apiUrl: 'https://api.onfido.com/v3/',
        apiKey: '',
        webhookToken: '',
        allowedReferrers: ['https://yourapp.com', 'http://localhost:3000'],
    });
    const [sumsubConfig, setSumSubConfig] = useState<ProviderConfig>({
        apiUrl: 'https://api.sumsub.com',
        appToken: '',
        secretKey: '',
        webhookSecret: '',
        allowedReferrers: ['https://yourapp.com', 'http://localhost:3000'],
    });
    const [loadingSettings, setLoadingSettings] = useState<boolean>(true);
    const [savingSettings, setSavingSettings] = useState<boolean>(false);
    const [savingOnfido, setSavingOnfido] = useState<boolean>(false);
    const [savingSumSub, setSavingSumSub] = useState<boolean>(false);
    const [stats, setStats] = useState<ProviderStats[]>([]);
    const [loadingStats, setLoadingStats] = useState<boolean>(false);

    // Fetch all configuration on component mount
    useEffect(() => {
        fetchSettings();
        fetchOnfidoConfig();
        fetchSumSubConfig();
        fetchProviderStats();
    }, []);

    // Mock function to fetch general KYC settings
    const fetchSettings = async () => {
        setLoadingSettings(true);
        try {
            // In a real application, this would be a call to your backend API
            // For now, we'll just use mock data after a delay
            setTimeout(() => {
                const mockSettings: KycSettings = {
                    defaultProvider: 'Onfido',
                    enableRoundRobin: false,
                    providerWeights: { Onfido: 0.7, SumSub: 0.3 },
                };
                setSettings(mockSettings);
                settingsForm.setFieldsValue(mockSettings);
                setLoadingSettings(false);
            }, 1000);
        } catch (error) {
            console.error('Failed to fetch KYC settings:', error);
            message.error('Failed to load KYC settings');
            setLoadingSettings(false);
        }
    };

    // Mock function to fetch Onfido configuration
    const fetchOnfidoConfig = async () => {
        try {
            // In a real application, this would be a call to your backend API
            setTimeout(() => {
                const mockConfig: ProviderConfig = {
                    apiUrl: 'https://api.onfido.com/v3/',
                    apiKey: 'api_live_12345',
                    webhookToken: 'wh_token_12345',
                    allowedReferrers: ['https://yourapp.com', 'http://localhost:3000'],
                };
                setOnfidoConfig(mockConfig);
                onfidoForm.setFieldsValue(mockConfig);
            }, 1000);
        } catch (error) {
            console.error('Failed to fetch Onfido configuration:', error);
            message.error('Failed to load Onfido configuration');
        }
    };

    // Mock function to fetch SumSub configuration
    const fetchSumSubConfig = async () => {
        try {
            // In a real application, this would be a call to your backend API
            setTimeout(() => {
                const mockConfig: ProviderConfig = {
                    apiUrl: 'https://api.sumsub.com',
                    appToken: 'app_token_12345',
                    secretKey: 'secret_key_12345',
                    webhookSecret: 'webhook_secret_12345',
                    allowedReferrers: ['https://yourapp.com', 'http://localhost:3000'],
                };
                setSumSubConfig(mockConfig);
                sumsubForm.setFieldsValue(mockConfig);
            }, 1000);
        } catch (error) {
            console.error('Failed to fetch SumSub configuration:', error);
            message.error('Failed to load SumSub configuration');
        }
    };

    // Mock function to fetch provider statistics
    const fetchProviderStats = async () => {
        setLoadingStats(true);
        try {
            // In a real application, this would be a call to your backend API
            setTimeout(() => {
                const mockStats: ProviderStats[] = [
                    {
                        provider: 'Onfido',
                        totalRequests: 1250,
                        successRate: 97.5,
                        averageTime: 2.8,
                        failureRate: 2.5,
                        lastUpdated: new Date().toISOString(),
                    },
                    {
                        provider: 'SumSub',
                        totalRequests: 850,
                        successRate: 98.2,
                        averageTime: 3.1,
                        failureRate: 1.8,
                        lastUpdated: new Date().toISOString(),
                    },
                ];
                setStats(mockStats);
                setLoadingStats(false);
            }, 1000);
        } catch (error) {
            console.error('Failed to fetch provider statistics:', error);
            message.error('Failed to load provider statistics');
            setLoadingStats(false);
        }
    };

    // Handle saving general KYC settings
    const handleSaveSettings = async (values: KycSettings) => {
        setSavingSettings(true);
        try {
            // In a real application, this would be a call to your backend API
            setTimeout(() => {
                setSettings(values);
                message.success('KYC settings saved successfully');
                setSavingSettings(false);
            }, 1000);
        } catch (error) {
            console.error('Failed to save KYC settings:', error);
            message.error('Failed to save KYC settings');
            setSavingSettings(false);
        }
    };

    // Handle saving Onfido configuration
    const handleSaveOnfido = async (values: ProviderConfig) => {
        setSavingOnfido(true);
        try {
            // In a real application, this would be a call to your backend API
            setTimeout(() => {
                setOnfidoConfig(values);
                message.success('Onfido configuration saved successfully');
                setSavingOnfido(false);
            }, 1000);
        } catch (error) {
            console.error('Failed to save Onfido configuration:', error);
            message.error('Failed to save Onfido configuration');
            setSavingOnfido(false);
        }
    };

    // Handle saving SumSub configuration
    const handleSaveSumSub = async (values: ProviderConfig) => {
        setSavingSumSub(true);
        try {
            // In a real application, this would be a call to your backend API
            setTimeout(() => {
                setSumSubConfig(values);
                message.success('SumSub configuration saved successfully');
                setSavingSumSub(false);
            }, 1000);
        } catch (error) {
            console.error('Failed to save SumSub configuration:', error);
            message.error('Failed to save SumSub configuration');
            setSavingSumSub(false);
        }
    };

    // Test connection to Onfido API
    const testOnfidoConnection = async () => {
        try {
            message.loading('Testing connection to Onfido...', 1.5);
            // In a real application, this would be a call to your backend API
            setTimeout(() => {
                message.success('Successfully connected to Onfido API');
            }, 2000);
        } catch (error) {
            console.error('Failed to connect to Onfido:', error);
            message.error('Failed to connect to Onfido API');
        }
    };

    // Test connection to SumSub API
    const testSumSubConnection = async () => {
        try {
            message.loading('Testing connection to SumSub...', 1.5);
            // In a real application, this would be a call to your backend API
            setTimeout(() => {
                message.success('Successfully connected to SumSub API');
            }, 2000);
        } catch (error) {
            console.error('Failed to connect to SumSub:', error);
            message.error('Failed to connect to SumSub API');
        }
    };

    // Columns for the statistics table
    const columns = [
        {
            title: 'Provider',
            dataIndex: 'provider',
            key: 'provider',
        },
        {
            title: 'Total Requests',
            dataIndex: 'totalRequests',
            key: 'totalRequests',
            sorter: (a: ProviderStats, b: ProviderStats) => a.totalRequests - b.totalRequests,
        },
        {
            title: 'Success Rate',
            dataIndex: 'successRate',
            key: 'successRate',
            render: (rate: number) => `${rate.toFixed(1)}%`,
            sorter: (a: ProviderStats, b: ProviderStats) => a.successRate - b.successRate,
        },
        {
            title: 'Average Processing Time',
            dataIndex: 'averageTime',
            key: 'averageTime',
            render: (time: number) => `${time.toFixed(1)} sec`,
            sorter: (a: ProviderStats, b: ProviderStats) => a.averageTime - b.averageTime,
        },
        {
            title: 'Failure Rate',
            dataIndex: 'failureRate',
            key: 'failureRate',
            render: (rate: number) => `${rate.toFixed(1)}%`,
            sorter: (a: ProviderStats, b: ProviderStats) => a.failureRate - b.failureRate,
        },
        {
            title: 'Last Updated',
            dataIndex: 'lastUpdated',
            key: 'lastUpdated',
            render: (date: string) => new Date(date).toLocaleString(),
        },
    ];

    return (
        <Card title="KYC Provider Configuration" className="mb-5">
            <Tabs defaultActiveKey="1">
                <TabPane tab="General Settings" key="1">
                    <Card title="KYC Service Settings" bordered={false}>
                        <Form
                            form={settingsForm}
                            layout="vertical"
                            onFinish={handleSaveSettings}
                            initialValues={settings}
                            disabled={loadingSettings}
                        >
                            <Form.Item
                                name="defaultProvider"
                                label="Default KYC Provider"
                                tooltip="The provider that will be used by default when no specific provider is requested"
                            >
                                <Select>
                                    {kycProviders.map(provider => (
                                        <Option key={provider.name} value={provider.name}>
                                            {provider.displayName}
                                        </Option>
                                    ))}
                                </Select>
                            </Form.Item>

                            <Form.Item
                                name="enableRoundRobin"
                                label="Enable Round-Robin Load Balancing"
                                tooltip="When enabled, verification requests will be distributed across providers based on the weights below"
                                valuePropName="checked"
                            >
                                <Switch />
                            </Form.Item>

                            <Divider orientation="left">Provider Weights</Divider>
                            <p>Set the weight for each provider when round-robin is enabled (values should sum to 1.0)</p>

                            <Form.Item
                                name={['providerWeights', 'Onfido']}
                                label="Onfido Weight"
                                tooltip="Percentage of verification requests to route to Onfido (between 0 and 1)"
                            >
                                <InputNumber
                                    min={0}
                                    max={1}
                                    step={0.1}
                                    style={{ width: '100%' }}
                                />
                            </Form.Item>

                            <Form.Item
                                name={['providerWeights', 'SumSub']}
                                label="SumSub Weight"
                                tooltip="Percentage of verification requests to route to SumSub (between 0 and 1)"
                            >
                                <InputNumber
                                    min={0}
                                    max={1}
                                    step={0.1}
                                    style={{ width: '100%' }}
                                />
                            </Form.Item>

                            <Form.Item>
                                <Button type="primary" htmlType="submit" loading={savingSettings}>
                                    Save Settings
                                </Button>
                            </Form.Item>
                        </Form>
                    </Card>
                </TabPane>

                <TabPane tab="Provider Statistics" key="2">
                    <Card title="KYC Provider Performance" bordered={false}>
                        <p>
                            Compare performance metrics across KYC providers to help determine optimal configuration.
                        </p>
                        <Table
                            dataSource={stats}
                            columns={columns}
                            rowKey="provider"
                            loading={loadingStats}
                            pagination={false}
                        />
                        <div className="mt-4">
                            <Button type="primary" onClick={fetchProviderStats} loading={loadingStats}>
                                Refresh Statistics
                            </Button>
                        </div>
                    </Card>
                </TabPane>

                <TabPane tab="Onfido Configuration" key="3">
                    <Card
                        title={
                            <div className="flex items-center">
                                <img src="/images/onfido-logo.svg" alt="Onfido" className="h-6 mr-2" />
                                <span>Onfido Configuration</span>
                            </div>
                        }
                        bordered={false}
                    >
                        <Form
                            form={onfidoForm}
                            layout="vertical"
                            onFinish={handleSaveOnfido}
                            initialValues={onfidoConfig}
                        >
                            <Form.Item
                                name="apiUrl"
                                label="Onfido API URL"
                                rules={[{ required: true, message: 'Please enter the Onfido API URL' }]}
                            >
                                <Input addonBefore={<GlobalOutlined />} placeholder="https://api.onfido.com/v3/" />
                            </Form.Item>

                            <Form.Item
                                name="apiKey"
                                label="API Key"
                                rules={[{ required: true, message: 'Please enter your Onfido API key' }]}
                                tooltip="Your Onfido API key (starts with 'api_live_' or 'api_test_')"
                            >
                                <Input.Password placeholder="api_live_xxxxx" />
                            </Form.Item>

                            <Form.Item
                                name="webhookToken"
                                label="Webhook Token"
                                tooltip="Token used to validate webhook requests from Onfido"
                            >
                                <Input.Password placeholder="wh_token_xxxxx" />
                            </Form.Item>

                            <Form.List name="allowedReferrers">
                                {(fields, { add, remove }) => (
                                    <>
                                        <Divider orientation="left">Allowed Referrers</Divider>
                                        <p>Domains allowed to initiate KYC verification (e.g., your application's domains)</p>

                                        {fields.map((field, index) => (
                                            <Form.Item key={field.key} label={index === 0 ? 'Domain' : ''}>
                                                <Space>
                                                    <Form.Item {...field} noStyle>
                                                        <Input placeholder="https://example.com" style={{ width: 300 }} />
                                                    </Form.Item>
                                                    {fields.length > 1 && (
                                                        <Button type="text" danger onClick={() => remove(field.name)}>
                                                            Remove
                                                        </Button>
                                                    )}
                                                </Space>
                                            </Form.Item>
                                        ))}

                                        <Form.Item>
                                            <Button type="dashed" onClick={() => add()} block>
                                                + Add Referrer Domain
                                            </Button>
                                        </Form.Item>
                                    </>
                                )}
                            </Form.List>

                            <Form.Item>
                                <Space>
                                    <Button type="primary" htmlType="submit" loading={savingOnfido}>
                                        Save Configuration
                                    </Button>
                                    <Button onClick={testOnfidoConnection} icon={<CheckCircleOutlined />}>
                                        Test Connection
                                    </Button>
                                </Space>
                            </Form.Item>
                        </Form>
                    </Card>
                </TabPane>

                <TabPane tab="SumSub Configuration" key="4">
                    <Card
                        title={
                            <div className="flex items-center">
                                <img src="/images/sumsub-logo.svg" alt="SumSub" className="h-6 mr-2" />
                                <span>SumSub Configuration</span>
                            </div>
                        }
                        bordered={false}
                    >
                        <Form
                            form={sumsubForm}
                            layout="vertical"
                            onFinish={handleSaveSumSub}
                            initialValues={sumsubConfig}
                        >
                            <Form.Item
                                name="apiUrl"
                                label="SumSub API URL"
                                rules={[{ required: true, message: 'Please enter the SumSub API URL' }]}
                            >
                                <Input addonBefore={<GlobalOutlined />} placeholder="https://api.sumsub.com" />
                            </Form.Item>

                            <Form.Item
                                name="appToken"
                                label="App Token"
                                rules={[{ required: true, message: 'Please enter your SumSub App Token' }]}
                                tooltip="Your SumSub App Token"
                            >
                                <Input.Password placeholder="app_token_xxxxx" />
                            </Form.Item>

                            <Form.Item
                                name="secretKey"
                                label="Secret Key"
                                rules={[{ required: true, message: 'Please enter your SumSub Secret Key' }]}
                                tooltip="Your SumSub Secret Key for signing API requests"
                            >
                                <Input.Password placeholder="secret_key_xxxxx" />
                            </Form.Item>

                            <Form.Item
                                name="webhookSecret"
                                label="Webhook Secret"
                                tooltip="Secret used to validate webhook requests from SumSub"
                            >
                                <Input.Password placeholder="webhook_secret_xxxxx" />
                            </Form.Item>

                            <Form.List name="allowedReferrers">
                                {(fields, { add, remove }) => (
                                    <>
                                        <Divider orientation="left">Allowed Referrers</Divider>
                                        <p>Domains allowed to initiate KYC verification (e.g., your application's domains)</p>

                                        {fields.map((field, index) => (
                                            <Form.Item key={field.key} label={index === 0 ? 'Domain' : ''}>
                                                <Space>
                                                    <Form.Item {...field} noStyle>
                                                        <Input placeholder="https://example.com" style={{ width: 300 }} />
                                                    </Form.Item>
                                                    {fields.length > 1 && (
                                                        <Button type="text" danger onClick={() => remove(field.name)}>
                                                            Remove
                                                        </Button>
                                                    )}
                                                </Space>
                                            </Form.Item>
                                        ))}

                                        <Form.Item>
                                            <Button type="dashed" onClick={() => add()} block>
                                                + Add Referrer Domain
                                            </Button>
                                        </Form.Item>
                                    </>
                                )}
                            </Form.List>

                            <Form.Item>
                                <Space>
                                    <Button type="primary" htmlType="submit" loading={savingSumSub}>
                                        Save Configuration
                                    </Button>
                                    <Button onClick={testSumSubConnection} icon={<CheckCircleOutlined />}>
                                        Test Connection
                                    </Button>
                                </Space>
                            </Form.Item>
                        </Form>
                    </Card>
                </TabPane>

                <TabPane tab="Documentation" key="5">
                    <Card title="KYC Provider Integration Guide" bordered={false}>
                        <div>
                            <h3>Setting Up KYC Providers</h3>
                            <p>Follow these steps to properly configure your KYC providers:</p>

                            <h4>Onfido Integration</h4>
                            <ol>
                                <li>Create an account on the <a href="https://onfido.com" target="_blank" rel="noopener noreferrer">Onfido website</a></li>
                                <li>Generate an API key from the Onfido dashboard</li>
                                <li>Configure webhook endpoints for receiving verification callbacks</li>
                                <li>Add your application's domains to the allowed referrers list</li>
                                <li>Test the integration using Onfido's sandbox environment</li>
                            </ol>

                            <h4>SumSub Integration</h4>
                            <ol>
                                <li>Create an account on the <a href="https://sumsub.com" target="_blank" rel="noopener noreferrer">SumSub website</a></li>
                                <li>Generate App Token and Secret Key from the SumSub dashboard</li>
                                <li>Configure webhook endpoints for receiving verification callbacks</li>
                                <li>Set up KYC levels in the SumSub dashboard matching your application's requirements</li>
                                <li>Add your application's domains to the allowed referrers list</li>
                                <li>Test the integration using SumSub's test environment</li>
                            </ol>

                            <h4>Load Balancing</h4>
                            <p>
                                The round-robin load balancing feature allows you to distribute KYC verification requests
                                across multiple providers based on the weights you define. This can help optimize costs
                                and improve overall verification success rates.
                            </p>

                            <h4>Monitoring</h4>
                            <p>
                                Regularly check the Provider Statistics tab to monitor the performance of each KYC provider.
                                Look for significant differences in success rates, processing time, and failure rates to
                                help inform your provider selection strategy.
                            </p>
                        </div>
                    </Card>
                </TabPane>
            </Tabs>
        </Card>
    );
};

export default KycProviderConfig;