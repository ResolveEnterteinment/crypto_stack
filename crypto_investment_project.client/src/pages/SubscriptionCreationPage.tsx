import {
    CheckCircleOutlined,
    ExclamationCircleOutlined,
    LoadingOutlined
} from '@ant-design/icons';
import { Alert, Button, Card, message, Spin, Steps } from 'antd';
import React, { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import Navbar from '../components/Navbar';
import AssetAllocationStep from '../components/Subscription/AssetAllocationStep';
import PlanDetailsStep from '../components/Subscription/PlanDetailsStep';
import ReviewStep from '../components/Subscription/ReviewStep';
import { useAuth } from '../context/AuthContext';
import { getSupportedAssets } from '../services/asset';
import { SessionResponse } from '../services/payment';
import { createSubscription } from '../services/subscription';
import { Asset } from '../types/assetTypes';
import { Allocation } from '../types/subscription';
import { formatApiError } from '../utils/apiErrorHandler';
import ICreateSubscriptionRequest from '../interfaces/ICreateSubscriptionRequest';

const { Step } = Steps;

/**
 * Error Feedback Component
 * Displays dismissible error messages with improved UX
 */
const ErrorFeedback: React.FC<{ error: string | null; onDismiss: () => void }> = ({
    error,
    onDismiss
}) => {
    if (!error) return null;

    return (
        <Alert
            message="Error"
            description={error}
            type="error"
            closable
            onClose={onDismiss}
            showIcon
            icon={<ExclamationCircleOutlined />}
            className="mb-lg animate-fade-in"
            style={{ marginBottom: 'var(--spacing-lg)' }}
        />
    );
};

/**
 * Validation Errors Component
 * Displays multiple validation errors in a list
 */
const ValidationErrors: React.FC<{ errors: string[] }> = ({ errors }) => {
    if (errors.length === 0) return null;

    return (
        <Alert
            message="Please fix the following issues:"
            description={
                <ul className="m-0 pl-lg">
                    {errors.map((err, index) => (
                        <li key={index}>{err}</li>
                    ))}
                </ul>
            }
            type="warning"
            showIcon
            icon={<ExclamationCircleOutlined />}
            className="mb-lg animate-fade-in"
            style={{ marginBottom: 'var(--spacing-lg)' }}
        />
    );
};

/**
 * Full-Page Loading Overlay
 */
const PageLoader: React.FC = () => (
    <div className="page-loader">
        <Spin
            indicator={<LoadingOutlined style={{ fontSize: 48 }} spin />}
            tip="Loading subscription data..."
            size="large"
        />
    </div>
);

/**
 * Main Subscription Creation Page Content
 */
const SubscriptionCreationPageContent: React.FC = () => {
    const navigate = useNavigate();
    const { user, isAuthenticated } = useAuth();

    // State management
    const [currentStep, setCurrentStep] = useState<number>(0);
    const [isLoading, setIsLoading] = useState<boolean>(true);
    const [error, setError] = useState<string | null>(null);
    const [availableAssets, setAvailableAssets] = useState<Asset[]>([]);
    const [paymentProcessing, setPaymentProcessing] = useState<boolean>(false);
    const [validationErrors, setValidationErrors] = useState<string[]>([]);

    // Form data state
    const [formData, setFormData] = useState({
        userId: user?.id || '',
        interval: 'MONTHLY',
        amount: 100,
        currency: 'USD',
        endDate: null as Date | null,
        allocations: [] as Allocation[]
    });

    /**
     * Reset error states when changing steps
     */
    useEffect(() => {
        setError(null);
        setValidationErrors([]);
    }, [currentStep]);

    /**
     * Load available assets on component mount
     */
    useEffect(() => {
        const loadAssets = async () => {
            try {
                setIsLoading(true);
                const assets = await getSupportedAssets();

                if (assets && assets.length > 0) {
                    setAvailableAssets(assets);
                } else {
                    setError("No investment assets are currently available. Please try again later.");
                }
            } catch (error) {
                console.error('Error loading assets:', error);
                setError('Failed to load available assets. Please try again later.');
            } finally {
                setIsLoading(false);
            }
        };

        if (isAuthenticated && user) {
            loadAssets();
            setFormData(prev => ({
                ...prev,
                userId: user.id
            }));
        } else {
            setIsLoading(false);
        }
    }, [isAuthenticated, user]);

    /**
     * Update form data helper
     */
    const updateFormData = (field: string, value: any) => {
        setFormData(prevData => ({
            ...prevData,
            [field]: value
        }));
    };

    /**
     * Validate current step
     */
    const validateCurrentStep = (): boolean => {
        setError(null);
        setValidationErrors([]);
        const errors: string[] = [];

        switch (currentStep) {
            case 0: // Plan details
                if (!formData.interval) {
                    errors.push('Please select an investment frequency');
                }
                if (!formData.amount || formData.amount <= 0) {
                    errors.push('Please enter a valid amount');
                }
                if (formData.amount < 10) {
                    errors.push('Minimum investment amount is $10');
                }
                if (formData.interval !== 'ONCE' && formData.endDate) {
                    const today = new Date();
                    if (formData.endDate <= today) {
                        errors.push('End date must be in the future');
                    }
                }
                break;

            case 1: // Asset allocation
                if (formData.allocations.length === 0) {
                    errors.push('Please add at least one asset allocation');
                }

                const totalPercent = formData.allocations.reduce(
                    (sum, allocation) => sum + allocation.percentAmount, 0
                );

                if (Math.abs(totalPercent - 100) > 0.01) {
                    errors.push(`Total allocation must equal 100% (currently ${totalPercent.toFixed(1)}%)`);
                }
                break;

            case 2: // Review - no additional validation needed
                break;
        }

        if (errors.length > 0) {
            setValidationErrors(errors);
            window.scrollTo({ top: 0, behavior: 'smooth' });
            return false;
        }

        return true;
    };

    /**
     * Move to next step
     */
    const goToNextStep = () => {
        if (validateCurrentStep() && currentStep < 2) {
            setCurrentStep(currentStep + 1);
            window.scrollTo({ top: 0, behavior: 'smooth' });
        }
    };

    /**
     * Move to previous step
     */
    const goToPreviousStep = () => {
        if (currentStep > 0) {
            setCurrentStep(currentStep - 1);
            window.scrollTo({ top: 0, behavior: 'smooth' });
        }
    };

    /**
     * Handle form submission
     */
    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();

        // Only submit on final step
        if (currentStep < 2) {
            goToNextStep();
            return;
        }

        // Validate before submission
        if (!validateCurrentStep()) {
            return;
        }

        setPaymentProcessing(true);
        setError(null);

        try {
            message.loading({ content: 'Processing your subscription...', key: 'subscription', duration: 0 });

            const subscriptionData: ICreateSubscriptionRequest = {
                ...formData,
                allocations: formData.allocations.map(allocation => ({
                    assetId: allocation.assetId,
                    percentAmount: allocation.percentAmount
                })),
                successUrl: window.location.origin + `/payment/checkout/success`,
                cancelUrl: window.location.origin + `/payment/checkout/cancel`
            };

            const response: SessionResponse = await createSubscription(subscriptionData);

            if (response.checkoutUrl) {
                message.destroy('subscription');
                message.success('Redirecting to payment...');
                window.location.href = response.checkoutUrl;
            } else {
                throw new Error('No payment URL received from server');
            }
        } catch (err) {
            message.destroy('subscription');
            console.error('Subscription creation error:', err);

            const errorMessage = formatApiError(err);
            setError(errorMessage);
            message.error(errorMessage);

            window.scrollTo({ top: 0, behavior: 'smooth' });
        } finally {
            setPaymentProcessing(false);
        }
    };

    // Show loading state
    if (isLoading) {
        return <PageLoader />;
    }

    // Show auth error
    if (!isAuthenticated) {
        return (
            <div className="container" style={{ paddingTop: 'var(--spacing-2xl)' }}>
                <Alert
                    message="Authentication Required"
                    description="Please log in to create a subscription."
                    type="warning"
                    showIcon
                    action={
                        <Button type="primary" onClick={() => navigate('/login')}>
                            Log In
                        </Button>
                    }
                />
            </div>
        );
    }

    // Define steps configuration
    const stepsConfig = [
        {
            title: 'Plan Details',
            icon: currentStep > 0 ? <CheckCircleOutlined /> : undefined,
        },
        {
            title: 'Asset Allocation',
            icon: currentStep > 1 ? <CheckCircleOutlined /> : undefined,
        },
        {
            title: 'Review & Confirm',
            icon: currentStep > 2 ? <CheckCircleOutlined /> : undefined,
        },
    ];

    return (
        <div className="container animate-fade-in" style={{ paddingTop: 'var(--spacing-2xl)', paddingBottom: 'var(--spacing-2xl)' }}>
            {/* Page Header */}
            <div className="text-center mb-xl">
                <h1 className="text-h1 mb-md">Create Your Investment Plan</h1>
                <p className="text-body-lg text-secondary">
                    Set up regular investments in your preferred cryptocurrencies
                </p>
            </div>

            {/* Progress Steps */}
            <Card className="mb-xl elevation-2" bordered={false}>
                <Steps current={currentStep} items={stepsConfig} />
            </Card>

            {/* Main Content Card */}
            <Card className="elevation-3" bordered={false}>
                {/* Error Feedback */}
                <ErrorFeedback error={error} onDismiss={() => setError(null)} />

                {/* Validation Errors */}
                <ValidationErrors errors={validationErrors} />

                {/* Step Content */}
                <form onSubmit={handleSubmit}>
                    {/* Step 1: Plan Details */}
                    {currentStep === 0 && (
                        <div className="animate-slide-up">
                            <PlanDetailsStep
                                formData={formData}
                                updateFormData={updateFormData}
                            />
                        </div>
                    )}

                    {/* Step 2: Asset Allocation */}
                    {currentStep === 1 && (
                        <div className="animate-slide-up">
                            <AssetAllocationStep
                                formData={formData}
                                updateFormData={updateFormData}
                                availableAssets={availableAssets}
                                isLoading={false}
                                error={error}
                            />
                        </div>
                    )}

                    {/* Step 3: Review */}
                    {currentStep === 2 && (
                        <div className="animate-slide-up">
                            <ReviewStep formData={formData} />
                        </div>
                    )}

                    {/* Navigation Buttons */}
                    <div className="flex-between mt-xl" style={{ marginTop: 'var(--spacing-xl)' }}>
                        {/* Back/Cancel Button */}
                        {currentStep > 0 ? (
                            <Button
                                size="large"
                                onClick={goToPreviousStep}
                                disabled={paymentProcessing}
                            >
                                Back
                            </Button>
                        ) : (
                            <Button
                                size="large"
                                onClick={() => navigate('/dashboard')}
                                disabled={paymentProcessing}
                            >
                                Cancel
                            </Button>
                        )}

                        {/* Next/Submit Button */}
                        <Button
                            type="primary"
                            size="large"
                            htmlType="submit"
                            loading={paymentProcessing}
                            disabled={currentStep === 1 && formData.allocations.length === 0}
                            icon={currentStep === 2 ? <CheckCircleOutlined /> : undefined}
                            className={currentStep === 2 ? 'hover-glow' : ''}
                        >
                            {paymentProcessing
                                ? 'Processing...'
                                : currentStep < 2
                                    ? 'Continue'
                                    : 'Confirm & Pay'
                            }
                        </Button>
                    </div>
                </form>
            </Card>

            {/* Help Section */}
            <div className="text-center mt-xl">
                <p className="text-body-sm text-secondary">
                    Having trouble?{' '}
                    <Button
                        type="link"
                        onClick={() => window.open('/help/subscription-creation', '_blank')}
                        className="p-0"
                    >
                        View Help Guide
                    </Button>
                    {' '}or{' '}
                    <Button
                        type="link"
                        onClick={() => window.open('/contact', '_blank')}
                        className="p-0"
                    >
                        Contact Support
                    </Button>
                </p>
            </div>
        </div>
    );
};

/**
 * Main Page Component with Navbar
 */
const SubscriptionCreationPage: React.FC = () => {
    return (
        <>
            <Navbar />
            <main style={{ marginTop: 'var(--nav-height)' }}>
                <SubscriptionCreationPageContent />
            </main>
        </>
    );
};

export default SubscriptionCreationPage;