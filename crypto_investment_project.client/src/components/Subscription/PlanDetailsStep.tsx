// src/components/Subscription/PlanDetailsStep.jsx
import React from 'react';
import { Allocation } from '../../types/subscription';

const INTERVALS = [
    { value: 'ONCE', label: 'One-time payment' },
    { value: 'DAILY', label: 'Daily' },
    { value: 'WEEKLY', label: 'Weekly' },
    { value: 'MONTHLY', label: 'Monthly' },
    { value: 'YEARLY', label: 'Yearly' }
];

interface PlanDetailsStepProps {
    formData: {
        interval: string;
        amount: number;
        endDate: Date | null;
    };
    updateFormData: any;
}

const PlanDetailsStep: React.FC<PlanDetailsStepProps> = ({ formData, updateFormData }) => {
    const handleIntervalChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
        updateFormData('interval', e.target.value);
    };

    const handleAmountChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const value = parseFloat(e.target.value);
        if (!isNaN(value) && value > 0) {
            updateFormData('amount', value);
        }
    };

    const handleEndDateChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        const date = e.target.value ? new Date(e.target.value) : null;
        updateFormData('endDate', date);
    };

    // Get minimum date (tomorrow)
    const tomorrow = new Date();
    tomorrow.setDate(tomorrow.getDate() + 1);
    const minDate = tomorrow.toISOString().split('T')[0];

    return (
        <div className="space-y-6">
            <div>
                <h2 className="text-2xl font-semibold text-gray-800 mb-4">Investment Plan Details</h2>
                <p className="text-gray-600 mb-6">
                    Choose how often you want to invest and how much.
                </p>
            </div>

            <div>
                <label className="block text-gray-700 font-medium mb-2">
                    Investment Frequency
                </label>
                <select
                    value={formData.interval}
                    onChange={handleIntervalChange}
                    className="w-full p-3 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:outline-none"
                    required
                >
                    {INTERVALS.map(option => (
                        <option key={option.value} value={option.value}>
                            {option.label}
                        </option>
                    ))}
                </select>
                <p className="mt-1 text-sm text-gray-500">
                    How often would you like to invest? Choose "One-time payment" for a single investment.
                </p>
            </div>

            <div>
                <label className="block text-gray-700 font-medium mb-2">
                    Investment Amount (USD)
                </label>
                <div className="relative">
                    <span className="absolute left-3 top-1/2 transform -translate-y-1/2 text-gray-500">
                        $
                    </span>
                    <input
                        type="number"
                        min="10"
                        step="0.01"
                        value={formData.amount}
                        onChange={handleAmountChange}
                        className="w-full p-3 pl-8 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:outline-none"
                        required
                    />
                </div>
                <p className="mt-1 text-sm text-gray-500">
                    Minimum investment: $10
                </p>
            </div>

            {formData.interval !== 'ONCE' && (
                <div>
                    <label className="block text-gray-700 font-medium mb-2">
                        End Date (Optional)
                    </label>
                    <input
                        type="date"
                        value={formData.endDate ? formData.endDate.toISOString().split('T')[0] : ''}
                        onChange={handleEndDateChange}
                        min={minDate}
                        className="w-full p-3 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 focus:outline-none"
                    />
                    <p className="mt-1 text-sm text-gray-500">
                        Leave empty for ongoing subscription. You can cancel anytime.
                    </p>
                </div>
            )}

            {formData.interval !== 'ONCE' && (
                <div className="bg-gray-50 p-4 rounded-lg">
                    <h3 className="font-medium text-gray-700 mb-2">Recurring Payment Summary</h3>
                    <div className="grid grid-cols-2 gap-4">
                        <div>
                            <p className="text-sm text-gray-500">Frequency</p>
                            <p className="font-medium">
                                {INTERVALS.find(i => i.value === formData.interval)?.label}
                            </p>
                        </div>
                        <div>
                            <p className="text-sm text-gray-500">Per Payment</p>
                            <p className="font-medium">${formData.amount.toFixed(2)}</p>
                        </div>
                        <div>
                            <p className="text-sm text-gray-500">End Date</p>
                            <p className="font-medium">
                                {formData.endDate
                                    ? formData.endDate.toLocaleDateString()
                                    : 'Ongoing (until canceled)'}
                            </p>
                        </div>
                        <div>
                            <p className="text-sm text-gray-500">Payment Method</p>
                            <p className="font-medium">Credit/Debit Card</p>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
};

export default PlanDetailsStep;