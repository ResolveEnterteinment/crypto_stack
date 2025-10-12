/**
 * LANDING PAGE COMPONENT
 * 
 * Professional, conversion-optimized landing page for crypto DCA platform
 * 
 * Features:
 * - Apple-inspired minimalist design
 * - Fully responsive layout
 * - Dark mode support
 * - Smooth animations
 * - Accessibility compliant
 * - SEO optimized
 * 
 * Integrated with:
 * - Global styling system (variables.css, global.css)
 * - Ant Design theme
 * - ThemeProvider for dark mode
 */

import React, { useEffect, useState, useCallback, useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
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
import Navbar from '../components/LandingPage/Navbar';
import '../styles/LandingPage/LandingPage.css';

/* ========================================
   TYPE DEFINITIONS
   ======================================== */

interface SectionProps {
    id?: string;
    className?: string;
    children: React.ReactNode;
}

/* ========================================
   DESTRUCTURED ANT DESIGN COMPONENTS
   ======================================== */

const { Content, Footer } = Layout;
const { Title, Text, Paragraph } = Typography;
const { useBreakpoint } = Grid;

/* ========================================
   SECTION WRAPPER COMPONENT
   ======================================== */

const Section: React.FC<SectionProps> = ({ id, className = '', children }) => (
    <section id={id} className={className}>
        {children}
    </section>
);

/* ========================================
   MAIN LANDING PAGE COMPONENT
   ======================================== */

const LandingPage: React.FC = () => {
    const navigate = useNavigate();
    const screens = useBreakpoint();
    const [scrolled, setScrolled] = useState(false);

    /* ========================================
       SCROLL HANDLER
       ======================================== */

    useEffect(() => {
        const handleScroll = () => {
            setScrolled(window.scrollY > 50);
        };

        window.addEventListener('scroll', handleScroll);
        return () => window.removeEventListener('scroll', handleScroll);
    }, []);

    /* ========================================
       SMOOTH SCROLL UTILITY
       ======================================== */

    const scrollToSection = useCallback((id: string) => {
        const element = document.getElementById(id);
        if (element) {
            element.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }
    }, []);

    /* ========================================
       NAVIGATION HANDLERS
       ======================================== */

    const handleGetStarted = useCallback(() => {
        navigate('/signup');
    }, [navigate]);

    const handleSeeHowItWorks = useCallback(() => {
        scrollToSection('how-it-works');
    }, [scrollToSection]);

    /* ========================================
       STATIC DATA (Memoized for performance)
       ======================================== */

    const trustStats = useMemo(() => [
        { value: 99.9, suffix: '%', label: 'Uptime' },
        { value: 256, suffix: '-bit', label: 'Encryption' },
        { value: 0, prefix: '$', label: 'Hidden Fees' },
        { value: 24, suffix: '/7', label: 'Support' }
    ], []);

    const benefits = useMemo(() => [
        {
            icon: <LineChartOutlined />,
            title: 'Smart Allocation',
            description: 'Set your portfolio allocation once. We automatically rebalance and execute purchases according to your strategy. Not just recurring buys—intelligent portfolio management.',
            highlights: ['Portfolio-level control', 'Automatic rebalancing']
        },
        {
            icon: <ThunderboltOutlined />,
            title: 'True Automation',
            description: 'Your crypto autopilot. Set it once, never think about it again. Our enterprise-grade flow engine handles edge cases, retries failed transactions, and ensures zero missed purchases.',
            highlights: ['Never miss a purchase', 'Works with any exchange']
        },
        {
            icon: <SafetyOutlined />,
            title: 'Total Transparency',
            description: 'Real-time dashboard shows exactly where your money goes. Track every transaction, monitor performance, see all fees. No surprises, no hidden costs, no duplicate charges.',
            highlights: ['Real-time tracking', 'Complete transparency']
        }
    ], []);

    const features = useMemo(() => [
        {
            icon: <DollarOutlined />,
            title: 'Fair, Transparent Pricing',
            description: 'Just 1% platform fee. No hidden charges. No monthly subscriptions. You see exactly what you pay before every transaction.'
        },
        {
            icon: <LockOutlined />,
            title: 'Bank-Level Security',
            description: '256-bit encryption. Secure KYC verification. Never store your credit card. Compliant with all financial regulations.'
        },
        {
            icon: <ThunderboltOutlined />,
            title: 'Enterprise-Grade Reliability',
            description: '99.9% uptime. Automatic retry on failures. Idempotency protection prevents duplicate charges. Never miss a purchase.'
        },
        {
            icon: <TeamOutlined />,
            title: 'Dedicated Support',
            description: 'Real humans, real fast. Email support within 2 hours. Comprehensive knowledge base. Active community forum.'
        }
    ], []);

    const faqs = useMemo(() => [
        {
            question: 'What is Dollar-Cost Averaging (DCA)?',
            answer: 'DCA is an investment strategy where you invest a fixed amount at regular intervals, regardless of price. This removes emotion from investing and reduces the impact of volatility. Instead of trying to time the market (which experts struggle with), you build your position steadily over time.'
        },
        {
            question: 'How is this different from Coinbase recurring buys?',
            answer: 'Coinbase offers simple recurring purchases. We offer portfolio-level allocation and automatic rebalancing. You set percentage allocations (e.g., 50% BTC, 30% ETH, 20% SOL), and we maintain that balance with each purchase. Plus, we work with any exchange, not just Coinbase.'
        },
        {
            question: 'Is my money safe?',
            answer: 'Yes. We use bank-level 256-bit encryption, never store your credit card information, and follow all financial regulations. Your crypto is purchased on regulated exchanges and can be withdrawn anytime. We\'re fully KYC-compliant and transparent about all operations.'
        },
        {
            question: 'Can I cancel anytime?',
            answer: 'Absolutely. You have full control. Pause your subscription, change your allocation, adjust your investment amount, or cancel completely—all with one click. No penalties, no fees, no questions asked.'
        },
        {
            question: 'What\'s the minimum investment?',
            answer: 'Just $50. We want investing to be accessible to everyone. Start small, grow as you\'re comfortable. Increase or decrease anytime.'
        }
    ], []);

    /* ========================================
       RENDER
       ======================================== */

    return (
        <Layout className="landing-page">
            {/* ==================== NAVIGATION ==================== */}
            <Navbar transparent={!scrolled} />

            <Content>
                {/* ==================== HERO SECTION ==================== */}
                <Section className="hero-section">
                    <div className="hero-content">
                        <div className="hero-text">
                            <Title level={1} className="hero-title">
                                Stop timing the market.
                                <br />
                                <span className="text-gradient-primary">Start building wealth.</span>
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
                                    onClick={handleGetStarted}
                                    icon={<ArrowRightOutlined />}
                                    iconPosition="end"
                                    aria-label="Get started with your first investment"
                                >
                                    Get Started Free
                                </Button>
                                <Button
                                    size="large"
                                    className="secondary-button"
                                    onClick={handleSeeHowItWorks}
                                    aria-label="Learn how our platform works"
                                >
                                    See How It Works
                                </Button>
                            </Space>
                            <Text className="hero-note">
                                <CheckCircleOutlined aria-hidden="true" /> First month free · No credit card required
                            </Text>
                        </div>

                        {/* Hero Visual - Dashboard Preview */}
                        <div className="hero-visual" role="img" aria-label="Portfolio dashboard preview">
                            <div className="dashboard-preview">
                                <div className="preview-header">
                                    <div className="preview-dots" aria-hidden="true">
                                        <span className="dot red" />
                                        <span className="dot yellow" />
                                        <span className="dot green" />
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
                                    <div className="chart-placeholder" aria-hidden="true">
                                        <div className="chart-line" />
                                        <Text className="chart-label">+11.6% growth</Text>
                                    </div>
                                </div>
                            </div>
                        </div>
                    </div>
                </Section>

                {/* ==================== TRUST INDICATORS ==================== */}
                <Section className="trust-section">
                    <div className="container-narrow">
                        <Row gutter={[48, 24]} justify="center" align="middle">
                            {trustStats.map((stat, index) => (
                                <Col xs={12} md={6} key={index} className="trust-stat">
                                    <Statistic
                                        value={stat.value}
                                        prefix={stat.prefix}
                                        suffix={stat.suffix}
                                        valueStyle={{ fontSize: '32px', fontWeight: 600 }}
                                    />
                                    <Text className="trust-label">{stat.label}</Text>
                                </Col>
                            ))}
                        </Row>
                    </div>
                </Section>

                {/* ==================== BENEFITS SECTION ==================== */}
                <Section id="benefits" className="benefits-section">
                    <div className="container">
                        <header className="section-header">
                            <Title level={2} className="section-title">
                                Built for serious investors.
                                <br />
                                <span className="text-secondary">Designed for everyone.</span>
                            </Title>
                        </header>

                        <Row gutter={[48, 48]} className="benefits-grid">
                            {benefits.map((benefit, index) => (
                                <Col xs={24} md={8} key={index}>
                                    <article className="benefit-card">
                                        <div className="benefit-icon" aria-hidden="true">
                                            {benefit.icon}
                                        </div>
                                        <Title level={3} className="benefit-title">
                                            {benefit.title}
                                        </Title>
                                        <Paragraph className="benefit-description">
                                            {benefit.description}
                                        </Paragraph>
                                        <div className="benefit-highlight">
                                            {benefit.highlights.map((highlight, i) => (
                                                <React.Fragment key={i}>
                                                    <CheckCircleOutlined aria-hidden="true" /> {highlight}
                                                    {i < benefit.highlights.length - 1 && <br />}
                                                </React.Fragment>
                                            ))}
                                        </div>
                                    </article>
                                </Col>
                            ))}
                        </Row>
                    </div>
                </Section>

                {/* ==================== HOW IT WORKS ==================== */}
                <Section id="how-it-works" className="how-it-works-section">
                    <div className="container-narrow">
                        <header className="section-header">
                            <Title level={2} className="section-title">
                                Get started in minutes.
                            </Title>
                            <Paragraph className="section-description">
                                No complex forms. No confusing interfaces. Just simple, elegant automation.
                            </Paragraph>
                        </header>

                        <div className="steps-container">
                            <div className="step-item">
                                <div className="step-number" aria-label="Step 1">1</div>
                                <div className="step-content">
                                    <Title level={4}>Create Your Account</Title>
                                    <Paragraph>
                                        Sign up with your email. Quick identity verification for security.
                                        Takes less than 2 minutes.
                                    </Paragraph>
                                </div>
                            </div>

                            <div className="step-divider" aria-hidden="true" />

                            <div className="step-item">
                                <div className="step-number" aria-label="Step 2">2</div>
                                <div className="step-content">
                                    <Title level={4}>Set Your Strategy</Title>
                                    <Paragraph>
                                        Choose investment amount, frequency, and asset allocation.
                                        Customize your portfolio or use our smart defaults.
                                    </Paragraph>
                                </div>
                            </div>

                            <div className="step-divider" aria-hidden="true" />

                            <div className="step-item">
                                <div className="step-number" aria-label="Step 3">3</div>
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
                                onClick={handleGetStarted}
                                icon={<ArrowRightOutlined />}
                                iconPosition="end"
                            >
                                Start Your First Investment
                            </Button>
                        </div>
                    </div>
                </Section>

                {/* ==================== WHY CHOOSE US ==================== */}
                <Section className="why-choose-section">
                    <div className="container">
                        <header className="section-header">
                            <Title level={2} className="section-title">
                                Why choose us?
                            </Title>
                        </header>

                        <Row gutter={[32, 32]}>
                            {features.map((feature, index) => (
                                <Col xs={24} md={12} key={index}>
                                    <article className="feature-item">
                                        <div className="feature-icon" aria-hidden="true">
                                            {feature.icon}
                                        </div>
                                        <div className="feature-content">
                                            <Title level={4}>{feature.title}</Title>
                                            <Paragraph>{feature.description}</Paragraph>
                                        </div>
                                    </article>
                                </Col>
                            ))}
                        </Row>
                    </div>
                </Section>

                {/* ==================== TESTIMONIAL ==================== */}
                <Section className="testimonial-section">
                    <div className="container-narrow">
                        <article className="testimonial-card">
                            <div className="quote-mark" aria-hidden="true">"</div>
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
                        </article>
                    </div>
                </Section>

                {/* ==================== PRICING ==================== */}
                <Section id="pricing" className="pricing-section">
                    <div className="container-narrow">
                        <header className="section-header">
                            <Title level={2} className="section-title">
                                Simple, transparent pricing.
                            </Title>
                            <Paragraph className="section-description">
                                No hidden fees. No surprises. Just honest pricing.
                            </Paragraph>
                        </header>

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
                                onClick={handleGetStarted}
                            >
                                Start Investing Now
                            </Button>
                        </div>
                    </div>
                </Section>

                {/* ==================== FAQ ==================== */}
                <Section id="faq" className="faq-section">
                    <div className="container-narrow">
                        <header className="section-header">
                            <Title level={2} className="section-title">
                                Common questions.
                            </Title>
                        </header>

                        <div className="faq-list">
                            {faqs.map((faq, index) => (
                                <article key={index} className="faq-item">
                                    <Title level={4} className="faq-question">
                                        {faq.question}
                                    </Title>
                                    <Paragraph className="faq-answer">
                                        {faq.answer}
                                    </Paragraph>
                                </article>
                            ))}
                        </div>
                    </div>
                </Section>

                {/* ==================== FINAL CTA ==================== */}
                <Section className="final-cta-section">
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
                                onClick={handleGetStarted}
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
                </Section>
            </Content>

            {/* ==================== FOOTER ==================== */}
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
                            <nav className="footer-links">
                                <a onClick={() => scrollToSection('benefits')}>Features</a>
                                <a onClick={() => scrollToSection('pricing')}>Pricing</a>
                                <a onClick={() => scrollToSection('how-it-works')}>How It Works</a>
                                <a onClick={handleGetStarted}>Get Started</a>
                            </nav>
                        </Col>
                        <Col xs={12} md={8}>
                            <Title level={5}>Company</Title>
                            <nav className="footer-links">
                                <a href="/about">About Us</a>
                                <a href="/security">Security</a>
                                <a href="/terms">Terms of Service</a>
                                <a href="/privacy">Privacy Policy</a>
                            </nav>
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