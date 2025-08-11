import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import {
    Layout,
    Typography,
    Button,
    Card,
    Row,
    Col,
    Statistic,
    Steps,
    Rate,
    Carousel,
    Collapse,
    Space,
    Tag,
    Divider,
    Avatar,
    List,
    Progress,
    Timeline,
    Badge,
    FloatButton,
    Anchor,
    ConfigProvider,
    theme
} from 'antd';
import {
    DollarOutlined,
    TrophyOutlined,
    SecurityScanOutlined,
    ThunderboltOutlined,
    SafetyOutlined,
    CheckCircleOutlined,
    ArrowRightOutlined,
    PlayCircleOutlined,
    StarFilled,
    PhoneOutlined,
    MailOutlined,
    EnvironmentOutlined,
    FacebookFilled,
    TwitterOutlined,
    LinkedinFilled,
    InstagramOutlined,
    RocketOutlined,
    CheckOutlined,
    BankOutlined,
    ApiOutlined,
    ClockCircleOutlined,
    TeamOutlined
} from '@ant-design/icons';

// Import enhanced components
import HeroSection from "../components/LandingPage/HeroSection";
import Navbar from '../components/LandingPage/Navbar';

const { Content, Footer } = Layout;
const { Title, Paragraph, Text } = Typography;
const { Panel } = Collapse;

// Custom theme for professional appearance
const customTheme = {
    algorithm: theme.defaultAlgorithm,
    token: {
        colorPrimary: '#1890ff',
        colorSuccess: '#52c41a',
        colorWarning: '#faad14',
        colorError: '#ff4d4f',
        borderRadius: 8,
        fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif',
    },
};

// Animation state
const useCounter = (end: number, duration = 2000) => {
    const [count, setCount] = useState(0);

    useEffect(() => {
        let startTime: number | null = null;
        let animationFrame: number;

        const step = (timestamp: number) => {
            if (!startTime) startTime = timestamp;
            const progress = Math.min((timestamp - startTime) / duration, 1);
            setCount(Math.floor(progress * end));

            if (progress < 1) {
                animationFrame = requestAnimationFrame(step);
            }
        };

        animationFrame = requestAnimationFrame(step);
        return () => cancelAnimationFrame(animationFrame);
    }, [end, duration]);

    return count;
};

