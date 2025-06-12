import React, { useState, useEffect } from 'react';
import { format } from 'date-fns';
import api from '../../services/api';

interface PersonalInfo {
    firstName: string;
    lastName: string;
    dateOfBirth: string;
    documentNumber: string;
    nationality: string;
}

interface VerificationHistory {
    timestamp: string;
    action: string;
    status: string;
    performedBy: string;
    details?: any;
}

interface KycVerification {
    id: string;
    userId: string;
    status: string;
    verificationLevel: string;
    submittedAt: string;
    lastCheckedAt: string | null;
    isPoliticallyExposed: boolean;
    isHighRisk: boolean;
    riskScore: string;
    verificationData: Record<string, any>;
    history?: VerificationHistory[];
    additionalInfo?: {
        AmlCheckDate?: string;
        AmlProvider?: string;
    };
}

interface PaginatedResult {
    items: KycVerification[];
    page: number;
    pageSize: number;
    totalCount: number;
    totalPages: number;
    hasPreviousPage: boolean;
    hasNextPage: boolean;
}

const KycAdminPanel: React.FC = () => {
    const [verifications, setVerifications] = useState<KycVerification[]>([]);
    const [loading, setLoading] = useState<boolean>(true);
    const [error, setError] = useState<string | null>(null);
    const [page, setPage] = useState<number>(1);
    const [totalPages, setTotalPages] = useState<number>(1);
    const [selectedVerification, setSelectedVerification] = useState<KycVerification | null>(null);
    const [statusUpdate, setStatusUpdate] = useState<{ status: string; comment: string }>({
        status: '',
        comment: '',
    });
    const [activeTab, setActiveTab] = useState<'info' | 'history'>('info');

    const fetchVerifications = async () => {
        try {
            setLoading(true);
            const response = await api.get<{ success: boolean; data: PaginatedResult }>(
                `/admin/kyc/pending?page=${page}&pageSize=10`
            );

            if (response.data.success) {
                setVerifications(response.data.data.items);
                setTotalPages(response.data.data.totalPages);
            } else {
                setError('Failed to fetch verifications');
            }
        } catch (err: any) {
            setError(err.message || 'An error occurred');
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        fetchVerifications();
    }, [page]);

    const handleUpdateStatus = async () => {
        if (!selectedVerification || !statusUpdate.status) {
            return;
        }

        try {
            const response = await api.post(`/admin/kyc/update-status/${selectedVerification.userId}`, {
                status: statusUpdate.status,
                comment: statusUpdate.comment,
            });

            if (response.data.success) {
                // Refresh the verifications list
                fetchVerifications();
                // Close the detail view
                setSelectedVerification(null);
                setStatusUpdate({ status: '', comment: '' });
            } else {
                setError('Failed to update status');
            }
        } catch (err: any) {
            setError(err.message || 'An error occurred while updating status');
        }
    };

    const getStatusBadgeColor = (status: string): string => {
        switch (status) {
            case 'PENDING_VERIFICATION':
                return 'bg-yellow-100 text-yellow-800';
            case 'NEEDS_REVIEW':
                return 'bg-orange-100 text-orange-800';
            case 'APPROVED':
                return 'bg-green-100 text-green-800';
            case 'REJECTED':
                return 'bg-red-100 text-red-800';
            case 'ADDITIONAL_INFO_REQUIRED':
                return 'bg-blue-100 text-blue-800';
            default:
                return 'bg-gray-100 text-gray-800';
        }
    };

    const formatDate = (dateString: string | null): string => {
        if (!dateString) return 'N/A';
        return format(new Date(dateString), 'MMM dd, yyyy HH:mm');
    };

    if (loading && verifications.length === 0) {
        return (
            <div className="bg-white shadow rounded-lg p-6">
                <h2 className="text-xl font-semibold mb-4">KYC Verification Requests</h2>
                <div className="flex justify-center">
                    <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-500"></div>
                </div>
            </div>
        );
    }

    if (error) {
        return (
            <div className="bg-white shadow rounded-lg p-6">
                <h2 className="text-xl font-semibold mb-4">KYC Verification Requests</h2>
                <div className="bg-red-50 border border-red-200 text-red-700 px-4 py-3 rounded">
                    {error}
                </div>
            </div>
        );
    }

    return (
        <div className="bg-white shadow rounded-lg p-6">
            <h2 className="text-xl font-semibold mb-4">KYC Verification Requests</h2>

            {selectedVerification ? (
                <div className="border rounded-lg p-4 mb-4">
                    <div className="flex justify-between items-center mb-4">
                        <h3 className="text-lg font-medium">Verification Details</h3>
                        <button
                            onClick={() => setSelectedVerification(null)}
                            className="text-gray-500 hover:text-gray-700"
                        >
                            <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M6 18L18 6M6 6l12 12" />
                            </svg>
                        </button>
                    </div>

                    <div className="mb-4">
                        <div className="flex border-b space-x-4">
                            <button
                                onClick={() => setActiveTab('info')}
                                className={`py-2 px-4 ${activeTab === 'info' ? 'border-b-2 border-blue-500 text-blue-600' : 'text-gray-500'}`}
                            >
                                Personal Info
                            </button>
                            <button
                                onClick={() => setActiveTab('history')}
                                className={`py-2 px-4 ${activeTab === 'history' ? 'border-b-2 border-blue-500 text-blue-600' : 'text-gray-500'}`}
                            >
                                History
                            </button>
                        </div>
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mb-4">
                        <div>
                            <p className="text-sm text-gray-500">User ID</p>
                            <p className="font-medium">{selectedVerification.userId}</p>
                        </div>
                        <div>
                            <p className="text-sm text-gray-500">Status</p>
                            <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${getStatusBadgeColor(selectedVerification.status)}`}>
                                {selectedVerification.status}
                            </span>
                        </div>
                        <div>
                            <p className="text-sm text-gray-500">Verification Level</p>
                            <p className="font-medium">{selectedVerification.verificationLevel}</p>
                        </div>
                        <div>
                            <p className="text-sm text-gray-500">Submitted At</p>
                            <p className="font-medium">{formatDate(selectedVerification.submittedAt)}</p>
                        </div>
                        <div>
                            <p className="text-sm text-gray-500">Risk Score</p>
                            <p className={`font-medium ${selectedVerification.riskScore === 'high' ? 'text-red-600' : selectedVerification.riskScore === 'medium' ? 'text-yellow-600' : 'text-green-600'}`}>
                                {selectedVerification.riskScore?.toUpperCase() || 'N/A'}
                            </p>
                        </div>
                        <div>
                            <p className="text-sm text-gray-500">Politically Exposed</p>
                            <p className="font-medium">{selectedVerification.isPoliticallyExposed ? 'Yes' : 'No'}</p>
                        </div>
                    </div>

                    {activeTab === 'info' && selectedVerification.verificationData && (
                        <div className="mb-4">
                            <div className="mb-4">
                                <h4 className="text-md font-medium mb-2">Verification Documents</h4>
                                <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                                    {selectedVerification.verificationData?.selfieImage && (
                                        <div>
                                            <p className="text-sm text-gray-500 mb-1">Selfie Image</p>
                                            <div className="border rounded p-1">
                                                <img
                                                    src={selectedVerification.verificationData.selfieImage}
                                                    alt="Selfie"
                                                    className="max-h-64 object-contain mx-auto"
                                                />
                                            </div>
                                        </div>
                                    )}
                                    {selectedVerification.verificationData?.documentImage && (
                                        <div>
                                            <p className="text-sm text-gray-500 mb-1">Document Image</p>
                                            <div className="border rounded p-1">
                                                <img
                                                    src={selectedVerification.verificationData.documentImage}
                                                    alt="ID Document"
                                                    className="max-h-64 object-contain mx-auto"
                                                />
                                            </div>
                                        </div>
                                    )}
                                </div>
                            </div>
                            <h4 className="text-md font-medium mb-2">Personal Information</h4>
                            <div className="bg-gray-50 p-3 rounded">
                                <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
                                    <div>
                                        <p className="text-sm text-gray-500">First Name</p>
                                        <p className="font-medium">{selectedVerification.verificationData.firstName}</p>
                                    </div>
                                    <div>
                                        <p className="text-sm text-gray-500">Last Name</p>
                                        <p className="font-medium">{selectedVerification.verificationData.lastName}</p>
                                    </div>
                                    <div>
                                        <p className="text-sm text-gray-500">Date of Birth</p>
                                        <p className="font-medium">{selectedVerification.verificationData.dateOfBirth}</p>
                                    </div>
                                    <div>
                                        <p className="text-sm text-gray-500">Document Number</p>
                                        <p className="font-medium">{selectedVerification.verificationData.documentNumber}</p>
                                    </div>
                                    <div>
                                        <p className="text-sm text-gray-500">Nationality</p>
                                        <p className="font-medium">{selectedVerification.verificationData.nationality}</p>
                                    </div>
                                </div>
                            </div>
                        </div>
                    )}
                    {activeTab === 'history' && selectedVerification.history && (
                        <div className="mb-4">
                            <h4 className="text-md font-medium mb-2">Verification History</h4>
                            <div className="bg-gray-50 p-3 rounded overflow-x-auto">
                                <table className="min-w-full divide-y divide-gray-200">
                                    <thead>
                                        <tr>
                                            <th className="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Timestamp</th>
                                            <th className="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Action</th>
                                            <th className="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Status</th>
                                            <th className="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">By</th>
                                        </tr>
                                    </thead>
                                    <tbody className="divide-y divide-gray-200">
                                    {selectedVerification.history ? (
                                        selectedVerification.history.map((entry, index) => (
                                            <tr key={index}>
                                                <td className="px-3 py-2 whitespace-nowrap text-sm">{formatDate(entry.timestamp)}</td>
                                                <td className="px-3 py-2 whitespace-nowrap text-sm">{entry.action}</td>
                                                <td className="px-3 py-2 whitespace-nowrap text-sm">
                                                    <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium ${getStatusBadgeColor(entry.status)}`}>
                                                        {entry.status}
                                                    </span>
                                                </td>
                                                <td className="px-3 py-2 whitespace-nowrap text-sm">{entry.performedBy === 'SYSTEM' ? 'System' : 'Admin'}</td>
                                            </tr>
                                        )))
                                        : (
                                            <tr key={'noHistory'}>
                                                    <td className="px-3 py-2 whitespace-nowrap text-sm" colSpan={4}>No records</td>
                                            </tr>
                                        )
                                        }
                                    </tbody>
                                </table>
                            </div>
                        </div>
                    )}

                    <div className="border-t pt-4 mt-4">
                        <h4 className="text-md font-medium mb-2">Update Status</h4>
                        <div className="grid grid-cols-1 gap-4">
                            <div>
                                <label className="block text-sm font-medium text-gray-700">Status</label>
                                <select
                                    value={statusUpdate.status}
                                    onChange={(e) => setStatusUpdate({ ...statusUpdate, status: e.target.value })}
                                    className="mt-1 block w-full pl-3 pr-10 py-2 text-base border-gray-300 focus:outline-none focus:ring-blue-500 focus:border-blue-500 sm:text-sm rounded-md"
                                >
                                    <option value="">Select Status</option>
                                    <option value="APPROVED">Approve</option>
                                    <option value="REJECTED">Reject</option>
                                    <option value="ADDITIONAL_INFO_REQUIRED">Request Additional Info</option>
                                    <option value="PENDING_VERIFICATION">Pending Verification</option>
                                </select>
                            </div>
                            <div>
                                <label className="block text-sm font-medium text-gray-700">Comment</label>
                                <textarea
                                    value={statusUpdate.comment}
                                    onChange={(e) => setStatusUpdate({ ...statusUpdate, comment: e.target.value })}
                                    rows={3}
                                    className="mt-1 block w-full border border-gray-300 rounded-md shadow-sm py-2 px-3 focus:outline-none focus:ring-blue-500 focus:border-blue-500 sm:text-sm"
                                    placeholder="Add a comment about this decision..."
                                />
                            </div>
                            <div>
                                <button
                                    onClick={handleUpdateStatus}
                                    disabled={!statusUpdate.status}
                                    className="w-full inline-flex justify-center py-2 px-4 border border-transparent shadow-sm text-sm font-medium rounded-md text-white bg-blue-600 hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500 disabled:bg-blue-300"
                                >
                                    Update Status
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            ) : (
                <div>
                    {verifications.length === 0 ? (
                        <div className="text-center py-8">
                            <svg className="mx-auto h-12 w-12 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
                            </svg>
                            <h3 className="mt-2 text-sm font-medium text-gray-900">No pending verifications</h3>
                            <p className="mt-1 text-sm text-gray-500">There are no verifications waiting for review.</p>
                        </div>
                    ) : (
                        <>
                            <div className="overflow-x-auto">
                                <table className="min-w-full divide-y divide-gray-200">
                                    <thead className="bg-gray-50">
                                        <tr>
                                            <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">User ID</th>
                                            <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Status</th>
                                            <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Level</th>
                                            <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Submitted</th>
                                            <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Risk</th>
                                            <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Actions</th>
                                        </tr>
                                    </thead>
                                    <tbody className="bg-white divide-y divide-gray-200">
                                        {verifications.map((verification) => (
                                            <tr key={verification.id} className="hover:bg-gray-50">
                                                <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-gray-900">{verification.userId.substring(0, 8)}...</td>
                                                <td className="px-6 py-4 whitespace-nowrap text-sm">
                                                    <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${getStatusBadgeColor(verification.status)}`}>
                                                        {verification.status}
                                                    </span>
                                                </td>
                                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{verification.verificationLevel}</td>
                                                <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{formatDate(verification.submittedAt)}</td>
                                                <td className="px-6 py-4 whitespace-nowrap">
                                                    <span className={`inline-block h-2 w-2 rounded-full mr-2 ${verification.isHighRisk ? 'bg-red-500' : verification.isPoliticallyExposed ? 'bg-yellow-500' : 'bg-green-500'}`}></span>
                                                    <span className="text-sm text-gray-500">{verification.riskScore}</span>
                                                </td>
                                                <td className="px-6 py-4 whitespace-nowrap text-sm font-medium">
                                                    <button
                                                        onClick={() => setSelectedVerification(verification)}
                                                        className="text-blue-600 hover:text-blue-900"
                                                    >
                                                        Review
                                                    </button>
                                                </td>
                                            </tr>
                                        ))}
                                    </tbody>
                                </table>
                            </div>

                            {totalPages > 1 && (
                                <div className="flex items-center justify-between border-t border-gray-200 bg-white px-4 py-3 sm:px-6 mt-4">
                                    <div className="flex flex-1 justify-between sm:hidden">
                                        <button
                                            onClick={() => setPage(page - 1)}
                                            disabled={page === 1}
                                            className="relative inline-flex items-center rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:bg-gray-100 disabled:text-gray-400"
                                        >
                                            Previous
                                        </button>
                                        <button
                                            onClick={() => setPage(page + 1)}
                                            disabled={page === totalPages}
                                            className="relative ml-3 inline-flex items-center rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:bg-gray-100 disabled:text-gray-400"
                                        >
                                            Next
                                        </button>
                                    </div>
                                    <div className="hidden sm:flex sm:flex-1 sm:items-center sm:justify-between">
                                        <div>
                                            <p className="text-sm text-gray-700">
                                                Showing page <span className="font-medium">{page}</span> of <span className="font-medium">{totalPages}</span>
                                            </p>
                                        </div>
                                        <div>
                                            <nav className="isolate inline-flex -space-x-px rounded-md shadow-sm" aria-label="Pagination">
                                                <button
                                                    onClick={() => setPage(page - 1)}
                                                    disabled={page === 1}
                                                    className="relative inline-flex items-center rounded-l-md px-2 py-2 text-gray-400 ring-1 ring-inset ring-gray-300 hover:bg-gray-50 focus:z-20 focus:outline-offset-0 disabled:bg-gray-100"
                                                >
                                                    <span className="sr-only">Previous</span>
                                                    <svg className="h-5 w-5" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
                                                        <path fillRule="evenodd" d="M12.79 5.23a.75.75 0 01-.02 1.06L8.832 10l3.938 3.71a.75.75 0 11-1.04 1.08l-4.5-4.25a.75.75 0 010-1.08l4.5-4.25a.75.75 0 011.06.02z" clipRule="evenodd" />
                                                    </svg>
                                                </button>
                                                <button
                                                    onClick={() => setPage(page + 1)}
                                                    disabled={page === totalPages}
                                                    className="relative inline-flex items-center rounded-r-md px-2 py-2 text-gray-400 ring-1 ring-inset ring-gray-300 hover:bg-gray-50 focus:z-20 focus:outline-offset-0 disabled:bg-gray-100"
                                                >
                                                    <span className="sr-only">Next</span>
                                                    <svg className="h-5 w-5" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
                                                        <path fillRule="evenodd" d="M7.21 14.77a.75.75 0 01.02-1.06L11.168 10 7.23 6.29a.75.75 0 111.04-1.08l4.5 4.25a.75.75 0 010 1.08l-4.5 4.25a.75.75 0 01-1.06-.02z" clipRule="evenodd" />
                                                    </svg>
                                                </button>
                                            </nav>
                                        </div>
                                    </div>
                                </div>
                            )}
                        </>
                    )}
                </div>
            )}
        </div>
    );
};

export default KycAdminPanel;