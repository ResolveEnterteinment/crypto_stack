// src/components/DevTools/ApiTestPanel.tsx
import React, { useState } from 'react';
import api from '../../services/api';

// Array of sample common endpoints
const COMMON_ENDPOINTS = [
    { name: 'Create Subscription', method: 'POST', path: '/Subscription/new' },
    { name: 'Get Assets', method: 'GET', path: '/Asset/supported' },
    { name: 'Get Subscriptions', method: 'GET', path: '/Subscription' },
    { name: 'Initiate Payment', method: 'POST', path: '/Payment/create-checkout-session' }
];

// Sample request bodies for common endpoints
const REQUEST_TEMPLATES: Record<string, any> = {
    '/Subscription/new': {
        userId: "user_guid_here",
        allocations: [
            {
                assetId: "asset_guid_here",
                percentAmount: 100
            }
        ],
        interval: "MONTHLY",
        amount: 100,
        currency: "USD",
        endDate: null
    },
    '/Payment/create-checkout-session': {
        subscriptionId: "subscription_guid_here",
        userId: "user_guid_here",
        amount: 100,
        currency: "USD",
        isRecurring: true,
        returnUrl: window.location.origin + "/payment/success",
        cancelUrl: window.location.origin + "/payment/cancel"
    }
};

const ApiTestPanel: React.FC = () => {
    const [expanded, setExpanded] = useState(false);
    const [selectedEndpoint, setSelectedEndpoint] = useState('');
    const [method, setMethod] = useState('GET');
    const [url, setUrl] = useState('');
    const [requestBody, setRequestBody] = useState('');
    const [headers, setHeaders] = useState('{\n  "Content-Type": "application/json"\n}');
    const [response, setResponse] = useState<any>(null);
    const [error, setError] = useState<string | null>(null);
    const [isLoading, setIsLoading] = useState(false);

    // Only show in development
    if (process.env.NODE_ENV === 'production') {
        return null;
    }

    const handleEndpointSelect = (endpoint: string, method: string) => {
        setSelectedEndpoint(endpoint);
        setMethod(method);
        setUrl(endpoint);

        // Set template request body if available
        if (endpoint in REQUEST_TEMPLATES) {
            setRequestBody(JSON.stringify(REQUEST_TEMPLATES[endpoint], null, 2));
        } else {
            setRequestBody('');
        }
    };

    const handleSendRequest = async () => {
        setIsLoading(true);
        setError(null);
        setResponse(null);

        try {
            // Parse JSON values
            const parsedHeaders = JSON.parse(headers);
            const parsedBody = method !== 'GET' && requestBody ? JSON.parse(requestBody) : undefined;

            // Make the request using our API service
            let result;

            switch (method.toLowerCase()) {
                case 'get':
                    result = await api.get(url, { headers: parsedHeaders });
                    break;
                case 'post':
                    result = await api.post(url, parsedBody, { headers: parsedHeaders });
                    break;
                case 'put':
                    result = await api.put(url, parsedBody, { headers: parsedHeaders });
                    break;
                case 'delete':
                    result = await api.delete(url, { headers: parsedHeaders });
                    break;
                default:
                    throw new Error(`Unsupported method: ${method}`);
            }

            setResponse({
                status: result.status,
                statusText: result.statusText,
                headers: result.headers,
                data: result.data
            });
        } catch (err: any) {
            setError(err.message);

            // Set response from error if available
            if (err.response) {
                setResponse({
                    status: err.response.status,
                    statusText: err.response.statusText,
                    headers: err.response.headers,
                    data: err.response.data
                });
            }
        } finally {
            setIsLoading(false);
        }
    };

    if (!expanded) {
        return (
            <div className="fixed bottom-4 right-4 z-50">
                <button
                    onClick={() => setExpanded(true)}
                    className="bg-purple-600 hover:bg-purple-700 text-white font-medium px-4 py-2 rounded shadow-lg"
                >
                    API Tester
                </button>
            </div>
        );
    }

    return (
        <div className="fixed inset-0 bg-black bg-opacity-50 z-50 flex items-center justify-center p-4">
            <div className="bg-white rounded-lg shadow-xl w-full max-w-4xl max-h-[90vh] overflow-auto">
                <div className="p-4 border-b border-gray-200 flex justify-between items-center bg-gray-50">
                    <h2 className="text-lg font-semibold text-gray-800">API Test Panel</h2>
                    <button
                        onClick={() => setExpanded(false)}
                        className="text-gray-500 hover:text-gray-700"
                    >
                        <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 18L18 6M6 6l12 12" />
                        </svg>
                    </button>
                </div>

                <div className="p-4 grid grid-cols-1 md:grid-cols-3 gap-4">
                    <div className="md:col-span-1 border-r pr-4">
                        <h3 className="font-medium text-gray-700 mb-2">Common Endpoints</h3>
                        <div className="space-y-2">
                            {COMMON_ENDPOINTS.map((endpoint) => (
                                <button
                                    key={endpoint.path}
                                    onClick={() => handleEndpointSelect(endpoint.path, endpoint.method)}
                                    className={`block w-full text-left px-3 py-2 rounded text-sm ${selectedEndpoint === endpoint.path
                                            ? 'bg-purple-100 text-purple-700'
                                            : 'hover:bg-gray-100'
                                        }`}
                                >
                                    <span className="font-mono text-xs bg-gray-200 px-1 rounded mr-2">
                                        {endpoint.method}
                                    </span>
                                    {endpoint.name}
                                </button>
                            ))}
                        </div>

                        <div className="mt-6">
                            <h3 className="font-medium text-gray-700 mb-2">Custom Request</h3>
                            <div className="mb-3">
                                <label className="block text-sm font-medium text-gray-700 mb-1">
                                    Method:
                                </label>
                                <select
                                    value={method}
                                    onChange={(e) => setMethod(e.target.value)}
                                    className="w-full p-2 border border-gray-300 rounded"
                                >
                                    <option value="GET">GET</option>
                                    <option value="POST">POST</option>
                                    <option value="PUT">PUT</option>
                                    <option value="DELETE">DELETE</option>
                                </select>
                            </div>

                            <div className="mb-3">
                                <label className="block text-sm font-medium text-gray-700 mb-1">
                                    URL:
                                </label>
                                <input
                                    type="text"
                                    value={url}
                                    onChange={(e) => setUrl(e.target.value)}
                                    placeholder="/endpoint"
                                    className="w-full p-2 border border-gray-300 rounded"
                                />
                            </div>

                            <div className="mb-3">
                                <label className="block text-sm font-medium text-gray-700 mb-1">
                                    Headers (JSON):
                                </label>
                                <textarea
                                    value={headers}
                                    onChange={(e) => setHeaders(e.target.value)}
                                    rows={3}
                                    className="w-full p-2 border border-gray-300 rounded font-mono text-sm"
                                />
                            </div>

                            {method !== 'GET' && (
                                <div className="mb-3">
                                    <label className="block text-sm font-medium text-gray-700 mb-1">
                                        Request Body (JSON):
                                    </label>
                                    <textarea
                                        value={requestBody}
                                        onChange={(e) => setRequestBody(e.target.value)}
                                        rows={6}
                                        className="w-full p-2 border border-gray-300 rounded font-mono text-sm"
                                        placeholder="{}"
                                    />
                                </div>
                            )}

                            <button
                                onClick={handleSendRequest}
                                disabled={isLoading || !url}
                                className="w-full bg-purple-600 hover:bg-purple-700 text-white px-4 py-2 rounded disabled:opacity-50 disabled:cursor-not-allowed"
                            >
                                {isLoading ? 'Sending...' : 'Send Request'}
                            </button>
                        </div>
                    </div>

                    <div className="md:col-span-2">
                        <h3 className="font-medium text-gray-700 mb-2">Response</h3>

                        {isLoading && (
                            <div className="flex justify-center items-center h-40">
                                <div className="animate-spin rounded-full h-10 w-10 border-4 border-purple-500 border-t-transparent"></div>
                            </div>
                        )}

                        {error && !response && (
                            <div className="p-4 bg-red-50 border border-red-200 rounded mb-4">
                                <p className="text-red-600 font-medium">Error:</p>
                                <p className="font-mono text-sm break-all">{error}</p>
                            </div>
                        )}

                        {response && (
                            <div className="border border-gray-200 rounded">
                                <div className="flex justify-between p-3 bg-gray-50 border-b border-gray-200">
                                    <span className={`font-medium ${response.status < 400 ? 'text-green-600' : 'text-red-600'
                                        }`}>
                                        Status: {response.status} {response.statusText}
                                    </span>
                                </div>

                                <div className="p-3 border-b border-gray-200">
                                    <p className="font-medium text-sm mb-1">Headers:</p>
                                    <div className="bg-gray-50 p-2 rounded">
                                        <pre className="text-xs font-mono overflow-auto max-h-20">
                                            {JSON.stringify(response.headers, null, 2)}
                                        </pre>
                                    </div>
                                </div>

                                <div className="p-3">
                                    <p className="font-medium text-sm mb-1">Response Body:</p>
                                    <div className="bg-gray-50 p-2 rounded">
                                        <pre className="text-xs font-mono overflow-auto max-h-96">
                                            {JSON.stringify(response.data, null, 2)}
                                        </pre>
                                    </div>
                                </div>
                            </div>
                        )}

                        {!isLoading && !response && !error && (
                            <div className="flex flex-col items-center justify-center h-64 text-gray-500">
                                <svg className="w-16 h-16 mb-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M8 9l3 3-3 3m5 0h3M5 20h14a2 2 0 002-2V6a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z" />
                                </svg>
                                <p>Send a request to see the response here</p>
                            </div>
                        )}
                    </div>
                </div>
            </div>
        </div>
    );
};

export default ApiTestPanel;