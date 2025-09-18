// src/pages/SubscriptionCreationPage.tsx - Enhanced Version
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

// Error feedback component with improved UX
const ErrorFeedback: React.FC<{ error: string | null; onDismiss: () => void }> = ({ error, onDismiss }) => {
    if (!error) return null;

    return (
        <div className="bg-red-50 border-l-4 border-red-500 p-4 mb-6 relative">
            <button
                onClick={onDismiss}
                className="absolute top-2 right-2 text-red-500 hover:text-red-700"
                aria-label="Dismiss error"
            >
                <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 18L18 6M6 6l12 12"></path>
                </svg>
            </button>
            <div className="flex">
                <div className="flex-shrink-0">
                    <svg className="h-5 w-5 text-red-500" viewBox="0 0 20 20" fill="currentColor">
                        <path fillRule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7 4a1 1 0 11-2 0 1 1 0 012 0zm-1-9a1 1 0 00-1 1v4a1 1 0 102 0V6a1 1 0 00-1-1z" clipRule="evenodd" />
                    </svg>
                </div>
                <div className="ml-3">
                    <p className="text-sm text-red-700">{error}</p>
                </div>
            </div>
        </div>
    );
};

const SubscriptionCreationPageContent: React.FC = () => {
    const navigate = useNavigate();
    const { user, isAuthenticated } = useAuth();
    const [currentStep, setCurrentStep] = useState<number>(1);
    const [isLoading, setIsLoading] = useState<boolean>(true);
    const [error, setError] = useState<string | null>(null);
    const [availableAssets, setAvailableAssets] = useState<Asset[]>([]);
    const [paymentProcessing, setPaymentProcessing] = useState<boolean>(false);
    const [validationErrors, setValidationErrors] = useState<string[]>([]);

    // Form data state with default values
    const [formData, setFormData] = useState({
        userId: user?.id || '',
        interval: 'MONTHLY',
        amount: 100,
        currency: 'USD',
        endDate: null as Date | null,
        allocations: [] as Allocation[]
    });

    // Reset error when changing steps
    useEffect(() => {
        setError(null);
        setValidationErrors([]);
    }, [currentStep]);

    // Load available assets on component mount
    useEffect(() => {
        const loadAssets = async () => {
            try {
                setIsLoading(true);
                const assets = await getSupportedAssets();

                if (assets && assets.length > 0) {
                    setAvailableAssets(assets);
                    setIsLoading(false);
                } else {
                    // If no assets, show an error
                    setError("No investment assets are currently available. Please try again later.");
                    setIsLoading(false);
                }
            } catch (error) {
                console.error('Error loading assets:', error);
                setError('Failed to load available assets. Please try again later.');
                setIsLoading(false);
            }
        };

        // Only load assets if user is authenticated
        if (isAuthenticated && user) {
            loadAssets();
            // Update userId in form data when user is available
            setFormData(prev => ({
                ...prev,
                userId: user.id
            }));
        } else {
            setIsLoading(false);
        }
    }, [isAuthenticated, user]);

    // Helper function to update form data
    const updateFormData = (field: string, value: any) => {
        setFormData(prevData => ({
            ...prevData,
            [field]: value
        }));
    };

    // Move to the next step
    const goToNextStep = () => {
        if (currentStep < 3) {
            if (validateCurrentStep()) {
                setCurrentStep(currentStep + 1);
                window.scrollTo(0, 0);
            }
        }
    };

    // Move to the previous step
    const goToPreviousStep = () => {
        if (currentStep > 1) {
            setCurrentStep(currentStep - 1);
            window.scrollTo(0, 0);
        }
    };

    // Validate current step
    const validateCurrentStep = (): boolean => {
        setError(null);
        setValidationErrors([]);
        const errors: string[] = [];

        switch (currentStep) {
            case 1: // Plan details
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
            case 2: // Asset allocation
                if (formData.allocations.length === 0) {
                    errors.push('Please add at least one asset allocation');
                }

                // Check if allocations sum to 100%
                const totalPercent = formData.allocations.reduce(
                    (sum, allocation) => sum + allocation.percentAmount, 0
                );

                if (Math.abs(totalPercent - 100) > 0.01) {
                    errors.push(`Asset allocations total ${totalPercent.toFixed(1)}%, but must sum to 100%`);
                }

                // Validate that each allocation has required properties
                formData.allocations.forEach((allocation, index) => {
                    if (!allocation.assetId) {
                        errors.push(`Allocation #${index + 1} is missing asset information`);
                    }
                    if (!allocation.percentAmount || allocation.percentAmount <= 0) {
                        errors.push(`Allocation #${index + 1} has invalid percentage amount`);
                    }
                });
                break;
            case 3: // Review
                // Final validation check before submission
                if (!formData.userId) {
                    errors.push('User information is missing. Please try logging in again.');
                }
                if (formData.allocations.length === 0) {
                    errors.push('Your investment plan requires at least one asset allocation');
                }
                if (!formData.amount || formData.amount < 10) {
                    errors.push('Invalid investment amount');
                }
                break;
            default:
                break;
        }

        if (errors.length > 0) {
            setValidationErrors(errors);
            setError(errors.join('. '));
            return false;
        }

        return true;
    };

    // Handle form submission
    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();

        if (!validateCurrentStep()) {
            return;
        }

        if (currentStep < 3) {
            goToNextStep();
            return;
        }

        try {
            setPaymentProcessing(true);
            setError(null);

            // Format request payload
            const subscriptionRequest = {
                userId: formData.userId,
                allocations: formData.allocations,
                interval: formData.interval,
                amount: formData.amount,
                currency: formData.currency,
                endDate: formData.endDate,
                successUrl: window.location.origin + `/payment/checkout/success`,
                cancelUrl: window.location.origin + `/payment/checkout/cancel`
            };

            // Create subscription with improved error handling
            let session: SessionResponse;
            try {
                session = await createSubscription(subscriptionRequest);

                if (!session || !session.checkoutUrl) {
                    throw new Error('Server returned empty/invalid session response');
                }

                // Save current transaction state to session storage for recovery
                sessionStorage.setItem('pendingSubscription', JSON.stringify({
                    session,
                    timestamp: Date.now(),
                    amount: formData.amount,
                    currency: formData.currency
                }));

                console.log(`Sessionv ${session.sessionId} created successfully. Redirecting to:`, session.checkoutUrl);

                // Redirect to checkout
                window.location.href = session.checkoutUrl;

            } catch (subscriptionError: any) {
                console.error('Subscription creation error:', subscriptionError);
                // Format user-friendly error message
                const errorMessage = formatApiError(subscriptionError);
                throw new Error(`Subscription creation failed: ${errorMessage}`);
            }
        } catch (error: any) {
            console.error('Error in subscription creation flow:', error);

            // Show detailed error to user
            setError(error.message || 'Failed to create subscription. Please try again.');

            // Scroll to error message for visibility
            window.scrollTo({ top: 0, behavior: 'smooth' });

            setPaymentProcessing(false);
        }
    };

    // If not authenticated, show a message
    if (!isAuthenticated || !user) {
        return (
            <>
                <div className="min-h-screen bg-gray-50 flex justify-center items-center p-4">
                    <div className="bg-white rounded-lg shadow-lg p-8 max-w-md w-full text-center">
                        <h1 className="text-2xl font-bold text-gray-800 mb-4">Authentication Required</h1>
                        <p className="text-gray-600 mb-6">
                            Please log in to create a subscription.
                        </p>
                        <button
                            onClick={() => navigate('/auth', { state: { from: '/subscription/new' } })}
                            className="px-6 py-3 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors"
                        >
                            Go to Login
                        </button>
                    </div>
                </div>
            </>
        );
    }

    // Show loading state
    if (isLoading) {
        return (
            <>
                <div className="min-h-screen bg-gray-50 flex justify-center items-center">
                    <div className="text-center">
                        <div className="w-16 h-16 border-4 border-blue-500 border-t-transparent rounded-full animate-spin mx-auto mb-4"></div>
                        <p className="text-gray-500">Loading investment options...</p>
                    </div>
                </div>
            </>
        );
    }

    return (
        <>
            <div className="min-h-screen bg-gray-50 py-12 px-4 sm:px-6 lg:px-8">
                <div className="max-w-4xl mx-auto">
                    {/* Page header */}
                    <div className="text-center mb-8">
                        <h1 className="text-3xl font-bold text-gray-900">Create Your Investment Plan</h1>
                        <p className="mt-2 text-lg text-gray-600">
                            Set up regular investments in your preferred cryptocurrencies
                        </p>
                    </div>

                    {/* Progress indicator */}
                    <div className="mb-10">
                        <div className="flex justify-between">
                            <div className="text-center flex-1">
                                <div className={`w-10 h-10 mx-auto rounded-full flex items-center justify-center ${currentStep >= 1 ? 'bg-blue-500 text-white' : 'bg-gray-200 text-gray-500'
                                    }`}>
                                    1
                                </div>
                                <p className="mt-2 text-sm font-medium">Plan Details</p>
                            </div>
                            <div className="text-center flex-1">
                                <div className={`w-10 h-10 mx-auto rounded-full flex items-center justify-center ${currentStep >= 2 ? 'bg-blue-500 text-white' : 'bg-gray-200 text-gray-500'
                                    }`}>
                                    2
                                </div>
                                <p className="mt-2 text-sm font-medium">Asset Allocation</p>
                            </div>
                            <div className="text-center flex-1">
                                <div className={`w-10 h-10 mx-auto rounded-full flex items-center justify-center ${currentStep >= 3 ? 'bg-blue-500 text-white' : 'bg-gray-200 text-gray-500'
                                    }`}>
                                    3
                                </div>
                                <p className="mt-2 text-sm font-medium">Review & Confirm</p>
                            </div>
                        </div>
                        <div className="relative mt-3">
                            <div className="absolute inset-0 flex items-center" aria-hidden="true">
                                <div className="w-full border-t border-gray-300"></div>
                            </div>
                            <div className="relative flex justify-between">
                                <div className={`w-0 ${currentStep >= 2 ? 'w-1/2' : ''} transition-all duration-500 h-1 bg-blue-500`}></div>
                                <div className={`w-0 ${currentStep >= 3 ? 'w-1/2' : ''} transition-all duration-500 h-1 bg-blue-500`}></div>
                            </div>
                        </div>
                    </div>

                    {/* Main content area */}
                    <div className="bg-white shadow rounded-lg p-6 md:p-8">
                        {/* Enhanced Error display */}
                        <ErrorFeedback
                            error={error}
                            onDismiss={() => setError(null)}
                        />

                        {/* Validation errors */}
                        {validationErrors.length > 0 && (
                            <div className="bg-yellow-50 border-l-4 border-yellow-400 p-4 mb-6">
                                <div className="flex">
                                    <div className="flex-shrink-0">
                                        <svg className="h-5 w-5 text-yellow-400" viewBox="0 0 20 20" fill="currentColor">
                                            <path fillRule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clipRule="evenodd" />
                                        </svg>
                                    </div>
                                    <div className="ml-3">
                                        <h3 className="text-sm font-medium text-yellow-800">
                                            Please fix the following issues:
                                        </h3>
                                        <ul className="mt-1 text-sm text-yellow-700 list-disc list-inside">
                                            {validationErrors.map((err, index) => (
                                                <li key={index}>{err}</li>
                                            ))}
                                        </ul>
                                    </div>
                                </div>
                            </div>
                        )}

                        <form onSubmit={handleSubmit}>
                            {/* Step 1: Plan Details */}
                            {currentStep === 1 && (
                                <PlanDetailsStep
                                    formData={formData}
                                    updateFormData={updateFormData}
                                />
                            )}

                            {/* Step 2: Asset Allocation */}
                            {currentStep === 2 && (
                                <AssetAllocationStep
                                    formData={formData}
                                    updateFormData={updateFormData}
                                    availableAssets={availableAssets}
                                    isLoading={isLoading}
                                    error={error}
                                />
                            )}

                            {/* Step 3: Review */}
                            {currentStep === 3 && (
                                <ReviewStep
                                    formData={formData}
                                />
                            )}

                            {/* Navigation buttons */}
                            <div className="mt-8 flex justify-between">
                                {currentStep > 1 ? (
                                    <button
                                        type="button"
                                        onClick={goToPreviousStep}
                                        className="px-6 py-3 bg-gray-200 text-gray-800 rounded-lg hover:bg-gray-300 transition-colors"
                                        disabled={paymentProcessing}
                                    >
                                        Back
                                    </button>
                                ) : (
                                    <button
                                        type="button"
                                        onClick={() => navigate('/dashboard')}
                                        className="px-6 py-3 bg-gray-200 text-gray-800 rounded-lg hover:bg-gray-300 transition-colors"
                                        disabled={paymentProcessing}
                                    >
                                        Cancel
                                    </button>
                                )}

                                <button
                                    type="submit"
                                    className={`px-6 py-3 ${currentStep === 3 ? 'bg-green-600 hover:bg-green-700' : 'bg-blue-600 hover:bg-blue-700'} text-white rounded-lg transition-colors disabled:bg-opacity-70 disabled:cursor-not-allowed`}
                                    disabled={paymentProcessing || (currentStep === 2 && formData.allocations.length === 0)}
                                >
                                    {paymentProcessing ? (
                                        <div className="flex items-center">
                                            <div className="w-5 h-5 border-2 border-white border-t-transparent rounded-full animate-spin mr-2"></div>
                                            Processing...
                                        </div>
                                    ) : currentStep < 3 ? (
                                        'Continue'
                                    ) : (
                                        'Confirm & Pay'
                                    )}
                                </button>
                            </div>
                        </form>
                    </div>

                    {/* Help section */}
                    <div className="mt-8 text-center">
                        <div className="text-sm text-gray-500">
                            <p>Having trouble? <button type="button" onClick={() => window.open('/help/subscription-creation', '_blank')} className="text-blue-600 hover:underline">View Help Guide</button> or <button type="button" onClick={() => window.open('/contact', '_blank')} className="text-blue-600 hover:underline">Contact Support</button></p>
                        </div>
                    </div>
                </div>
            </div>
        </>
    );
};

const SubscriptionCreationPage: React.FC = () => {
    return (
        <>
            <Navbar />
            <div className="relative top-5">
                <SubscriptionCreationPageContent />
            </div>
        </>
    )
};

export default SubscriptionCreationPage;