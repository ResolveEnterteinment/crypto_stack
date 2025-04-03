// src/pages/SubscriptionCreationPage.tsx
import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import Navbar from '../components/Navbar';
import PlanDetailsStep from '../components/Subscription/PlanDetailsStep';
import AssetAllocationStep from '../components/Subscription/AssetAllocationStep';
import ReviewStep from '../components/Subscription/ReviewStep';
import { getSupportedAssets } from '../services/asset';
import { createSubscription } from '../services/subscription';
import { initiatePayment } from '../services/payment';
import IAsset from '../interfaces/IAsset';
import IAllocation from '../interfaces/IAllocation';

const SubscriptionCreationPage: React.FC = () => {
    const navigate = useNavigate();
    const { user, isAuthenticated } = useAuth();
    const [currentStep, setCurrentStep] = useState<number>(1);
    const [isLoading, setIsLoading] = useState<boolean>(true);
    const [error, setError] = useState<string | null>(null);
    const [availableAssets, setAvailableAssets] = useState<IAsset[]>([]);
    const [paymentProcessing, setPaymentProcessing] = useState<boolean>(false);

    // Form data state with default values
    const [formData, setFormData] = useState({
        userId: user?.id || '',
        interval: 'MONTHLY',
        amount: 100,
        currency: 'USD',
        endDate: null as Date | null,
        allocations: [] as Omit<IAllocation, 'id'>[]
    });

    // Load available assets on component mount
    useEffect(() => {
        const loadAssets = async () => {
            try {
                setIsLoading(true);
                const assets = await getSupportedAssets();
                setAvailableAssets(assets);
                setIsLoading(false);
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
            setCurrentStep(currentStep + 1);
            window.scrollTo(0, 0);
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

        switch (currentStep) {
            case 1: // Plan details
                if (!formData.interval) {
                    setError('Please select an investment frequency');
                    return false;
                }
                if (!formData.amount || formData.amount <= 0) {
                    setError('Please enter a valid amount');
                    return false;
                }
                break;
            case 2: // Asset allocation
                if (formData.allocations.length === 0) {
                    setError('Please add at least one asset allocation');
                    return false;
                }

                // Check if allocations sum to 100%
                const totalPercent = formData.allocations.reduce(
                    (sum, allocation) => sum + allocation.percentAmount, 0
                );

                if (Math.abs(totalPercent - 100) > 0.01) {
                    setError('Asset allocations must sum to 100%');
                    return false;
                }
                break;
            case 3: // Review
                // Nothing to validate here, handled in submit
                break;
            default:
                break;
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

            // Create subscription
            const subscriptionId = await createSubscription({
                userId: formData.userId,
                allocations: formData.allocations,
                interval: formData.interval,
                amount: formData.amount,
                currency: formData.currency,
                endDate: formData.endDate
            });

            if (!subscriptionId) {
                throw new Error('Failed to create subscription');
            }

            // Initialize payment
            const checkoutUrl = await initiatePayment({
                subscriptionId,
                userId: formData.userId,
                amount: formData.amount,
                currency: formData.currency,
                isRecurring: formData.interval !== 'ONCE'
            });

            // Redirect to checkout
            window.location.href = checkoutUrl;
        } catch (error: any) {
            console.error('Error creating subscription:', error);
            setError(error.message || 'Failed to create subscription. Please try again.');
            setPaymentProcessing(false);
        }
    };

    // If not authenticated, show a message
    if (!isAuthenticated || !user) {
        return (
            <>
                <Navbar
                    showProfile={() => navigate('/profile')}
                    showSettings={() => navigate('/settings')}
                    logout={() => { }}
                />
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
                <Navbar
                    showProfile={() => navigate('/profile')}
                    showSettings={() => navigate('/settings')}
                    logout={() => { }}
                />
                <div className="min-h-screen bg-gray-50 flex justify-center items-center">
                    <div className="text-center">
                        <div className="w-16 h-16 border-4 border-blue-500 border-t-transparent rounded-full animate-spin mx-auto mb-4"></div>
                        <p className="text-gray-500">Loading...</p>
                    </div>
                </div>
            </>
        );
    }

    return (
        <>
            <Navbar
                showProfile={() => navigate('/profile')}
                showSettings={() => navigate('/settings')}
                logout={() => { }}
            />
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
                        {/* Error display */}
                        {error && (
                            <div className="bg-red-50 border-l-4 border-red-500 p-4 mb-6">
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
                                    className="px-6 py-3 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors disabled:bg-blue-300"
                                    disabled={paymentProcessing}
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
                </div>
            </div>
        </>
    );
};

export default SubscriptionCreationPage;