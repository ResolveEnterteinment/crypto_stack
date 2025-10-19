/**
 * AUTHENTICATION PAGE COMPONENT
 * 
 * Professional authentication interface using Ant Design
 * Integrated with global styling system and theme provider
 * 
 * Features:
 * - Apple-inspired minimalist design
 * - Login & Registration forms
 * - Email confirmation workflow
 * - Form validation with visual feedback
 * - Loading states and error handling
 * - Responsive layout
 * - Dark mode support
 * - Accessibility compliant
 */

import React, { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import {
    Alert,
    Button,
    Card,
    Divider,
    Form,
    Input,
    Space,
    Tabs,
    Typography,
    message
} from "antd";
import {
    CheckCircleOutlined,
    EyeInvisibleOutlined,
    EyeTwoTone,
    InfoCircleOutlined,
    LockOutlined,
    MailOutlined,
    UserOutlined,
    WarningOutlined
} from "@ant-design/icons";
import { useAuth } from "../context/AuthContext";
import authService from "../services/authService";
import "../styles/Auth/AuthPage.css";

const { Title, Text, Paragraph } = Typography;

/* ========================================
   TYPE DEFINITIONS
   ======================================== */

interface LoginFormValues {
    email: string;
    password: string;
}

interface RegisterFormValues {
    fullName: string;
    email: string;
    password: string;
}

/* ========================================
   MAIN COMPONENT
   ======================================== */

const AuthPage: React.FC = () => {
    const { user, login } = useAuth();
    const navigate = useNavigate();
    const [form] = Form.useForm();

    // UI States
    const [activeTab, setActiveTab] = useState<'login' | 'register'>('login');
    const [isLoading, setIsLoading] = useState(false);
    const [registrationSuccess, setRegistrationSuccess] = useState(false);
    const [unconfirmedEmailLogin, setUnconfirmedEmailLogin] = useState(false);
    const [resendingEmail, setResendingEmail] = useState(false);
    const [userEmail, setUserEmail] = useState("");

    // Redirect if already authenticated
    useEffect(() => {
        if (user) {
            navigate("/dashboard");
        }
    }, [user, navigate]);

    /* ========================================
       FORM HANDLERS
       ======================================== */

    /**
     * Handle login form submission
     */
    const handleLogin = async (values: LoginFormValues) => {
        setIsLoading(true);
        setUnconfirmedEmailLogin(false);
        setUserEmail(values.email);

        try {
            const response = await authService.login({
                email: values.email,
                password: values.password
            });

            await login(response);
            message.success('Successfully logged in!');
            navigate("/dashboard");

        } catch (err: any) {
            console.error("Login error:", err);

            // Handle specific error cases
            if (err.message?.includes("Email is not confirmed") ||
                err.message?.includes("EMAIL_NOT_CONFIRMED")) {
                setUnconfirmedEmailLogin(true);
            } else if (err.message?.includes("Invalid credentials") ||
                err.message?.includes("INVALID_CREDENTIALS")) {
                message.error("Invalid email or password. Please try again.");
            } else if (err.message?.includes("Account is temporarily locked") ||
                err.message?.includes("ACCOUNT_LOCKED")) {
                message.error("Your account is temporarily locked. Please try again later or reset your password.");
            } else {
                message.error(err.message || "An error occurred during login. Please try again later.");
            }
        } finally {
            setIsLoading(false);
        }
    };

    /**
     * Handle registration form submission
     */
    const handleRegister = async (values: RegisterFormValues) => {
        setIsLoading(true);
        setUserEmail(values.email);

        try {
            await authService.register({
                fullName: values.fullName,
                email: values.email,
                password: values.password
            });

            setRegistrationSuccess(true);
            form.resetFields();
            message.success('Registration successful! Please check your email.');

        } catch (err: any) {
            console.error("Registration error:", err);

            // Handle validation errors
            if (err.response?.data?.validationErrors) {
                const validationErrors = err.response.data.validationErrors;
                const formErrors: any = {};

                if (validationErrors.Email) {
                    formErrors.email = validationErrors.Email[0];
                }
                if (validationErrors.Password) {
                    formErrors.password = validationErrors.Password[0];
                }
                if (validationErrors.FullName) {
                    formErrors.fullName = validationErrors.FullName[0];
                }

                form.setFields(Object.keys(formErrors).map(key => ({
                    name: key,
                    errors: [formErrors[key]]
                })));

                message.error("Please correct the errors in the form.");
            } else if (err.message?.includes("Email already registered") ||
                err.message?.includes("EMAIL_ALREADY_REGISTERED")) {
                form.setFields([{
                    name: 'email',
                    errors: ['This email is already registered. Please login instead.']
                }]);
                message.error("Email already registered.");
            } else {
                message.error(err.message || "An error occurred during registration. Please try again later.");
            }
        } finally {
            setIsLoading(false);
        }
    };

    /**
     * Handle resend confirmation email
     */
    const handleResendConfirmation = async () => {
        if (!userEmail) {
            message.error('Email address not found. Please try again.');
            return;
        }

        setResendingEmail(true);

        try {
            await authService.resendConfirmation({ email: userEmail });
            message.success('Confirmation email sent successfully! Please check your inbox.');
        } catch (err: any) {
            console.error("Resend confirmation error:", err);
            message.error(err.message || "Failed to resend confirmation email. Please try again.");
        } finally {
            setResendingEmail(false);
        }
    };

    /* ========================================
       VIEW COMPONENTS
       ======================================== */

    /**
     * Render login form
     */
    const renderLoginForm = () => (
        <Form
            form={form}
            name="login"
            onFinish={handleLogin}
            layout="vertical"
            size="large"
            requiredMark={false}
            autoComplete="off"
        >
            <Form.Item
                name="email"
                label="Email Address"
                rules={[
                    { required: true, message: 'Please enter your email' },
                    { type: 'email', message: 'Please enter a valid email address' }
                ]}
            >
                <Input
                    prefix={<MailOutlined className="input-icon" />}
                    placeholder="your@email.com"
                    autoComplete="email"
                />
            </Form.Item>

            <Form.Item
                name="password"
                label="Password"
                rules={[
                    { required: true, message: 'Please enter your password' },
                    { min: 8, message: 'Password must be at least 8 characters' }
                ]}
            >
                <Input.Password
                    prefix={<LockOutlined className="input-icon" />}
                    placeholder="Enter your password"
                    iconRender={(visible) => (visible ? <EyeTwoTone /> : <EyeInvisibleOutlined />)}
                    autoComplete="current-password"
                />
            </Form.Item>

            <div className="form-footer">
                <Link to="/forgot-password" className="forgot-password-link">
                    Forgot password?
                </Link>
            </div>

            <Form.Item className="submit-button-wrapper">
                <Button
                    type="primary"
                    htmlType="submit"
                    loading={isLoading}
                    block
                    className="submit-button"
                >
                    {isLoading ? 'Signing in...' : 'Sign In'}
                </Button>
            </Form.Item>

            <Divider plain className="divider-text">
                Don't have an account?
            </Divider>

            <Button
                type="link"
                onClick={() => setActiveTab('register')}
                block
                className="switch-form-button"
            >
                Create a new account
            </Button>
        </Form>
    );

    /**
     * Render registration form
     */
    const renderRegisterForm = () => (
        <Form
            form={form}
            name="register"
            onFinish={handleRegister}
            layout="vertical"
            size="large"
            requiredMark={false}
            autoComplete="off"
        >
            <Form.Item
                name="fullName"
                label="Full Name"
                rules={[
                    { required: true, message: 'Please enter your full name' },
                    { min: 2, message: 'Name must be at least 2 characters' }
                ]}
            >
                <Input
                    prefix={<UserOutlined className="input-icon" />}
                    placeholder="John Doe"
                    autoComplete="name"
                />
            </Form.Item>

            <Form.Item
                name="email"
                label="Email Address"
                rules={[
                    { required: true, message: 'Please enter your email' },
                    { type: 'email', message: 'Please enter a valid email address' }
                ]}
            >
                <Input
                    prefix={<MailOutlined className="input-icon" />}
                    placeholder="your@email.com"
                    autoComplete="email"
                />
            </Form.Item>

            <Form.Item
                name="password"
                label="Password"
                rules={[
                    { required: true, message: 'Please enter a password' },
                    { min: 8, message: 'Password must be at least 8 characters' }
                ]}
                extra={
                    <Text type="secondary" className="password-hint">
                        Password must be at least 8 characters long and contain a mix of letters, numbers, and special characters.
                    </Text>
                }
            >
                <Input.Password
                    prefix={<LockOutlined className="input-icon" />}
                    placeholder="Create a strong password"
                    iconRender={(visible) => (visible ? <EyeTwoTone /> : <EyeInvisibleOutlined />)}
                    autoComplete="new-password"
                />
            </Form.Item>

            <Form.Item className="submit-button-wrapper">
                <Button
                    type="primary"
                    htmlType="submit"
                    loading={isLoading}
                    block
                    className="submit-button"
                >
                    {isLoading ? 'Creating account...' : 'Create Account'}
                </Button>
            </Form.Item>

            <Divider plain className="divider-text">
                Already have an account?
            </Divider>

            <Button
                type="link"
                onClick={() => setActiveTab('login')}
                block
                className="switch-form-button"
            >
                Sign in to your account
            </Button>
        </Form>
    );

    /**
     * Render registration success view
     */
    const renderRegistrationSuccessView = () => (
        <div className="success-view">
            <div className="success-icon-wrapper">
                <CheckCircleOutlined className="success-icon" />
            </div>

            <Title level={2} className="success-title">
                Registration Successful!
            </Title>

            <Paragraph className="success-description">
                We've sent a confirmation link to <Text strong>{userEmail}</Text>.
                Please check your email to verify your account before logging in.
            </Paragraph>

            <Alert
                message="Don't see the email?"
                description="If you don't see the email in your inbox, please check your spam folder."
                type="info"
                icon={<InfoCircleOutlined />}
                showIcon
                className="info-alert"
            />

            <Space direction="vertical" size="middle" className="button-group">
                <Button
                    onClick={handleResendConfirmation}
                    loading={resendingEmail}
                    block
                    size="large"
                    className="secondary-button"
                >
                    {resendingEmail ? 'Sending...' : 'Resend Confirmation Email'}
                </Button>

                <Button
                    type="primary"
                    onClick={() => {
                        setActiveTab('login');
                        setRegistrationSuccess(false);
                    }}
                    block
                    size="large"
                    className="primary-button"
                >
                    Go to Login
                </Button>
            </Space>
        </div>
    );

    /**
     * Render unconfirmed email view
     */
    const renderUnconfirmedEmailView = () => (
        <div className="warning-view">
            <div className="warning-icon-wrapper">
                <WarningOutlined className="warning-icon" />
            </div>

            <Title level={2} className="warning-title">
                Email Not Verified
            </Title>

            <Paragraph className="warning-description">
                Your account exists, but your email <Text strong>{userEmail}</Text> hasn't been verified yet.
                Please check your email for the verification link we sent when you registered.
            </Paragraph>

            <Alert
                message="Check your spam folder"
                description="If you don't see the verification email in your inbox, please check your spam folder or request a new verification email."
                type="warning"
                icon={<InfoCircleOutlined />}
                showIcon
                className="warning-alert"
            />

            <Space direction="vertical" size="middle" className="button-group">
                <Button
                    onClick={handleResendConfirmation}
                    loading={resendingEmail}
                    block
                    size="large"
                    className="secondary-button"
                >
                    {resendingEmail ? 'Sending...' : 'Resend Verification Email'}
                </Button>

                <Button
                    type="primary"
                    onClick={() => {
                        setUnconfirmedEmailLogin(false);
                    }}
                    block
                    size="large"
                    className="primary-button"
                >
                    Back to Login
                </Button>
            </Space>
        </div>
    );

    /* ========================================
       RENDER MAIN VIEW
       ======================================== */

    return (
        <div className="auth-page">
            <div className="auth-container">
                <Card className="auth-card" bordered={false}>
                    {registrationSuccess ? (
                        renderRegistrationSuccessView()
                    ) : unconfirmedEmailLogin ? (
                        renderUnconfirmedEmailView()
                    ) : (
                        <>
                            <div className="auth-header">
                                <Title level={1} className="auth-title">
                                    {activeTab === 'login' ? 'Welcome Back' : 'Get Started'}
                                </Title>
                                <Text type="secondary" className="auth-subtitle">
                                    {activeTab === 'login'
                                        ? 'Sign in to your account to continue'
                                        : 'Create your account to get started'}
                                </Text>
                            </div>

                            <Tabs
                                activeKey={activeTab}
                                onChange={(key) => {
                                    setActiveTab(key as 'login' | 'register');
                                    form.resetFields();
                                }}
                                centered
                                className="auth-tabs"
                                items={[
                                    {
                                        key: 'login',
                                        label: 'Sign In',
                                        children: renderLoginForm()
                                    },
                                    {
                                        key: 'register',
                                        label: 'Sign Up',
                                        children: renderRegisterForm()
                                    }
                                ]}
                            />
                        </>
                    )}
                </Card>
            </div>
        </div>
    );
};

export default AuthPage;