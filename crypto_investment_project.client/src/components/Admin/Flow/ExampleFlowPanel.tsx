import { Activity, AlertCircle, ArrowRight, CheckCircle, Clock, ExternalLink, Eye, Filter, GitBranch, Pause, Play, Search, Settings, X, XCircle, Zap } from 'lucide-react';
import React, { useMemo, useState } from 'react';

// Types
interface FlowStep {
    id: string;
    name: string;
    type: 'action' | 'condition' | 'trigger' | 'transform';
    status: 'completed' | 'running' | 'failed' | 'pending';
    description?: string;
    duration?: number;
    error?: string;
    config?: Record<string, any>;
}

interface TriggerInfo {
    flowId: string;
    flowName: string;
    timestamp: string;
    type: 'external' | 'scheduled' | 'manual';
}

interface Flow {
    id: string;
    name: string;
    description: string;
    status: 'active' | 'inactive' | 'error';
    lastRun: string;
    executionCount: number;
    successRate: number;
    steps: FlowStep[];
    triggeredBy?: TriggerInfo;
    triggersFlows?: TriggerInfo[];
}

// Mock data
const mockFlows: Flow[] = [
    {
        id: '1',
        name: 'Customer Onboarding',
        description: 'Automated customer onboarding workflow',
        status: 'active',
        lastRun: '2024-01-10 14:30',
        executionCount: 1542,
        successRate: 98.5,
        steps: [
            { id: 's1', name: 'Validate Email', type: 'action', status: 'completed', duration: 120 },
            { id: 's2', name: 'Check Eligibility', type: 'condition', status: 'completed', duration: 85 },
            { id: 's3', name: 'Create Account', type: 'action', status: 'completed', duration: 340 },
            { id: 's4', name: 'Send Welcome Email', type: 'action', status: 'completed', duration: 95 },
        ],
        triggeredBy: {
            flowId: 'signup-flow',
            flowName: 'User Signup Flow',
            timestamp: '2024-01-10 14:30',
            type: 'external'
        },
        triggersFlows: [
            {
                flowId: 'email-campaign',
                flowName: 'Email Campaign Flow',
                timestamp: '2024-01-10 14:31',
                type: 'external'
            }
        ]
    },
    {
        id: '2',
        name: 'Payment Processing',
        description: 'Handle payment transactions and verification',
        status: 'active',
        lastRun: '2024-01-10 15:45',
        executionCount: 3821,
        successRate: 99.2,
        steps: [
            { id: 'p1', name: 'Validate Payment', type: 'action', status: 'completed', duration: 200 },
            { id: 'p2', name: 'Fraud Check', type: 'condition', status: 'completed', duration: 150 },
            { id: 'p3', name: 'Process Transaction', type: 'action', status: 'running', duration: 450 },
            { id: 'p4', name: 'Update Balance', type: 'action', status: 'pending' },
            { id: 'p5', name: 'Send Receipt', type: 'action', status: 'pending' },
        ]
    },
    {
        id: '3',
        name: 'Data Sync Pipeline',
        description: 'Synchronize data across multiple systems',
        status: 'error',
        lastRun: '2024-01-10 16:00',
        executionCount: 567,
        successRate: 87.3,
        steps: [
            { id: 'd1', name: 'Extract Data', type: 'action', status: 'completed', duration: 1200 },
            { id: 'd2', name: 'Transform Data', type: 'transform', status: 'failed', error: 'Schema mismatch error', duration: 340 },
            { id: 'd3', name: 'Load Data', type: 'action', status: 'pending' },
        ]
    }
];