const LandingPageContent: React.FC = () => {
    const navigate = useNavigate();
    const [isLoading, setIsLoading] = useState(true);

    // Counters for stats section
    const userCount = useCounter(15000);
    const investedAmount = useCounter(25000000);
    const cryptoCount = useCounter(32);

    useEffect(() => {
        setIsLoading(false);
    }, []);

    if (isLoading) {
        return (
            <div style={{
                minHeight: '100vh',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center'
            }}>
                <div style={{ textAlign: 'center' }}>
                    <Progress
                        type="circle"
                        percent={100}
                        status="active"
                        strokeColor="#1890ff"
                    />
                    <Paragraph style={{ marginTop: 16 }}>Loading...</Paragraph>
                </div>
            </div>
        );
    }

    // Stats data
    const statsData = [
        {
            title: userCount.toLocaleString() + '+',
            value: 'Active Investors',
            icon: <TeamOutlined style={{ color: '#1890ff', fontSize: 32 }} />,
            prefix: '',
            suffix: '',
            color: '#1890ff'
        },
        {
            title: '$' + (investedAmount / 1000000).toFixed(1) + 'M+',
            value: 'Total Invested',
            icon: <DollarOutlined style={{ color: '#52c41a', fontSize: 32 }} />,
            prefix: '',
            suffix: '',
            color: '#52c41a'
        },
        {
            title: cryptoCount + '+',
            value: 'Cryptocurrencies',
            icon: <ApiOutlined style={{ color: '#722ed1', fontSize: 32 }} />,
            prefix: '',
            suffix: '',
            color: '#722ed1'
        },
        {
            title: '99.9%',
            value: 'Uptime Guarantee',
            icon: <SafetyOutlined style={{ color: '#fa8c16', fontSize: 32 }} />,
            prefix: '',
            suffix: '',
            color: '#fa8c16'
        }
    ];

    // Features data
    const featuresData = [
        {
            icon: <ThunderboltOutlined style={{ fontSize: 48, color: '#1890ff' }} />,
            title: 'Automated Investments',
            description: 'Set up recurring investments on your schedule. Daily, weekly, or monthly—you decide.',
            badge: 'Popular'
        },
        {
            icon: <TrophyOutlined style={{ fontSize: 48, color: '#52c41a' }} />,
            title: 'Diversified Portfolio',
            description: 'Spread your investment across multiple cryptocurrencies to balance risk and reward.',
            badge: 'Recommended'
        },
        {
            icon: <CheckOutlined style={{ fontSize: 48, color: '#722ed1' }} />,
            title: 'Bank-Level Security',
            description: 'Your investments are protected with military-grade encryption and strict security protocols.',
            badge: 'Secure'
        },
        {
            icon: <DollarOutlined style={{ fontSize: 48, color: '#13c2c2' }} />,
            title: 'Low Fees',
            description: 'Competitive pricing with transparent fee structure—no hidden costs or surprises.',
            badge: 'Value'
        },
        {
            icon: <SecurityScanOutlined style={{ fontSize: 48, color: '#fa8c16' }} />,
            title: 'Regulated & Compliant',
            description: 'We follow all regulatory requirements to ensure your investments are legitimate and protected.',
            badge: 'Trusted'
        },
        {
            icon: <ClockCircleOutlined style={{ fontSize: 48, color: '#eb2f96' }} />,
            title: 'Instant Processing',
            description: 'Your orders are executed immediately with optimized trade routing for the best prices.',
            badge: 'Fast'
        }
    ];

    // Steps data
    const stepsData = [
        {
            title: 'Create Your Account',
            description: 'Sign up in less than 2 minutes with just your email and basic information.',
            icon: <Avatar size={64} style={{ backgroundColor: '#1890ff' }}>1</Avatar>
        },
        {
            title: 'Set Investment Plan',
            description: 'Choose your investment amount, frequency, and the cryptocurrencies you want to buy.',
            icon: <Avatar size={64} style={{ backgroundColor: '#52c41a' }}>2</Avatar>
        },
        {
            title: 'Watch Portfolio Grow',
            description: 'We\'ll automatically execute your investment plan and you can track performance in real-time.',
            icon: <Avatar size={64} style={{ backgroundColor: '#722ed1' }}>3</Avatar>
        }
    ];

    // Testimonials data
    const testimonialsData = [
        {
            name: 'Michael T.',
            title: 'Software Engineer',
            avatar: 'https://randomuser.me/api/portraits/men/1.jpg',
            rating: 5,
            comment: 'I\'ve tried many platforms, but this one makes investing in crypto truly effortless. The automated buys have consistently built my portfolio even during market dips.'
        },
        {
            name: 'Sarah L.',
            title: 'Marketing Director',
            avatar: 'https://randomuser.me/api/portraits/women/2.jpg',
            rating: 5,
            comment: 'As someone new to crypto, I was intimidated by the complexity. This platform made it simple to start small and grow my investments over time.'
        },
        {
            name: 'David R.',
            title: 'Financial Analyst',
            avatar: 'https://randomuser.me/api/portraits/men/3.jpg',
            rating: 4,
            comment: 'The diversification options are excellent. I\'ve been able to spread my risk across multiple cryptocurrencies while still focusing on the ones I believe in.'
        },
        {
            name: 'Emily K.',
            title: 'Small Business Owner',
            avatar: 'https://randomuser.me/api/portraits/women/4.jpg',
            rating: 5,
            comment: 'Customer support is fantastic. When I had questions about my subscription, they responded quickly and resolved everything in minutes.'
        }
    ];

    // FAQ data
    const faqData = [
        {
            key: '1',
            label: 'How do I get started?',
            children: (
                <Paragraph>
                    Creating an account takes less than 2 minutes. Just click the Get Started button,
                    enter your email, create a password, and you're ready to set up your first investment plan.
                </Paragraph>
            ),
        },
        {
            key: '2',
            label: 'What are the fees?',
            children: (
                <div>
                    <Paragraph>
                        We charge a simple 1% platform fee on each transaction, plus standard payment
                        processing fees (2.9% + $0.30). There are no hidden charges or subscription fees.
                    </Paragraph>
                    <Timeline
                        items={[
                            { children: 'Platform Fee: 1% per transaction' },
                            { children: 'Payment Processing: 2.9% + $0.30' },
                            { children: 'No monthly or annual fees' },
                            { children: 'No withdrawal fees' }
                        ]}
                    />
                </div>
            ),
        },
        {
            key: '3',
            label: 'How secure is my investment?',
            children: (
                <Paragraph>
                    We use bank-level encryption and security measures to protect your data and investments.
                    We never store your credit card information and all transactions are processed through
                    secure, regulated channels.
                </Paragraph>
            ),
        },
        {
            key: '4',
            label: 'Can I withdraw my funds anytime?',
            children: (
                <Paragraph>
                    Yes, you have full control over your portfolio. You can withdraw your crypto or sell
                    it back to fiat currency at any time without penalties.
                </Paragraph>
            ),
        },
        {
            key: '5',
            label: 'Which cryptocurrencies can I invest in?',
            children: (
                <div>
                    <Paragraph>
                        We support over 30 cryptocurrencies including:
                    </Paragraph>
                    <Space wrap>
                        <Tag color="blue">Bitcoin (BTC)</Tag>
                        <Tag color="purple">Ethereum (ETH)</Tag>
                        <Tag color="green">Solana (SOL)</Tag>
                        <Tag color="orange">Cardano (ADA)</Tag>
                        <Tag color="cyan">Polkadot (DOT)</Tag>
                        <Tag color="gold">Chainlink (LINK)</Tag>
                        <Text type="secondary">...and many more</Text>
                    </Space>
                </div>
            ),
        }
    ];

    return (
        <ConfigProvider theme={customTheme}>
            <Layout style={{ minHeight: '100vh', backgroundColor: '#f0f2f5' }}>
                {/* Hero Section */}
                <HeroSection />

                <Content>
                    {/* Stats Section */}
                    <section style={{ padding: '80px 0', backgroundColor: '#fff' }}>
                        <div style={{ maxWidth: 1200, margin: '0 auto', padding: '0 24px' }}>
                            <Row gutter={[32, 32]} justify="center">
                                {statsData.map((stat, index) => (
                                    <Col xs={24} sm={12} lg={6} key={index}>
                                        <Card
                                            hoverable
                                            style={{
                                                textAlign: 'center',
                                                height: '100%',
                                                border: 'none',
                                                boxShadow: '0 4px 12px rgba(0,0,0,0.1)',
                                                borderRadius: 12
                                            }}
                                            bodyStyle={{ padding: '32px 24px' }}
                                        >
                                            <Space direction="vertical" size="middle" style={{ width: '100%' }}>
                                                {stat.icon}
                                                <Statistic
                                                    title={stat.value}
                                                    value={stat.title}
                                                    valueStyle={{
                                                        color: stat.color,
                                                        fontSize: '2.5rem',
                                                        fontWeight: 'bold'
                                                    }}
                                                />
                                            </Space>
                                        </Card>
                                    </Col>
                                ))}
                            </Row>
                        </div>
                    </section>

                    {/* Features Section */}
                    <section style={{ padding: '80px 0', backgroundColor: '#f0f2f5' }}>
                        <div style={{ maxWidth: 1200, margin: '0 auto', padding: '0 24px' }}>
                            <div style={{ textAlign: 'center', marginBottom: 64 }}>
                                <Title level={1} style={{ color: '#262626', marginBottom: 16 }}>
                                    Why Choose Our Platform
                                </Title>
                                <Paragraph
                                    style={{
                                        fontSize: '1.2rem',
                                        color: '#595959',
                                        maxWidth: 600,
                                        margin: '0 auto'
                                    }}
                                >
                                    Investing in crypto shouldn't be complicated. Our platform makes it
                                    simple, secure, and effective.
                                </Paragraph>
                            </div>

                            <Row gutter={[32, 32]}>
                                {featuresData.map((feature, index) => (
                                    <Col xs={24} md={12} lg={8} key={index}>
                                        <Badge.Ribbon text={feature.badge} color="blue">
                                            <Card
                                                hoverable
                                                style={{
                                                    height: '100%',
                                                    borderRadius: 12,
                                                    border: 'none',
                                                    boxShadow: '0 4px 12px rgba(0,0,0,0.08)'
                                                }}
                                                bodyStyle={{ padding: '32px 24px' }}
                                            >
                                                <Space direction="vertical" size="large" style={{ width: '100%' }}>
                                                    <div style={{ textAlign: 'center' }}>
                                                        {feature.icon}
                                                    </div>
                                                    <Title level={3} style={{ textAlign: 'center', marginBottom: 16 }}>
                                                        {feature.title}
                                                    </Title>
                                                    <Paragraph style={{ textAlign: 'center', color: '#595959' }}>
                                                        {feature.description}
                                                    </Paragraph>
                                                </Space>
                                            </Card>
                                        </Badge.Ribbon>
                                    </Col>
                                ))}
                            </Row>
                        </div>
                    </section>

                    {/* How It Works Section */}
                    <section style={{ padding: '80px 0', backgroundColor: '#fff' }}>
                        <div style={{ maxWidth: 1200, margin: '0 auto', padding: '0 24px' }}>
                            <div style={{ textAlign: 'center', marginBottom: 64 }}>
                                <Title level={1} style={{ color: '#262626', marginBottom: 16 }}>
                                    How It Works
                                </Title>
                                <Paragraph
                                    style={{
                                        fontSize: '1.2rem',
                                        color: '#595959',
                                        maxWidth: 600,
                                        margin: '0 auto'
                                    }}
                                >
                                    Get started in three simple steps
                                </Paragraph>
                            </div>

                            <Row gutter={[48, 48]} align="middle">
                                {stepsData.map((step, index) => (
                                    <Col xs={24} lg={8} key={index}>
                                        <Card
                                            style={{
                                                textAlign: 'center',
                                                height: '100%',
                                                borderRadius: 12,
                                                border: 'none',
                                                boxShadow: '0 4px 12px rgba(0,0,0,0.08)'
                                            }}
                                            bodyStyle={{ padding: '40px 24px' }}
                                        >
                                            <Space direction="vertical" size="large" style={{ width: '100%' }}>
                                                {step.icon}
                                                <Title level={2} style={{ marginBottom: 16 }}>
                                                    {step.title}
                                                </Title>
                                                <Paragraph style={{ color: '#595959', fontSize: '1rem' }}>
                                                    {step.description}
                                                </Paragraph>
                                            </Space>
                                        </Card>
                                    </Col>
                                ))}
                            </Row>

                            <div style={{ textAlign: 'center', marginTop: 48 }}>
                                <Button
                                    type="primary"
                                    size="large"
                                    icon={<RocketOutlined />}
                                    onClick={() => navigate('/auth/register')}
                                    style={{
                                        height: 48,
                                        borderRadius: 24,
                                        paddingLeft: 32,
                                        paddingRight: 32,
                                        fontSize: '1.1rem'
                                    }}
                                >
                                    Start Investing Today
                                </Button>
                            </div>
                        </div>
                    </section>

                    {/* Testimonials Section */}
                    <section style={{ padding: '80px 0', backgroundColor: '#f0f2f5' }}>
                        <div style={{ maxWidth: 1200, margin: '0 auto', padding: '0 24px' }}>
                            <div style={{ textAlign: 'center', marginBottom: 64 }}>
                                <Title level={1} style={{ color: '#262626', marginBottom: 16 }}>
                                    What Our Users Say
                                </Title>
                                <Paragraph
                                    style={{
                                        fontSize: '1.2rem',
                                        color: '#595959',
                                        maxWidth: 600,
                                        margin: '0 auto'
                                    }}
                                >
                                    Join thousands of satisfied investors already growing their crypto portfolios
                                </Paragraph>
                            </div>

                            <Carousel autoplay autoplaySpeed={4000} dots={{ className: 'custom-dots' }}>
                                {testimonialsData.map((testimonial, index) => (
                                    <div key={index}>
                                        <Card
                                            style={{
                                                maxWidth: 800,
                                                margin: '0 auto',
                                                borderRadius: 12,
                                                border: 'none',
                                                boxShadow: '0 8px 24px rgba(0,0,0,0.1)'
                                            }}
                                            bodyStyle={{ padding: '48px 32px' }}
                                        >
                                            <div style={{ textAlign: 'center' }}>
                                                <Avatar
                                                    size={80}
                                                    src={testimonial.avatar}
                                                    style={{ marginBottom: 24 }}
                                                />
                                                <Rate disabled defaultValue={testimonial.rating} style={{ marginBottom: 24 }} />
                                                <Paragraph
                                                    style={{
                                                        fontSize: '1.1rem',
                                                        fontStyle: 'italic',
                                                        color: '#595959',
                                                        marginBottom: 24,
                                                        lineHeight: 1.6
                                                    }}
                                                >
                                                    "{testimonial.comment}"
                                                </Paragraph>
                                                <Title level={4} style={{ marginBottom: 4 }}>
                                                    {testimonial.name}
                                                </Title>
                                                <Text type="secondary">{testimonial.title}</Text>
                                            </div>
                                        </Card>
                                    </div>
                                ))}
                            </Carousel>
                        </div>
                    </section>

                    {/* FAQ Section */}
                    <section style={{ padding: '80px 0', backgroundColor: '#fff' }}>
                        <div style={{ maxWidth: 1000, margin: '0 auto', padding: '0 24px' }}>
                            <div style={{ textAlign: 'center', marginBottom: 64 }}>
                                <Title level={1} style={{ color: '#262626', marginBottom: 16 }}>
                                    Frequently Asked Questions
                                </Title>
                                <Paragraph
                                    style={{
                                        fontSize: '1.2rem',
                                        color: '#595959',
                                        maxWidth: 600,
                                        margin: '0 auto'
                                    }}
                                >
                                    Got questions? We've got answers.
                                </Paragraph>
                            </div>

                            <Collapse
                                items={faqData}
                                size="large"
                                expandIconPosition="end"
                                style={{
                                    backgroundColor: 'transparent',
                                    border: 'none'
                                }}
                            />

                            <div style={{ textAlign: 'center', marginTop: 48 }}>
                                <Title level={4} style={{ marginBottom: 24 }}>
                                    Still have questions?
                                </Title>
                                <Button
                                    type="primary"
                                    size="large"
                                    icon={<MailOutlined />}
                                    onClick={() => navigate('/contact')}
                                    style={{
                                        height: 48,
                                        borderRadius: 24,
                                        paddingLeft: 32,
                                        paddingRight: 32
                                    }}
                                >
                                    Contact Support
                                </Button>
                            </div>
                        </div>
                    </section>

                    {/* CTA Section */}
                    <section style={{
                        padding: '100px 0',
                        background: 'linear-gradient(135deg, #1890ff 0%, #722ed1 100%)',
                        color: '#fff'
                    }}>
                        <div style={{ maxWidth: 1000, margin: '0 auto', padding: '0 24px', textAlign: 'center' }}>
                            <Title level={1} style={{ color: '#fff', marginBottom: 24 }}>
                                Ready to Start Your Crypto Journey?
                            </Title>
                            <Paragraph
                                style={{
                                    fontSize: '1.3rem',
                                    color: 'rgba(255,255,255,0.9)',
                                    marginBottom: 48,
                                    maxWidth: 600,
                                    margin: '0 auto 48px auto'
                                }}
                            >
                                Join thousands of investors who are already building their crypto portfolio the smart way.
                            </Paragraph>

                            <Space size="large" wrap>
                                <Button
                                    type="primary"
                                    size="large"
                                    icon={<ArrowRightOutlined />}
                                    onClick={() => navigate('/auth/register')}
                                    style={{
                                        height: 56,
                                        borderRadius: 28,
                                        paddingLeft: 40,
                                        paddingRight: 40,
                                        fontSize: '1.2rem',
                                        backgroundColor: '#fff',
                                        borderColor: '#fff',
                                        color: '#1890ff'
                                    }}
                                >
                                    Create Free Account
                                </Button>
                                <Button
                                    type="default"
                                    size="large"
                                    icon={<PlayCircleOutlined />}
                                    onClick={() => navigate('/pricing')}
                                    style={{
                                        height: 56,
                                        borderRadius: 28,
                                        paddingLeft: 40,
                                        paddingRight: 40,
                                        fontSize: '1.2rem',
                                        backgroundColor: 'transparent',
                                        borderColor: '#fff',
                                        color: '#fff'
                                    }}
                                >
                                    View Demo
                                </Button>
                            </Space>

                            <div style={{ marginTop: 48 }}>
                                <Row gutter={[32, 16]} justify="center">
                                    <Col><CheckCircleOutlined /> No Credit Card Required</Col>
                                    <Col><CheckCircleOutlined /> Start with $10</Col>
                                    <Col><CheckCircleOutlined /> Cancel Anytime</Col>
                                </Row>
                            </div>
                        </div>
                    </section>
                </Content>

                {/* Footer */}
                <Footer style={{ backgroundColor: '#001529', color: '#fff', padding: '60px 0' }}>
                    <div style={{ maxWidth: 1200, margin: '0 auto', padding: '0 24px' }}>
                        <Row gutter={[48, 32]}>
                            <Col xs={24} sm={12} lg={6}>
                                <Space direction="vertical" size="large">
                                    <div>
                                        <Title level={3} style={{ color: '#fff', marginBottom: 16 }}>
                                            CryptoInvest
                                        </Title>
                                        <Paragraph style={{ color: 'rgba(255,255,255,0.7)' }}>
                                            Automated crypto investing for everyone. Build your portfolio
                                            with confidence.
                                        </Paragraph>
                                    </div>
                                    <Space size="middle">
                                        <Button type="text" shape="circle" icon={<FacebookFilled />} style={{ color: '#fff' }} />
                                        <Button type="text" shape="circle" icon={<TwitterOutlined />} style={{ color: '#fff' }} />
                                        <Button type="text" shape="circle" icon={<LinkedinFilled />} style={{ color: '#fff' }} />
                                        <Button type="text" shape="circle" icon={<InstagramOutlined />} style={{ color: '#fff' }} />
                                    </Space>
                                </Space>
                            </Col>

                            <Col xs={24} sm={12} lg={6}>
                                <Title level={4} style={{ color: '#fff', marginBottom: 24 }}>
                                    Quick Links
                                </Title>
                                <List
                                    dataSource={[
                                        { title: 'Features', link: '/features' },
                                        { title: 'Pricing', link: '/pricing' },
                                        { title: 'Learn', link: '/learn' },
                                        { title: 'Blog', link: '/blog' }
                                    ]}
                                    renderItem={item => (
                                        <List.Item style={{ border: 'none', padding: '8px 0' }}>
                                            <Button
                                                type="text"
                                                onClick={() => navigate(item.link)}
                                                style={{ color: 'rgba(255,255,255,0.7)', padding: 0 }}
                                            >
                                                {item.title}
                                            </Button>
                                        </List.Item>
                                    )}
                                />
                            </Col>

                            <Col xs={24} sm={12} lg={6}>
                                <Title level={4} style={{ color: '#fff', marginBottom: 24 }}>
                                    Legal
                                </Title>
                                <List
                                    dataSource={[
                                        { title: 'Terms of Service', link: '/terms' },
                                        { title: 'Privacy Policy', link: '/privacy' },
                                        { title: 'Security', link: '/security' },
                                        { title: 'Compliance', link: '/compliance' }
                                    ]}
                                    renderItem={item => (
                                        <List.Item style={{ border: 'none', padding: '8px 0' }}>
                                            <Button
                                                type="text"
                                                onClick={() => navigate(item.link)}
                                                style={{ color: 'rgba(255,255,255,0.7)', padding: 0 }}
                                            >
                                                {item.title}
                                            </Button>
                                        </List.Item>
                                    )}
                                />
                            </Col>

                            <Col xs={24} sm={12} lg={6}>
                                <Title level={4} style={{ color: '#fff', marginBottom: 24 }}>
                                    Contact
                                </Title>
                                <List
                                    dataSource={[
                                        { icon: <MailOutlined />, text: 'support@cryptoinvest.example' },
                                        { icon: <PhoneOutlined />, text: '+1 (555) 123-4567' },
                                        { icon: <EnvironmentOutlined />, text: '123 Crypto Street, San Francisco, CA 94105' }
                                    ]}
                                    renderItem={item => (
                                        <List.Item style={{ border: 'none', padding: '8px 0' }}>
                                            <Space>
                                                <span style={{ color: '#1890ff' }}>{item.icon}</span>
                                                <Text style={{ color: 'rgba(255,255,255,0.7)' }}>
                                                    {item.text}
                                                </Text>
                                            </Space>
                                        </List.Item>
                                    )}
                                />
                            </Col>
                        </Row>

                        <Divider style={{ borderColor: 'rgba(255,255,255,0.2)', marginTop: 48, marginBottom: 24 }} />

                        <Row justify="space-between" align="middle">
                            <Col>
                                <Text style={{ color: 'rgba(255,255,255,0.5)' }}>
                                    © 2024 CryptoInvest. All rights reserved.
                                </Text>
                            </Col>
                            <Col>
                                <Space>
                                    <BankOutlined style={{ color: 'rgba(255,255,255,0.5)' }} />
                                    <Text style={{ color: 'rgba(255,255,255,0.5)' }}>
                                        Regulated & Insured
                                    </Text>
                                </Space>
                            </Col>
                        </Row>
                    </div>
                </Footer>

                {/* Float Action Button */}
                <FloatButton.Group shape="circle" style={{ right: 24 }}>
                    <FloatButton
                        icon={<PhoneOutlined />}
                        tooltip="Contact Support"
                        onClick={() => navigate('/contact')}
                    />
                    <FloatButton
                        icon={<ArrowRightOutlined />}
                        type="primary"
                        tooltip="Get Started"
                        onClick={() => navigate('/auth/register')}
                    />
                </FloatButton.Group>
            </Layout>

            {/* Custom Styles */}
            <style dangerouslySetInnerHTML={{
                __html: `
                    .custom-dots .slick-dots li button {
                        background: rgba(24, 144, 255, 0.3);
                    }
                    .custom-dots .slick-dots li.slick-active button {
                        background: #1890ff;
                    }
                    .ant-carousel .slick-slide {
                        text-align: center;
                        height: auto;
                        padding: 0 16px;
                    }
                    .ant-float-btn-group .ant-float-btn {
                        box-shadow: 0 6px 16px rgba(0, 0, 0, 0.12);
                    }
                    .ant-card:hover {
                        transform: translateY(-4px);
                        transition: all 0.3s ease;
                    }
                `
            }} />
        </ConfigProvider>
    );
};

const LandingPage: React.FC = () => {
    return (
        <>
            <Navbar />
            <LandingPageContent />
        </>
    );
};

export default LandingPage;