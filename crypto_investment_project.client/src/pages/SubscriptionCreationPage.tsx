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
import ICreateSubscriptionRequest from '../interfaces/ICreateSubscriptionRequest';

const SubscriptionCreationPage: React.FC = () => {
    const navigate = useNavigate();
    const { user } = useAuth();

    // Form state
    const [formData, setFormData] = useState({
        interval: 'MONTHLY',
        amount: 100,
        currency: 'USD',
        endDate: null,
        allocations: new Array <IAllocation>()
    });

    // UI state
    const [step, setStep] = useState(1);
    const [availableAssets, setAvailableAssets] = useState<IAsset[] | null>(null);
    const [loading, setLoading] = useState(true);
    const [submitting, setSubmitting] = useState(false);
    const [error, setError] = useState < String | null> (null);

    // Fetch available assets when component mounts
    useEffect(() => {
        if (!user || !user.id) {
            //navigate('/auth');
            return;
        }

        const fetchAssets = async () => {
            try {
                const assets = await getSupportedAssets();
                setAvailableAssets(assets);
                setLoading(false);
            } catch (err) {
                console.error('Error fetching available assets:', err);
                setError('Failed to load available assets. Please try again.');
                setLoading(false);
            }
        };

        fetchAssets();
    }, [user]);

    // Form update handler
    const updateFormData = (fieldName:string, value: object) => {
        setFormData(prev => ({
            ...prev,
            [fieldName]: value
        }));
    };

    // Step navigation
    const nextStep = () => {
        // Validate current step before proceeding
        if (step === 1) {
            if (!formData.interval || formData.amount <= 0) {
                setError('Please fill in all required fields with valid values.');
                return;
            }
            setError(null);
        } else if (step === 2) {
            // Validate allocations
            if (!formData.allocations.length) {
                setError('Please select at least one asset for your portfolio.');
                return;
            }

            const totalPercentage = formData.allocations.reduce(
                (sum, allocation) => sum + allocation.percentAmount, 0
            );

            if (Math.abs(totalPercentage - 100) > 0.01) {
                setError('Asset allocations must total exactly 100%.');
                return;
            }
            setError(null);
        }

        setStep(curr => Math.min(curr + 1, 3));
    };

    const prevStep = () => {
        setStep(curr => Math.max(curr - 1, 1));
        setError(null);
    };

    // Form submission
    const handleSubmit = async () => {
        if (!user?.id) {
            setError('You must be logged in to create a subscription');
            return;
        }

        try {
            setSubmitting(true);
            setError(null);

            // Create subscription in the backend
            const subscriptionData: ICreateSubscriptionRequest = {
                userId: user.id,
                interval: formData.interval,
                amount: formData.amount,
                currency: formData.currency,
                endDate: formData.endDate,
                allocations: formData.allocations
            };

            const subscriptionId = await createSubscription(subscriptionData);

            // Initiate payment with Stripe
            const paymentUrl = await initiatePayment({
                subscriptionId,
                userId: user.id,
                amount: formData.amount,
                currency: formData.currency,
                isRecurring: formData.interval !== 'ONCE'
            });

            // Redirect to Stripe checkout
            window.location.href = paymentUrl;
        } catch (err:Error|any) {
            console.error('Error creating subscription:', err);
            setError(err.message || 'Failed to create subscription. Please try again.');
            setSubmitting(false);
        }
    };

    // UI for loading state
    if (loading) {
        return (
            <>
                <Navbar
                    showProfile={() => navigate('/profile')}
                    showSettings={() => navigate('/settings')}
                    logout={() => { }}
                />
                <div className="min-h-screen bg-gray-50 flex justify-center items-center p-4">
                    <div className="text-center">
                        <div className="w-16 h-16 border-4 border-blue-500 border-t-transparent rounded-full animate-spin mx-auto mb-4"></div>
                        <p className="text-gray-500">Loading available assets...</p>
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
                <div className="max-w-3xl mx-auto">
                    <div className="text-center mb-8">
                        <h1 className="text-3xl font-bold text-gray-900">Create Your Investment Plan</h1>
                        <p className="mt-2 text-lg text-gray-600">
                            Customize your crypto portfolio with regular or one-time investments
                        </p>
                    </div>

                    {/* Step progress indicator */}
                    <div className="mb-10">
                        <div className="flex items-center justify-between mb-2">
                            <div className={`flex-1 h-1 ${step >= 1 ? 'bg-blue-500' : 'bg-gray-200'}`}></div>
                            <div className={`w-8 h-8 rounded-full flex items-center justify-center ${step >= 1 ? 'bg-blue-500 text-white' : 'bg-gray-200 text-gray-500'
                                }`}>
                                1
                            </div>
                            <div className={`flex-1 h-1 ${step >= 2 ? 'bg-blue-500' : 'bg-gray-200'}`}></div>
                            <div className={`w-8 h-8 rounded-full flex items-center justify-center ${step >= 2 ? 'bg-blue-500 text-white' : 'bg-gray-200 text-gray-500'
                                }`}>
                                2
                            </div>
                            <div className={`flex-1 h-1 ${step >= 3 ? 'bg-blue-500' : 'bg-gray-200'}`}></div>
                            <div className={`w-8 h-8 rounded-full flex items-center justify-center ${step >= 3 ? 'bg-blue-500 text-white' : 'bg-gray-200 text-gray-500'
                                }`}>
                                3
                            </div>
                            <div className={`flex-1 h-1 ${step >= 3 ? 'bg-blue-500' : 'bg-gray-200'}`}></div>
                        </div>
                        <div className="flex justify-between text-sm">
                            <div className={step >= 1 ? 'text-blue-600 font-medium' : 'text-gray-500'}>
                                Plan Details
                            </div>
                            <div className={step >= 2 ? 'text-blue-600 font-medium' : 'text-gray-500'}>
                                Asset Allocation
                            </div>
                            <div className={step >= 3 ? 'text-blue-600 font-medium' : 'text-gray-500'}>
                                Review & Confirm
                            </div>
                        </div>
                    </div>

                    {/* Error message */}
                    {error && (
                        <div className="bg-red-50 border-l-4 border-red-500 p-4 mb-6">
                            <div className="flex">
                                <div className="flex-shrink-0">
                                    <svg className="h-5 w-5 text-red-500" viewBox="0 0 20 20" fill="currentColor">
                                        <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clipRule="evenodd" />
                                    </svg>
                                </div>
                                <div className="ml-3">
                                    <p className="text-sm text-red-700">{error}</p>
                                </div>
                            </div>
                        </div>
                    )}

                    {/* Content card */}
                    <div className="bg-white shadow-lg rounded-lg overflow-hidden">
                        <div className="p-6 sm:p-8">
                            {/* Step 1: Plan Details */}
                            {step === 1 && (
                                <PlanDetailsStep
                                    formData={formData}
                                    updateFormData={updateFormData}
                                />
                            )}

                            {/* Step 2: Asset Allocation */}
                            {step === 2 && (
                                <AssetAllocationStep
                                    formData={formData}
                                    updateFormData={updateFormData}
                                    availableAssets={availableAssets}
                                />
                            )}

                            {/* Step 3: Review & Confirm */}
                            {step === 3 && (
                                <ReviewStep
                                    formData={formData}
                                />
                            )}

                            {/* Navigation buttons */}
                            <div className="flex justify-between mt-8 pt-6 border-t border-gray-200">
                                {step > 1 ? (
                                    <button
                                        type="button"
                                        onClick={prevStep}
                                        className="inline-flex items-center px-4 py-2 border border-gray-300 shadow-sm text-base font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500"
                                    >
                                        <svg className="mr-2 h-5 w-5" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor">
                                            <path fillRule="evenodd" d="M9.707 14.707a1 1 0 01-1.414 0l-4-4a1 1 0 010-1.414l4-4a1 1 0 011.414 1.414L7.414 9H15a1 1 0 110 2H7.414l2.293 2.293a1 1 0 010 1.414z" clipRule="evenodd" />
                                        </svg>
                                        Back
                                    </button>
                                ) : (
                                    <button
                                        type="button"
                                        onClick={() => navigate(-1)}
                                        className="inline-flex items-center px-4 py-2 border border-gray-300 shadow-sm text-base font-medium rounded-md text-gray-700 bg-white hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500"
                                    >
                                        Cancel
                                    </button>
                                )}

                                {step < 3 ? (
                                    <button
                                        type="button"
                                        onClick={nextStep}
                                        className="inline-flex items-center px-6 py-3 border border-transparent text-base font-medium rounded-md shadow-sm text-white bg-blue-600 hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500"
                                    >
                                        Next
                                        <svg className="ml-2 h-5 w-5" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor">
                                            <path fillRule="evenodd" d="M10.293 5.293a1 1 0 011.414 0l4 4a1 1 0 010 1.414l-4 4a1 1 0 01-1.414-1.414L12.586 11H5a1 1 0 110-2h7.586l-2.293-2.293a1 1 0 010-1.414z" clipRule="evenodd" />
                                        </svg>
                                    </button>
                                ) : (
                                    <button
                                        type="button"
                                        onClick={handleSubmit}
                                        disabled={submitting}
                                        className={`inline-flex items-center px-6 py-3 border border-transparent text-base font-medium rounded-md shadow-sm text-white ${submitting ? 'bg-blue-400 cursor-not-allowed' : 'bg-blue-600 hover:bg-blue-700'} focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500`}
                                    >
                                        {submitting ? (
                                            <>
                                                <svg className="animate-spin -ml-1 mr-3 h-5 w-5 text-white" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                                                    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                                                    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                                                </svg>
                                                Processing...
                                            </>
                                        ) : (
                                            <>
                                                Create Plan & Proceed to Payment
                                                <svg className="ml-2 h-5 w-5" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor">
                                                    <path fillRule="evenodd" d="M16.707 5.293a1 1 0 010 1.414l-8 8a1 1 0 01-1.414 0l-4-4a1 1 0 011.414-1.414L8 12.586l7.293-7.293a1 1 0 011.414 0z" clipRule="evenodd" />
                                                </svg>
                                            </>
                                        )}
                                    </button>
                                )}
                            </div>
                        </div>
                    </div>

                    {/* Help information */}
                    <div className="mt-6 bg-blue-50 rounded-lg p-4">
                        <h3 className="text-sm font-medium text-blue-800">How it works</h3>
                        <ul className="mt-2 text-sm text-blue-700 space-y-1">
                            <li className="flex items-start">
                                <span className="mr-2">•</span>
                                <span>Choose your investment frequency and amount</span>
                            </li>
                            <li className="flex items-start">
                                <span className="mr-2">•</span>
                                <span>Select which cryptocurrencies to include in your portfolio</span>
                            </li>
                            <li className="flex items-start">
                                <span className="mr-2">•</span>
                                <span>Review your plan and proceed to secure payment</span>
                            </li>
                            <li className="flex items-start">
                                <span className="mr-2">•</span>
                                <span>Your investment plan will start immediately after payment</span>
                            </li>
                        </ul>
                    </div>
                </div>
            </div>
        </>
    );
};

export default SubscriptionCreationPage;