const FlowEngineAdminPanel: React.FC = () => {
    const [flows] = useState<Flow[]>(mockFlows);
    const [searchTerm, setSearchTerm] = useState('');
    const [selectedFlow, setSelectedFlow] = useState<Flow | null>(null);
    const [selectedStep, setSelectedStep] = useState<FlowStep | null>(null);
    const [showFlowChart, setShowFlowChart] = useState(false);
    const [relatedFlowToShow, setRelatedFlowToShow] = useState<TriggerInfo | null>(null);

    const filteredFlows = useMemo(() => {
        return flows.filter(flow =>
            flow.name.toLowerCase().includes(searchTerm.toLowerCase()) ||
            flow.description.toLowerCase().includes(searchTerm.toLowerCase())
        );
    }, [flows, searchTerm]);

    const getStatusColor = (status: string) => {
        switch (status) {
            case 'active':
            case 'completed':
                return 'text-green-600';
            case 'inactive':
            case 'pending':
                return 'text-gray-500';
            case 'error':
            case 'failed':
                return 'text-red-600';
            case 'running':
                return 'text-blue-600';
            default:
                return 'text-gray-600';
        }
    };

    const getStatusIcon = (status: string) => {
        switch (status) {
            case 'completed':
                return <CheckCircle className="w-4 h-4" />;
            case 'failed':
                return <XCircle className="w-4 h-4" />;
            case 'running':
                return <Activity className="w-4 h-4 animate-pulse" />;
            case 'pending':
                return <Clock className="w-4 h-4" />;
            default:
                return null;
        }
    };

    const getStepTypeColor = (type: string) => {
        switch (type) {
            case 'action':
                return 'bg-blue-100 text-blue-700 border-blue-300';
            case 'condition':
                return 'bg-purple-100 text-purple-700 border-purple-300';
            case 'trigger':
                return 'bg-green-100 text-green-700 border-green-300';
            case 'transform':
                return 'bg-orange-100 text-orange-700 border-orange-300';
            default:
                return 'bg-gray-100 text-gray-700 border-gray-300';
        }
    };

    const openFlowChart = (flow: Flow) => {
        setSelectedFlow(flow);
        setShowFlowChart(true);
    };

    const closeFlowChart = () => {
        setShowFlowChart(false);
        setSelectedStep(null);
        setRelatedFlowToShow(null);
    };

    const handleStepClick = (step: FlowStep) => {
        setSelectedStep(step);
    };

    const handleTriggeredByClick = () => {
        if (selectedFlow?.triggeredBy) {
            setRelatedFlowToShow(selectedFlow.triggeredBy);
        }
    };

    const handleTriggeredFlowClick = (triggerInfo: TriggerInfo) => {
        setRelatedFlowToShow(triggerInfo);
    };

    return (
        <div className="min-h-screen bg-gray-50">
            {/* Header */}
            <div className="bg-white border-b border-gray-200">
                <div className="px-6 py-4">
                    <div className="flex items-center justify-between">
                        <div>
                            <h1 className="text-2xl font-semibold text-gray-900">Flow Engine</h1>
                            <p className="text-sm text-gray-600 mt-1">Manage and monitor your automated workflows</p>
                        </div>
                        <button className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors flex items-center gap-2">
                            <GitBranch className="w-4 h-4" />
                            Create Flow
                        </button>
                    </div>
                </div>
            </div>

            {/* Search and Filters */}
            <div className="px-6 py-4 bg-white border-b border-gray-200">
                <div className="flex gap-4">
                    <div className="flex-1 relative">
                        <Search className="absolute left-3 top-1/2 transform -translate-y-1/2 text-gray-400 w-5 h-5" />
                        <input
                            type="text"
                            placeholder="Search flows..."
                            value={searchTerm}
                            onChange={(e) => setSearchTerm(e.target.value)}
                            className="w-full pl-10 pr-4 py-2 border border-gray-300 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500"
                        />
                    </div>
                    <button className="px-4 py-2 border border-gray-300 rounded-lg hover:bg-gray-50 transition-colors flex items-center gap-2">
                        <Filter className="w-4 h-4" />
                        Filters
                    </button>
                </div>
            </div>

            {/* Stats Cards */}
            <div className="px-6 py-6">
                <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
                    <div className="bg-white p-4 rounded-lg border border-gray-200">
                        <div className="flex items-center justify-between">
                            <div>
                                <p className="text-sm text-gray-600">Total Flows</p>
                                <p className="text-2xl font-semibold text-gray-900">{flows.length}</p>
                            </div>
                            <GitBranch className="w-8 h-8 text-blue-600 opacity-20" />
                        </div>
                    </div>
                    <div className="bg-white p-4 rounded-lg border border-gray-200">
                        <div className="flex items-center justify-between">
                            <div>
                                <p className="text-sm text-gray-600">Active</p>
                                <p className="text-2xl font-semibold text-green-600">
                                    {flows.filter(f => f.status === 'active').length}
                                </p>
                            </div>
                            <CheckCircle className="w-8 h-8 text-green-600 opacity-20" />
                        </div>
                    </div>
                    <div className="bg-white p-4 rounded-lg border border-gray-200">
                        <div className="flex items-center justify-between">
                            <div>
                                <p className="text-sm text-gray-600">With Errors</p>
                                <p className="text-2xl font-semibold text-red-600">
                                    {flows.filter(f => f.status === 'error').length}
                                </p>
                            </div>
                            <AlertCircle className="w-8 h-8 text-red-600 opacity-20" />
                        </div>
                    </div>
                    <div className="bg-white p-4 rounded-lg border border-gray-200">
                        <div className="flex items-center justify-between">
                            <div>
                                <p className="text-sm text-gray-600">Avg Success Rate</p>
                                <p className="text-2xl font-semibold text-gray-900">
                                    {(flows.reduce((acc, f) => acc + f.successRate, 0) / flows.length).toFixed(1)}%
                                </p>
                            </div>
                            <Activity className="w-8 h-8 text-purple-600 opacity-20" />
                        </div>
                    </div>
                </div>
            </div>

            {/* Flows List */}
            <div className="px-6 pb-6">
                <div className="bg-white rounded-lg border border-gray-200 overflow-hidden">
                    <table className="w-full">
                        <thead className="bg-gray-50 border-b border-gray-200">
                            <tr>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    Flow Name
                                </th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    Status
                                </th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    Last Run
                                </th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    Executions
                                </th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    Success Rate
                                </th>
                                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                                    Actions
                                </th>
                            </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-200">
                            {filteredFlows.map((flow) => (
                                <tr key={flow.id} className="hover:bg-gray-50 transition-colors">
                                    <td className="px-6 py-4">
                                        <div>
                                            <div className="text-sm font-medium text-gray-900">{flow.name}</div>
                                            <div className="text-sm text-gray-500">{flow.description}</div>
                                        </div>
                                    </td>
                                    <td className="px-6 py-4">
                                        <span className={`inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-medium ${flow.status === 'active' ? 'bg-green-100 text-green-800' :
                                                flow.status === 'error' ? 'bg-red-100 text-red-800' :
                                                    'bg-gray-100 text-gray-800'
                                            }`}>
                                            {flow.status === 'active' && <CheckCircle className="w-3 h-3" />}
                                            {flow.status === 'error' && <AlertCircle className="w-3 h-3" />}
                                            {flow.status}
                                        </span>
                                    </td>
                                    <td className="px-6 py-4 text-sm text-gray-900">{flow.lastRun}</td>
                                    <td className="px-6 py-4 text-sm text-gray-900">{flow.executionCount.toLocaleString()}</td>
                                    <td className="px-6 py-4">
                                        <div className="flex items-center gap-2">
                                            <div className="w-24 bg-gray-200 rounded-full h-2">
                                                <div
                                                    className={`h-2 rounded-full ${flow.successRate >= 95 ? 'bg-green-500' :
                                                            flow.successRate >= 80 ? 'bg-yellow-500' :
                                                                'bg-red-500'
                                                        }`}
                                                    style={{ width: `${flow.successRate}%` }}
                                                />
                                            </div>
                                            <span className="text-sm text-gray-900">{flow.successRate}%</span>
                                        </div>
                                    </td>
                                    <td className="px-6 py-4">
                                        <div className="flex items-center gap-2">
                                            <button
                                                onClick={() => openFlowChart(flow)}
                                                className="p-1.5 text-gray-600 hover:text-blue-600 hover:bg-blue-50 rounded transition-colors"
                                                title="View Flow Chart"
                                            >
                                                <Eye className="w-4 h-4" />
                                            </button>
                                            <button className="p-1.5 text-gray-600 hover:text-green-600 hover:bg-green-50 rounded transition-colors" title="Run Flow">
                                                <Play className="w-4 h-4" />
                                            </button>
                                            <button className="p-1.5 text-gray-600 hover:text-gray-900 hover:bg-gray-100 rounded transition-colors" title="Settings">
                                                <Settings className="w-4 h-4" />
                                            </button>
                                        </div>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            </div>

            {/* Flow Chart Modal */}
            {showFlowChart && selectedFlow && (
                <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50 p-4">
                    <div className="bg-white rounded-xl w-full max-w-6xl max-h-[90vh] overflow-hidden flex flex-col">
                        {/* Modal Header */}
                        <div className="px-6 py-4 border-b border-gray-200 flex items-center justify-between">
                            <div>
                                <h2 className="text-xl font-semibold text-gray-900">{selectedFlow.name}</h2>
                                <p className="text-sm text-gray-600 mt-1">Flow Visualization</p>
                            </div>
                            <button
                                onClick={closeFlowChart}
                                className="p-2 hover:bg-gray-100 rounded-lg transition-colors"
                            >
                                <X className="w-5 h-5 text-gray-600" />
                            </button>
                        </div>

                        {/* Flow Chart Content */}
                        <div className="flex-1 overflow-auto p-6">
                            <div className="flex items-start gap-8">
                                {/* Main Flow */}
                                <div className="flex-1">
                                    <div className="flex items-center gap-4 mb-6">
                                        {/* Triggered By Node */}
                                        {selectedFlow.triggeredBy && (
                                            <div
                                                onClick={handleTriggeredByClick}
                                                className="cursor-pointer group"
                                            >
                                                <div className="bg-gradient-to-r from-purple-500 to-purple-600 text-white px-4 py-3 rounded-lg shadow-lg hover:shadow-xl transition-all transform hover:scale-105">
                                                    <div className="flex items-center gap-2">
                                                        <Zap className="w-5 h-5" />
                                                        <div>
                                                            <p className="text-xs opacity-90">Triggered By</p>
                                                            <p className="font-medium">{selectedFlow.triggeredBy.flowName}</p>
                                                        </div>
                                                    </div>
                                                </div>
                                                <div className="flex justify-center mt-2">
                                                    <ArrowRight className="w-5 h-5 text-gray-400" />
                                                </div>
                                            </div>
                                        )}

                                        {/* Main Flow Steps */}
                                        <div className="flex-1 space-y-4">
                                            <div className="bg-gradient-to-r from-blue-500 to-blue-600 text-white px-4 py-3 rounded-lg shadow-md">
                                                <div className="flex items-center gap-2">
                                                    <GitBranch className="w-5 h-5" />
                                                    <span className="font-medium">Start</span>
                                                </div>
                                            </div>

                                            {selectedFlow.steps.map((step, index) => (
                                                <div key={step.id} className="relative">
                                                    {index > 0 && (
                                                        <div className="absolute -top-4 left-1/2 transform -translate-x-1/2">
                                                            <ArrowRight className="w-5 h-5 text-gray-300 rotate-90" />
                                                        </div>
                                                    )}
                                                    <div
                                                        onClick={() => handleStepClick(step)}
                                                        className={`cursor-pointer border-2 rounded-lg p-4 hover:shadow-lg transition-all transform hover:scale-105 ${getStepTypeColor(step.type)}`}
                                                    >
                                                        <div className="flex items-center justify-between">
                                                            <div className="flex items-center gap-3">
                                                                <div className={`${getStatusColor(step.status)}`}>
                                                                    {getStatusIcon(step.status)}
                                                                </div>
                                                                <div>
                                                                    <p className="font-medium">{step.name}</p>
                                                                    <p className="text-xs opacity-75 capitalize">{step.type}</p>
                                                                </div>
                                                            </div>
                                                            {step.duration && (
                                                                <span className="text-xs opacity-75">{step.duration}ms</span>
                                                            )}
                                                        </div>
                                                        {step.error && (
                                                            <p className="text-xs text-red-600 mt-2">{step.error}</p>
                                                        )}
                                                    </div>
                                                </div>
                                            ))}

                                            <div className="bg-gradient-to-r from-green-500 to-green-600 text-white px-4 py-3 rounded-lg shadow-md">
                                                <div className="flex items-center gap-2">
                                                    <CheckCircle className="w-5 h-5" />
                                                    <span className="font-medium">End</span>
                                                </div>
                                            </div>
                                        </div>

                                        {/* Triggered Flows */}
                                        {selectedFlow.triggersFlows && selectedFlow.triggersFlows.length > 0 && (
                                            <div className="space-y-3">
                                                <div className="flex justify-center mb-2">
                                                    <ArrowRight className="w-5 h-5 text-gray-400" />
                                                </div>
                                                {selectedFlow.triggersFlows.map((trigger, index) => (
                                                    <div
                                                        key={index}
                                                        onClick={() => handleTriggeredFlowClick(trigger)}
                                                        className="cursor-pointer group"
                                                    >
                                                        <div className="bg-gradient-to-r from-teal-500 to-teal-600 text-white px-4 py-3 rounded-lg shadow-lg hover:shadow-xl transition-all transform hover:scale-105">
                                                            <div className="flex items-center gap-2">
                                                                <ExternalLink className="w-5 h-5" />
                                                                <div>
                                                                    <p className="text-xs opacity-90">Triggers</p>
                                                                    <p className="font-medium">{trigger.flowName}</p>
                                                                </div>
                                                            </div>
                                                        </div>
                                                    </div>
                                                ))}
                                            </div>
                                        )}
                                    </div>
                                </div>

                                {/* Flow Stats Sidebar */}
                                <div className="w-80 space-y-4">
                                    <div className="bg-gray-50 rounded-lg p-4">
                                        <h3 className="font-medium text-gray-900 mb-3">Flow Statistics</h3>
                                        <div className="space-y-3">
                                            <div className="flex justify-between text-sm">
                                                <span className="text-gray-600">Total Executions</span>
                                                <span className="font-medium">{selectedFlow.executionCount.toLocaleString()}</span>
                                            </div>
                                            <div className="flex justify-between text-sm">
                                                <span className="text-gray-600">Success Rate</span>
                                                <span className="font-medium text-green-600">{selectedFlow.successRate}%</span>
                                            </div>
                                            <div className="flex justify-between text-sm">
                                                <span className="text-gray-600">Last Run</span>
                                                <span className="font-medium">{selectedFlow.lastRun}</span>
                                            </div>
                                            <div className="flex justify-between text-sm">
                                                <span className="text-gray-600">Total Steps</span>
                                                <span className="font-medium">{selectedFlow.steps.length}</span>
                                            </div>
                                        </div>
                                    </div>

                                    <div className="bg-blue-50 border border-blue-200 rounded-lg p-4">
                                        <div className="flex items-center gap-2 text-blue-700 mb-2">
                                            <Activity className="w-4 h-4" />
                                            <span className="font-medium text-sm">Flow Actions</span>
                                        </div>
                                        <div className="space-y-2">
                                            <button className="w-full px-3 py-2 bg-white border border-blue-300 text-blue-700 rounded hover:bg-blue-100 transition-colors text-sm flex items-center justify-center gap-2">
                                                <Play className="w-4 h-4" />
                                                Run Flow
                                            </button>
                                            <button className="w-full px-3 py-2 bg-white border border-gray-300 text-gray-700 rounded hover:bg-gray-100 transition-colors text-sm flex items-center justify-center gap-2">
                                                <Pause className="w-4 h-4" />
                                                Pause Flow
                                            </button>
                                            <button className="w-full px-3 py-2 bg-white border border-gray-300 text-gray-700 rounded hover:bg-gray-100 transition-colors text-sm flex items-center justify-center gap-2">
                                                <Settings className="w-4 h-4" />
                                                Configure
                                            </button>
                                        </div>
                                    </div>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            )}

            {/* Step Details Modal */}
            {selectedStep && (
                <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-[60] p-4">
                    <div className="bg-white rounded-xl w-full max-w-2xl max-h-[80vh] overflow-hidden flex flex-col">
                        <div className="px-6 py-4 border-b border-gray-200 flex items-center justify-between">
                            <div>
                                <h3 className="text-lg font-semibold text-gray-900">{selectedStep.name}</h3>
                                <p className="text-sm text-gray-600 mt-1">Step Details</p>
                            </div>
                            <button
                                onClick={() => setSelectedStep(null)}
                                className="p-2 hover:bg-gray-100 rounded-lg transition-colors"
                            >
                                <X className="w-5 h-5 text-gray-600" />
                            </button>
                        </div>

                        <div className="flex-1 overflow-auto p-6">
                            <div className="space-y-6">
                                {/* Step Info */}
                                <div className="bg-gray-50 rounded-lg p-4">
                                    <h4 className="font-medium text-gray-900 mb-3">Step Information</h4>
                                    <div className="space-y-3">
                                        <div className="flex justify-between text-sm">
                                            <span className="text-gray-600">Type</span>
                                            <span className={`px-2 py-1 rounded text-xs font-medium ${getStepTypeColor(selectedStep.type)}`}>
                                                {selectedStep.type}
                                            </span>
                                        </div>
                                        <div className="flex justify-between text-sm">
                                            <span className="text-gray-600">Status</span>
                                            <span className={`flex items-center gap-1 ${getStatusColor(selectedStep.status)}`}>
                                                {getStatusIcon(selectedStep.status)}
                                                <span className="font-medium">{selectedStep.status}</span>
                                            </span>
                                        </div>
                                        {selectedStep.duration && (
                                            <div className="flex justify-between text-sm">
                                                <span className="text-gray-600">Duration</span>
                                                <span className="font-medium">{selectedStep.duration}ms</span>
                                            </div>
                                        )}
                                    </div>
                                </div>

                                {/* Description */}
                                {selectedStep.description && (
                                    <div>
                                        <h4 className="font-medium text-gray-900 mb-2">Description</h4>
                                        <p className="text-sm text-gray-600">{selectedStep.description}</p>
                                    </div>
                                )}

                                {/* Error Details */}
                                {selectedStep.error && (
                                    <div className="bg-red-50 border border-red-200 rounded-lg p-4">
                                        <h4 className="font-medium text-red-900 mb-2 flex items-center gap-2">
                                            <AlertCircle className="w-4 h-4" />
                                            Error Details
                                        </h4>
                                        <p className="text-sm text-red-700">{selectedStep.error}</p>
                                    </div>
                                )}

                                {/* Configuration */}
                                {selectedStep.config && (
                                    <div>
                                        <h4 className="font-medium text-gray-900 mb-2">Configuration</h4>
                                        <div className="bg-gray-50 rounded-lg p-3">
                                            <pre className="text-xs text-gray-700 overflow-x-auto">
                                                {JSON.stringify(selectedStep.config, null, 2)}
                                            </pre>
                                        </div>
                                    </div>
                                )}
                            </div>
                        </div>

                        <div className="px-6 py-4 border-t border-gray-200 flex justify-end gap-3">
                            <button
                                onClick={() => setSelectedStep(null)}
                                className="px-4 py-2 border border-gray-300 text-gray-700 rounded-lg hover:bg-gray-50 transition-colors"
                            >
                                Close
                            </button>
                            <button className="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors">
                                Edit Step
                            </button>
                        </div>
                    </div>
                </div>
            )}

            {/* Related Flow Details Modal */}
            {relatedFlowToShow && (
                <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-[70] p-4">
                    <div className="bg-white rounded-xl w-full max-w-lg">
                        <div className="px-6 py-4 border-b border-gray-200 flex items-center justify-between">
                            <div>
                                <h3 className="text-lg font-semibold text-gray-900">{relatedFlowToShow.flowName}</h3>
                                <p className="text-sm text-gray-600 mt-1">Related Flow Details</p>
                            </div>
                            <button
                                onClick={() => setRelatedFlowToShow(null)}
                                className="p-2 hover:bg-gray-100 rounded-lg transition-colors"
                            >
                                <X className="w-5 h-5 text-gray-600" />
                            </button>
                        </div>

                        <div className="p-6">
                            <div className="space-y-4">
                                <div className="flex justify-between text-sm">
                                    <span className="text-gray-600">Flow ID</span>
                                    <span className="font-mono text-xs bg-gray-100 px-2 py-1 rounded">{relatedFlowToShow.flowId}</span>
                                </div>
                                <div className="flex justify-between text-sm">
                                    <span className="text-gray-600">Trigger Type</span>
                                    <span className="font-medium capitalize">{relatedFlowToShow.type}</span>
                                </div>
                                <div className="flex justify-between text-sm">
                                    <span className="text-gray-600">Timestamp</span>
                                    <span className="font-medium">{relatedFlowToShow.timestamp}</span>
                                </div>
                            </div>

                            <div className="mt-6 flex gap-3">
                                <button
                                    onClick={() => setRelatedFlowToShow(null)}
                                    className="flex-1 px-4 py-2 border border-gray-300 text-gray-700 rounded-lg hover:bg-gray-50 transition-colors"
                                >
                                    Close
                                </button>
                                <button className="flex-1 px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors flex items-center justify-center gap-2">
                                    <Eye className="w-4 h-4" />
                                    View Flow
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            )}
        </div>
    );
};

export default FlowEngineAdminPanel;