import React, { useState } from 'react';

interface LogContextDisplayProps {
    context: Record<string, any> | string;
}

/**
 * Component to display log context data in a flexible, collapsible format
 */
const LogContextDisplay: React.FC<LogContextDisplayProps> = ({ context }) => {
    const [isExpanded, setIsExpanded] = useState(false);

    // Handle string context
    if (typeof context === 'string') {
        try {
            // Try to parse JSON string
            const parsed = JSON.parse(context);
            context = parsed;
        } catch (e) {
            // If it's not valid JSON, return simple pre display
            return (
                <pre className="text-xs bg-gray-50 p-2 rounded overflow-x-auto max-h-40 border border-gray-200">
                    {context.toString()}
                </pre>
            );
        }
    }

    // Convert context to array of key-value pairs
    const contextEntries = Object.entries(context);

    // Determine if we have complex values (objects or arrays)
    const hasComplexValues = contextEntries.some(
        ([_, value]) => typeof value === 'object' && value !== null
    );

    // Get formatted value for display
    const getFormattedValue = (value: any): string => {
        if (value === null) return 'null';
        if (value === undefined) return 'undefined';
        if (typeof value === 'object') {
            return JSON.stringify(value);
        }
        return String(value);
    };

    // Show a collapsed view with only simple key-value pairs
    const renderCollapsedView = () => {
        return (
            <div className="flex flex-wrap gap-2">
                {contextEntries
                    .filter(([_, value]) => typeof value !== 'object' || value === null)
                    .map(([key, value]) => (
                        <div
                            key={key}
                            className="bg-gray-100 px-3 py-1 rounded-md border border-gray-200 text-xs flex items-center"
                        >
                            <span className="font-medium text-gray-700 mr-2">{key}:</span>
                            <span className="text-gray-900 max-w-xs truncate">{getFormattedValue(value)}</span>
                        </div>
                    ))}
                {hasComplexValues && (
                    <button
                        onClick={() => setIsExpanded(true)}
                        className="bg-blue-100 hover:bg-blue-200 text-blue-700 px-3 py-1 rounded-md text-xs transition-colors"
                    >
                        Show more...
                    </button>
                )}
            </div>
        );
    };

    // Show detailed view with expandable complex values
    const renderDetailedView = () => {
        return (
            <div className="space-y-2">
                <div className="flex justify-between mb-2">
                    <span className="text-xs font-medium text-gray-500">
                        {contextEntries.length} properties
                    </span>
                    <button
                        onClick={() => setIsExpanded(false)}
                        className="text-xs text-blue-600 hover:text-blue-800"
                    >
                        Collapse
                    </button>
                </div>

                <div className="flex flex-wrap gap-2">
                    {contextEntries.map(([key, value]) => {
                        const isComplex = typeof value === 'object' && value !== null;

                        return (
                            <div
                                key={key}
                                className={`px-3 py-2 rounded-md border text-xs ${isComplex ? 'bg-blue-50 border-blue-200' : 'bg-gray-100 border-gray-200'
                                    }`}
                            >
                                <div className="font-medium text-gray-700 mb-1">{key}</div>
                                {isComplex ? (
                                    <div className="overflow-x-auto max-w-xs text-xs bg-white p-1 rounded">
                                        <pre className="whitespace-pre-wrap break-words">{JSON.stringify(value, null, 2)}</pre>
                                    </div>
                                ) : (
                                    <div className="text-gray-900">{getFormattedValue(value)}</div>
                                )}
                            </div>
                        );
                    })}
                </div>
            </div>
        );
    };

    return (
        <div className="bg-gray-50 p-2 rounded border border-gray-200">
            {isExpanded ? renderDetailedView() : renderCollapsedView()}
        </div>
    );
};

export default LogContextDisplay;