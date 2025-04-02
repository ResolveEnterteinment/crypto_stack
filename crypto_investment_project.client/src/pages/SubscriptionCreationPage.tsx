// src/pages/SubscriptionCreationPage.tsx
import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import Navbar from '../components/Navbar';
import AssetAllocationForm from '../components/Subscription/AssetAllocationForm';
import SubscriptionSummary from '../components/Subscription/SubscriptionSummary';
import { createSubscription } from '../services/subscription';
import { getAvailableAssets } from '../services/asset';
import IAsset from '../interfaces/IAsset';
import IAllocation from '../interfaces/IAllocation';
import { initiatePayment } from '../services/payment';

interface FormData {
    interval: string;
    amount: number;
    currency: string;
    endDate: Date | null;
    allocations: Omit<IAllocation, 'id'>[];
}

const intervals = [
    { value: 'ONCE', label: 'One-time payment' },
    { value: 'DAILY', label: 'Daily' },
    { value: 'WEEKLY', label: 'Weekly' },
    { value: 'MONTHLY', label: 'Monthly' }
];

const SubscriptionCreationPage: React.FC = () => {
    const navigate = useNavigate();
    const { user } = useAuth();

    // Form state
    const [formData, setFormData] = useState<FormData>({
        interval: 'MONTHLY',
        amount: 100,
        currency: 'USD',
        endDate: null,
        allocations: []
    });

    // UI state
    const [availableAssets, setAvailableAssets] = useState<IAsset[]>([]);
    const [loading, setLoading] = useState(true);
    const [submitting, setSubmitting] = useState(false);
    const [currentStep, setCurrentStep] = useState(1);
    const [error, setError] = useState<string | null>(null);

    // Fetch available assets when component mounts
    useEffect(() => {
        if (!user || !user.id) {
            navigate('/auth');
            return;
        }

        const fetchAssets = async () => {
            try {
                const assets = await getAvailableAssets();
                setAvailableAssets(assets);
                setLoading(false);
            } catch (err) {
                console.error('Error fetching available assets:', err);
                setError('Failed to load available cryptocurrencies. Please try again.');
                setLoading(false);
            }
        };

        fetchAssets();
    }, [user, navigate]);

    // Form change handlers
    const handleIntervalChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
        setFormData({ ...formData, interval: e.target.value });
    };

    const handleAmountChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const value = parseFloat(e.target.value);
        if (!isNaN(value) && value > 0) {
            setFormData({ ...formData, amount: value });
        }
    };

    const handleEndDateChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const date = e.target.value ? new Date(e.target.value) : null;
        setFormData({ ...formData, endDate: date });
    };

    const handleAllocationsChange = (allocations: Omit<IAllocation, 'id'>[]) => {
        setFormData({ ...formData, allocations });
    };

    // Form submission
    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();

        if (!user?.id) {
            setError('You must be logged in to create a subscription');
            return;
        }

        // Validate allocations total to 100%
        const totalPercentage = formData.allocations.reduce(
            (sum, allocation) => sum + allocation.percentAmount,
            0
        );

        if (Math.abs(totalPercentage - 100) > 0.01) {
            setError('Asset allocations must total exactly 100%');
            return;
        }

        // Validate we have at least one allocation
        if (formData.allocations.length === 0) {
            setError('You must select at least one asset');
            return;
        }

        try {
            setSubmitting(true);
            setError(null);

            // Create subscription in the backend
            const subscriptionData = {
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
        } catch (err: any) {
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
          showProfile= {() => navigate('/profile')}
showSettings = {() => navigate('/settings')}
logout = {() => { }} 
        />
    < div className = "min-h-screen bg-gray-50 flex justify-center items-center p-4" >
        <div className="text-center" >
            <div className="w-16 h-16 border-4 border-blue-500 border-t-transparent rounded-full animate-spin mx-auto mb-4" > </div>
                < p className = "text-gray-500" > Loading...</p>
                    </div>
                    </div>
                    </>
    );
  }

