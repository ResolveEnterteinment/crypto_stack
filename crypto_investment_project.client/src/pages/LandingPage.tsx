// LandingPage.tsx - Apple-Inspired Elegant Design
import {
    ArrowRightOutlined,
    CheckCircleOutlined,
    DollarOutlined,
    LineChartOutlined,
    LockOutlined,
    SafetyOutlined,
    TeamOutlined,
    ThunderboltOutlined
} from '@ant-design/icons';
import {
    Avatar,
    Button,
    Col,
    Divider,
    Grid,
    Layout,
    Row,
    Space,
    Statistic,
    Timeline,
    Typography
} from 'antd';
import React, { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import '../styles/LandingPage/LandingPage.css';
import Navbar from '../components/LandingPage/Navbar';

const { Content, Footer } = Layout;
const { Title, Text, Paragraph } = Typography;
const { useBreakpoint } = Grid;

const LandingPage: React.FC = () => {
    const navigate = useNavigate();
    const screens = useBreakpoint();
    const [scrolled, setScrolled] = useState(false);

    // Handle scroll effect for navbar
    useEffect(() => {
        const handleScroll = () => {
            setScrolled(window.scrollY > 50);
        };
        window.addEventListener('scroll', handleScroll);
        return () => window.removeEventListener('scroll', handleScroll);
    }, []);

    // Smooth scroll to section
    const scrollToSection = (id: string) => {
        const element = document.getElementById(id);
        if (element) {
            element.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }
    };

    return (
        <Layout className="landing-page">
            {/* Navigation */}
            <Navbar transparent={!scrolled} />

            <Content>
                {/* Hero Section - Apple-style minimalism */}
                <section className="hero-section">
                    <div className="hero-content">
                        <div className="hero-text">
                            <Title level={1} className="hero-title">
                                Stop timing the market.
                                <br />
                                <span className="gradient-text">Start building wealth.</span>
                            </Title>
                            <Paragraph className="hero-description">
                                The easiest way to build a diversified crypto portfolio automatically,
                                with professional-grade tools and transparent pricing.
                            </Paragraph>
                            <Space size="large" className="hero-actions">
                                <Button
                                    type="primary"
                                    size="large"
                                    className="cta-button"
                                    onClick={() => navigate('/signup')}
                                    icon={<ArrowRightOutlined />}
                                    iconPosition="end"
                                >
                                    Get Started Free
                                </Button>
                                <Button
                                    size="large"
                                    className="secondary-button"
                                    onClick={() => scrollToSection('how-it-works')}
                                >
                                    See How It Works
                                </Button>
                            </Space>
                            <Text className="hero-note">
                                <CheckCircleOutlined /> First month free · No credit card required
                            </Text>
                        </div>

                        {/* Hero Visual - Floating Dashboard Preview */}
                        <div className="hero-visual">
                            <div className="dashboard-preview">
                                <div className="preview-header">
                                    <div className="preview-dots">
                                        <span className="dot red"></span>
                                        <span className="dot yellow"></span>
                                        <span className="dot green"></span>
                                    </div>
                                    <Text className="preview-title">Your Portfolio</Text>
                                </div>
                                <div className="preview-content">
                                    <Row gutter={16} style={{ marginBottom: 24 }}>
                                        <Col span={12}>
                                            <div className="stat-card">
                                                <Text className="stat-label">Total Invested</Text>
                                                <Title level={3} className="stat-value">$12,450</Title>
                                            </div>
                                        </Col>
                                        <Col span={12}>
                                            <div className="stat-card success">
                                                <Text className="stat-label">Current Value</Text>
                                                <Title level={3} className="stat-value">$13,892</Title>
                                            </div>
                                        </Col>
                                    </Row>
                                    <div className="chart-placeholder">
                                        <div className="chart-line"></div>
                                        <Text className="chart-label">+11.6% growth</Text>
                                    </div>
                                </div>
                            </div>
                        </div>
                    </div>
                </section>

                {/* Trust Indicators - Subtle but powerful */}
                <section className="trust-section">
                    <div className="container-narrow">
                        <Row gutter={[48, 24]} justify="center" align="middle">
                            <Col xs={12} md={6} className="trust-stat">
                                <Statistic
                                    value={99.9}
                                    suffix="%"
                                    valueStyle={{ fontSize: '32px', fontWeight: 600 }}
                                />
                                <Text className="trust-label">Uptime</Text>
                            </Col>
                            <Col xs={12} md={6} className="trust-stat">
                                <Statistic
                                    value={256}
                                    suffix="-bit"
                                    valueStyle={{ fontSize: '32px', fontWeight: 600 }}
                                />
                                <Text className="trust-label">Encryption</Text>
                            </Col>
                            <Col xs={12} md={6} className="trust-stat">
                                <Statistic
                                    prefix="$"
                                    value={0}
                                    valueStyle={{ fontSize: '32px', fontWeight: 600 }}
                                />
                                <Text className="trust-label">Hidden Fees</Text>
                            </Col>
                            <Col xs={12} md={6} className="trust-stat">
                                <Statistic
                                    value={24}
                                    suffix="/7"
                                    valueStyle={{ fontSize: '32px', fontWeight: 600 }}
                                />
                                <Text className="trust-label">Support</Text>
                            </Col>
                        </Row>
                    </div>
                </section>

                {/* Value Proposition - 3 Core Benefits */}
                <section className="benefits-section" id="benefits">
                    <div className="container">
                        <div className="section-header">
                            <Title level={2} className="section-title">
                                Built for serious investors.
                                <br />
                                <span className="text-secondary">Designed for everyone.</span>
                            </Title>
                        </div>

                        <Row gutter={[48, 48]} className="benefits-grid">
                            <Col xs={24} md={8}>
                                <div className="benefit-card">
                                    <div className="benefit-icon">
                                        <LineChartOutlined />
                                    </div>
                                    <Title level={3} className="benefit-title">
                                        Smart Allocation
                                    </Title>
                                    <Paragraph className="benefit-description">
                                        Set your portfolio allocation once. We automatically rebalance
                                        and execute purchases according to your strategy. Not just
                                        recurring buys—intelligent portfolio management.
                                    </Paragraph>
                                    <div className="benefit-highlight">
                                        <CheckCircleOutlined /> Portfolio-level control
                                        <br />
                                        <CheckCircleOutlined /> Automatic rebalancing
                                    </div>
                                </div>
                            </Col>

                            <Col xs={24} md={8}>
                                <div className="benefit-card">
                                    <div className="benefit-icon">
                                        <ThunderboltOutlined />
                                    </div>
                                    <Title level={3} className="benefit-title">
                                        True Automation
                                    </Title>
                                    <Paragraph className="benefit-description">
                                        Your crypto autopilot. Set it once, never think about it again.
                                        Our enterprise-grade flow engine handles edge cases, retries
                                        failed transactions, and ensures zero missed purchases.
                                    </Paragraph>
                                    <div className="benefit-highlight">
                                        <CheckCircleOutlined /> Never miss a purchase
                                        <br />
                                        <CheckCircleOutlined /> Works with any exchange
                                    </div>
                                </div>
                            </Col>

                            <Col xs={24} md={8}>
                                <div className="benefit-card">
                                    <div className="benefit-icon">
                                        <SafetyOutlined />
                                    </div>
                                    <Title level={3} className="benefit-title">
                                        Total Transparency
                                    </Title>
                                    <Paragraph className="benefit-description">
                                        Real-time dashboard shows exactly where your money goes.
                                        Track every transaction, monitor performance, see all fees.
                                        No surprises, no hidden costs, no duplicate charges.
                                    </Paragraph>
                                    <div className="benefit-highlight">
                                        <CheckCircleOutlined /> Real-time tracking
                                        <br />
                                        <CheckCircleOutlined /> Complete transparency
                                    </div>
                                </div>
                            </Col>
                        </Row>
                    </div>
                </section>

                {/* How It Works - Simple 3-step process */}
                <section className="how-it-works-section" id="how-it-works">
                    <div className="container-narrow">
                        <div className="section-header">
                            <Title level={2} className="section-title">
                                Get started in minutes.
                            </Title>
                            <Paragraph className="section-description">
                                No complex forms. No confusing interfaces. Just simple, elegant automation.
                            </Paragraph>
                        </div>

                        <div className="steps-container">
                            <div className="step-item">
                                <div className="step-number">1</div>
                                <div className="step-content">
                                    <Title level={4}>Create Your Account</Title>
                                    <Paragraph>
                                        Sign up with your email. Quick identity verification for security.
                                        Takes less than 2 minutes.
                                    </Paragraph>
                                </div>
                            </div>

                            <div className="step-divider"></div>

                            <div className="step-item">
                                <div className="step-number">2</div>
                                <div className="step-content">
                                    <Title level={4}>Set Your Strategy</Title>
                                    <Paragraph>
                                        Choose investment amount, frequency, and asset allocation.
                                        Customize your portfolio or use our smart defaults.
                                    </Paragraph>
                                </div>
                            </div>

                            <div className="step-divider"></div>

                            <div className="step-item">
                                <div className="step-number">3</div>
                                <div className="step-content">
                                    <Title level={4}>Watch It Grow</Title>
                                    <Paragraph>
                                        We handle the rest. Track performance in your dashboard.
                                        Adjust anytime. Cancel anytime. Full control.
                                    </Paragraph>
                                </div>
                            </div>
                        </div>

                        <div style={{ textAlign: 'center', marginTop: 64 }}>
                            <Button
                                type="primary"
                                size="large"
                                className="cta-button"
                                onClick={() => navigate('/signup')}
                                icon={<ArrowRightOutlined />}
                                iconPosition="end"
                            >
                                Start Your First Investment
                            </Button>
                        </div>
                    </div>
                </section>

                {/* Why Choose Us - Competitive Advantages */}
                <section className="why-choose-section">
                    <div className="container">
                        <div className="section-header">
                            <Title level={2} className="section-title">
                                Why choose us?
                            </Title>
                        </div>

                        <Row gutter={[32, 32]}>
                            <Col xs={24} md={12}>
                                <div className="feature-item">
                                    <DollarOutlined className="feature-icon" />
                                    <div className="feature-content">
                                        <Title level={4}>Fair, Transparent Pricing</Title>
                                        <Paragraph>
                                            Just 1% platform fee. No hidden charges. No monthly subscriptions.
                                            You see exactly what you pay before every transaction.
                                        </Paragraph>
                                    </div>
                                </div>
                            </Col>

                            <Col xs={24} md={12}>
                                <div className="feature-item">
                                    <LockOutlined className="feature-icon" />
                                    <div className="feature-content">
                                        <Title level={4}>Bank-Level Security</Title>
                                        <Paragraph>
                                            256-bit encryption. Secure KYC verification. Never store your
                                            credit card. Compliant with all financial regulations.
                                        </Paragraph>
                                    </div>
                                </div>
                            </Col>

                            <Col xs={24} md={12}>
                                <div className="feature-item">
                                    <ThunderboltOutlined className="feature-icon" />
                                    <div className="feature-content">
                                        <Title level={4}>Enterprise-Grade Reliability</Title>
                                        <Paragraph>
                                            99.9% uptime. Automatic retry on failures. Idempotency
                                            protection prevents duplicate charges. Never miss a purchase.
                                        </Paragraph>
                                    </div>
                                </div>
                            </Col>

                            <Col xs={24} md={12}>
                                <div className="feature-item">
                                    <TeamOutlined className="feature-icon" />
                                    <div className="feature-content">
                                        <Title level={4}>Dedicated Support</Title>
                                        <Paragraph>
                                            Real humans, real fast. Email support within 2 hours.
                                            Comprehensive knowledge base. Active community forum.
                                        </Paragraph>
                                    </div>
                                </div>
                            </Col>
                        </Row>
                    </div>
                </section>

                {/* Social Proof - Simple testimonial */}
                <section className="testimonial-section">
                    <div className="container-narrow">
                        <div className="testimonial-card">
                            <div className="quote-mark">"</div>
                            <Paragraph className="testimonial-text">
                                I've tried every DCA platform. This one actually gets it right.
                                The portfolio allocation feature is genius—I can rebalance
                                my entire strategy in seconds. And the dashboard is beautiful.
                            </Paragraph>
                            <div className="testimonial-author">
                                <Avatar size={56} style={{ backgroundColor: '#1890ff' }}>
                                    MK
                                </Avatar>
                                <div>
                                    <Text strong className="author-name">Michael K.</Text>
                                    <br />
                                    <Text className="author-title">Software Engineer, Early Adopter</Text>
                                </div>
                            </div>
                        </div>
                    </div>
                </section>

                {/* Pricing - Crystal clear */}
                <section className="pricing-section" id="pricing">
                    <div className="container-narrow">
                        <div className="section-header">
                            <Title level={2} className="section-title">
                                Simple, transparent pricing.
                            </Title>
                            <Paragraph className="section-description">
                                No hidden fees. No surprises. Just honest pricing.
                            </Paragraph>
                        </div>

                        <div className="pricing-card">
                            <div className="pricing-header">
                                <Title level={1} className="pricing-rate">1%</Title>
                                <Text className="pricing-label">platform fee per transaction</Text>
                            </div>

                            <Divider />

                            <div className="pricing-breakdown">
                                <Timeline
                                    items={[
                                        {
                                            dot: <CheckCircleOutlined style={{ fontSize: 16 }} />,
                                            children: (
                                                <>
                                                    <Text strong>Platform Fee: 1%</Text>
                                                    <br />
                                                    <Text type="secondary">Applied to each investment</Text>
                                                </>
                                            )
                                        },
                                        {
                                            dot: <CheckCircleOutlined style={{ fontSize: 16 }} />,
                                            children: (
                                                <>
                                                    <Text strong>Payment Processing: 2.9% + $0.30</Text>
                                                    <br />
                                                    <Text type="secondary">Standard Stripe fees (pass-through)</Text>
                                                </>
                                            )
                                        },
                                        {
                                            dot: <CheckCircleOutlined style={{ fontSize: 16 }} />,
                                            children: (
                                                <>
                                                    <Text strong>Everything Else: $0</Text>
                                                    <br />
                                                    <Text type="secondary">No monthly fees · No withdrawal fees</Text>
                                                </>
                                            )
                                        }
                                    ]}
                                />
                            </div>

                            <div className="pricing-example">
                                <Text className="example-label">Example: $100 investment</Text>
                                <div className="example-breakdown">
                                    <div className="breakdown-row">
                                        <Text>Your investment</Text>
                                        <Text strong>$100.00</Text>
                                    </div>
                                    <div className="breakdown-row">
                                        <Text type="secondary">Platform fee (1%)</Text>
                                        <Text type="secondary">-$1.00</Text>
                                    </div>
                                    <div className="breakdown-row">
                                        <Text type="secondary">Payment processing</Text>
                                        <Text type="secondary">-$3.20</Text>
                                    </div>
                                    <Divider style={{ margin: '8px 0' }} />
                                    <div className="breakdown-row">
                                        <Text strong>Net invested in crypto</Text>
                                        <Text strong style={{ color: '#52c41a', fontSize: '18px' }}>
                                            $95.80
                                        </Text>
                                    </div>
                                </div>
                            </div>

                            <Button
                                type="primary"
                                size="large"
                                block
                                className="pricing-cta"
                                onClick={() => navigate('/signup')}
                            >
                                Start Investing Now
                            </Button>
                        </div>
                    </div>
                </section>

                {/* FAQ - Education */}
                <section className="faq-section" id="faq">
                    <div className="container-narrow">
                        <div className="section-header">
                            <Title level={2} className="section-title">
                                Common questions.
                            </Title>
                        </div>

                        <div className="faq-list">
                            <div className="faq-item">
                                <Title level={4} className="faq-question">
                                    What is Dollar-Cost Averaging (DCA)?
                                </Title>
                                <Paragraph className="faq-answer">
                                    DCA is an investment strategy where you invest a fixed amount
                                    at regular intervals, regardless of price. This removes emotion
                                    from investing and reduces the impact of volatility. Instead of
                                    trying to time the market (which experts struggle with), you build
                                    your position steadily over time.
                                </Paragraph>
                            </div>

                            <div className="faq-item">
                                <Title level={4} className="faq-question">
                                    How is this different from Coinbase recurring buys?
                                </Title>
                                <Paragraph className="faq-answer">
                                    Coinbase offers simple recurring purchases. We offer portfolio-level
                                    allocation and automatic rebalancing. You set percentage allocations
                                    (e.g., 50% BTC, 30% ETH, 20% SOL), and we maintain that balance with
                                    each purchase. Plus, we work with any exchange, not just Coinbase.
                                </Paragraph>
                            </div>

                            <div className="faq-item">
                                <Title level={4} className="faq-question">
                                    Is my money safe?
                                </Title>
                                <Paragraph className="faq-answer">
                                    Yes. We use bank-level 256-bit encryption, never store your credit
                                    card information, and follow all financial regulations. Your crypto
                                    is purchased on regulated exchanges and can be withdrawn anytime.
                                    We're fully KYC-compliant and transparent about all operations.
                                </Paragraph>
                            </div>

                            <div className="faq-item">
                                <Title level={4} className="faq-question">
                                    Can I cancel anytime?
                                </Title>
                                <Paragraph className="faq-answer">
                                    Absolutely. You have full control. Pause your subscription, change
                                    your allocation, adjust your investment amount, or cancel completely
                                    —all with one click. No penalties, no fees, no questions asked.
                                </Paragraph>
                            </div>

                            <div className="faq-item">
                                <Title level={4} className="faq-question">
                                    What's the minimum investment?
                                </Title>
                                <Paragraph className="faq-answer">
                                    Just $50. We want investing to be accessible to everyone. Start small,
                                    grow as you're comfortable. Increase or decrease anytime.
                                </Paragraph>
                            </div>
                        </div>
                    </div>
                </section>

                {/* Final CTA - Strong, clear */}
                <section className="final-cta-section">
                    <div className="container-narrow">
                        <div className="final-cta-content">
                            <Title level={2} className="final-cta-title">
                                Start building your crypto portfolio today.
                            </Title>
                            <Paragraph className="final-cta-description">
                                Join investors who've discovered the easiest way to DCA into crypto.
                            </Paragraph>
                            <Button
                                type="primary"
                                size="large"
                                className="cta-button"
                                onClick={() => navigate('/signup')}
                                icon={<ArrowRightOutlined />}
                                iconPosition="end"
                            >
                                Get Started Free
                            </Button>
                            <Text className="final-cta-note">
                                First month free · No credit card required · Cancel anytime
                            </Text>
                        </div>
                    </div>
                </section>
            </Content>

            {/* Footer - Clean, minimal */}
            <Footer className="landing-footer">
                <div className="container">
                    <Row gutter={[48, 24]}>
                        <Col xs={24} md={8}>
                            <Title level={4} className="footer-brand">InvestEase</Title>
                            <Paragraph className="footer-tagline">
                                The smart way to build your crypto portfolio.
                            </Paragraph>
                        </Col>
                        <Col xs={12} md={8}>
                            <Title level={5}>Product</Title>
                            <div className="footer-links">
                                <a onClick={() => scrollToSection('benefits')}>Features</a>
                                <a onClick={() => scrollToSection('pricing')}>Pricing</a>
                                <a onClick={() => scrollToSection('how-it-works')}>How It Works</a>
                                <a onClick={() => navigate('/signup')}>Get Started</a>
                            </div>
                        </Col>
                        <Col xs={12} md={8}>
                            <Title level={5}>Company</Title>
                            <div className="footer-links">
                                <a href="/about">About Us</a>
                                <a href="/security">Security</a>
                                <a href="/terms">Terms of Service</a>
                                <a href="/privacy">Privacy Policy</a>
                            </div>
                        </Col>
                    </Row>
                    <Divider />
                    <div className="footer-bottom">
                        <Text className="footer-copyright">
                            © 2025 InvestEase. All rights reserved.
                        </Text>
                        <Text className="footer-disclaimer">
                            Cryptocurrency investments carry risk. Past performance is not indicative
                            of future results. Invest responsibly.
                        </Text>
                    </div>
                </div>
            </Footer>
        </Layout>
    );
};

export default LandingPage;