return (
    <>
    <Navbar 
        showProfile= {() => navigate('/profile')}
showSettings = {() => navigate('/settings')}
logout = {() => { }} 
      />
    < div className = "min-h-screen bg-gray-50 py-8 px-4 lg:px-10" >
        <div className="max-w-4xl mx-auto bg-white rounded-lg shadow-md p-6" >
            <h1 className="text-2xl font-bold mb-6" > Create Investment Plan </h1>

{/* Progress indicator */ }
<div className="mb-8" >
    <div className="flex items-center justify-between" >
        <div className={ `flex-1 h-2 ${currentStep >= 1 ? 'bg-blue-500' : 'bg-gray-200'}` }> </div>
            < div className = {`flex-1 h-2 ${currentStep >= 2 ? 'bg-blue-500' : 'bg-gray-200'}`}> </div>
                </div>
                < div className = "flex justify-between mt-2 text-sm" >
                    <div className={ currentStep >= 1 ? 'text-blue-500 font-medium' : 'text-gray-500' }> Plan Details </div>
                        < div className = { currentStep >= 2 ? 'text-blue-500 font-medium' : 'text-gray-500'}> Asset Allocation </div>
                            </div>
                            </div>

{/* Error message */ }
{
    error && (
        <div className="bg-red-100 border border-red-400 text-red-700 px-4 py-3 rounded mb-4" >
            <p>{ error } </p>
            </div>
          )
}

<form onSubmit={ handleSubmit }>
    {/* Step 1: Basic Plan Details */ }
{
    currentStep === 1 && (
        <div className="space-y-6" >
            <div>
            <label className="block text-gray-700 font-medium mb-2" > Investment Frequency </label>
                < select
    value = { formData.interval }
    onChange = { handleIntervalChange }
    className = "w-full p-3 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:outline-none"
    required
        >
    {
        intervals.map(option => (
            <option key= { option.value } value = { option.value } >
            { option.label }
            </option>
        ))
    }
        </select>
        </div>

        < div >
        <label className="block text-gray-700 font-medium mb-2" > Investment Amount(USD) </label>
            < div className = "relative" >
                <span className="absolute left-3 top-1/2 transform -translate-y-1/2 text-gray-500" > $ </span>
                    < input
    type = "number"
    min = "10"
    step = "0.01"
    value = { formData.amount }
    onChange = { handleAmountChange }
    className = "w-full p-3 pl-8 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:outline-none"
    required
        />
        </div>
        < p className = "text-sm text-gray-500 mt-1" > Minimum investment: $10 </p>
            </div>

    {
        formData.interval !== 'ONCE' && (
            <div>
            <label className="block text-gray-700 font-medium mb-2" > End Date(Optional) </label>
                < input
        type = "date"
        value = { formData.endDate ? formData.endDate.toISOString().split('T')[0] : '' }
        onChange = { handleEndDateChange }
        min = { new Date().toISOString().split('T')[0] }
        className = "w-full p-3 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:outline-none"
            />
            <p className="text-sm text-gray-500 mt-1" > Leave empty for ongoing subscription </p>
                </div>
                )}

    <div className="flex justify-end mt-8" >
        <button
                    type="button"
    onClick = {() => setCurrentStep(2)
}
className = "bg-blue-600 text-white py-3 px-6 rounded-lg hover:bg-blue-700 transition-colors"
    >
    Next: Choose Assets
        </button>
        </div>
        </div>
            )}

{/* Step 2: Asset Allocation */ }
{
    currentStep === 2 && (
        <div>
        <AssetAllocationForm 
                  availableAssets={ availableAssets }
    allocations = { formData.allocations }
    onChange = { handleAllocationsChange }
        />

        {
            formData.allocations.length > 0 && (
                <div className="mt-8 p-4 bg-gray-50 rounded-lg">
                    <h3 className="text-lg font-medium mb-4"> Your Plan Summary</ h3 >
        <SubscriptionSummary
                      interval={ formData.interval }
    amount = { formData.amount }
    currency = { formData.currency }
    endDate = { formData.endDate }
    allocations = { formData.allocations }
        />
        </div>
                )
}

<div className="flex justify-between mt-8" >
    <button
                    type="button"
onClick = {() => setCurrentStep(1)}
className = "bg-gray-200 text-gray-700 py-3 px-6 rounded-lg hover:bg-gray-300 transition-colors"
    >
    Back
    </button>
    < button
type = "submit"
disabled = { submitting || formData.allocations.length === 0}
className = {`${submitting || formData.allocations.length === 0
        ? 'bg-blue-400 cursor-not-allowed'
        : 'bg-blue-600 hover:bg-blue-700'
    } text-white py-3 px-6 rounded-lg transition-colors`}
                  >
    {
        submitting?(
                      <div className = "flex items-center" >
                <svg className="animate-spin -ml-1 mr-2 h-4 w-4 text-white" xmlns = "http://www.w3.org/2000/svg" fill = "none" viewBox = "0 0 24 24" >
                    <circle className="opacity-25" cx = "12" cy = "12" r = "10" stroke = "currentColor" strokeWidth = "4" > </circle>
                        < path className = "opacity-75" fill = "currentColor" d = "M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" > </path>
                            </svg>
                        Processing...
                      </ div >
                    ) : (
    'Create Investment Plan'
)}
</button>
    </div>
    </div>
            )}
</form>
    </div>
    </div>
    </>
  );
};

export default SubscriptionCreationPage;