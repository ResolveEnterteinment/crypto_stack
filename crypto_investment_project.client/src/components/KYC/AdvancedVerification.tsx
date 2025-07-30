import React, { useState, useCallback } from 'react';
import {
    Button, Steps, Card, Alert, Typography, Form,
    Badge, Upload, message, Modal, Tooltip,
    Space, Checkbox, Select
} from 'antd';
import {
    IdcardOutlined, CheckCircleOutlined,
    UploadOutlined, EyeOutlined, VideoCameraOutlined,
    CloseOutlined
} from '@ant-design/icons';
import { motion, AnimatePresence } from 'framer-motion';
import type { UploadProps } from 'antd/es/upload/interface';
import kycService from "../../services/kycService";
import LiveDocumentCapture from './LiveDocumentCapture';
import { DocumentCaptureData, DocumentUpload, LiveDocumentCaptureRequest, DocumentType, AdvancedVerificationData } from '../../types/kyc';

const { Title, Text, Paragraph } = Typography;
const { Option } = Select;

interface AdvancedVerificationProps {
    userId: string;
    sessionId: string;
    level: string;
    onSubmit: (data: any) => void;
}

// Document type options for live capture
const DocumentTypes: Record<string, DocumentType> = {
    utility_bill: { value: 'utility_bill', label: 'Utility Bill', icon: <UploadOutlined />, requiresLive: false, requiresDuplex: false },
    residence_permit: { value: 'residence_permit', label: 'Residence Permit', icon: <UploadOutlined />, requiresLive: false, requiresDuplex: false }
};

const AdvancedVerification: React.FC<AdvancedVerificationProps> = ({
    userId,
    sessionId,
    level,
    onSubmit
}) => {
    // State management
    const [currentStep, setCurrentStep] = useState(0);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);

    const [documents, setDocuments] = useState<DocumentUpload[]>([]);

    const [consentGiven, setConsentGiven] = useState(false);
    const [termsAccepted, setTermsAccepted] = useState(false);
    const [previewModal, setPreviewModal] = useState({ visible: false, images: [''], title: '' });
    const [selectedDocumentType, setSelectedDocumentType] = useState<'utility_bill' | 'residence_permit'>('residence_permit');
    const [captureMode, setCaptureMode] = useState<'upload' | 'live'>('live');

    // Handle live document capture
    const handleLiveDocumentCapture = useCallback(async (captureData: DocumentCaptureData) => {
        try {
            setLoading(true);

            var liveCaptureRequest: LiveDocumentCaptureRequest = {
                sessionId,
                documentType: selectedDocumentType,
                imageData: captureData.imageData,
                isLive: captureData.isLive,
                isDuplex: captureData.isDuplex,
                captureMetadata: {
                    deviceFingerprint: captureData.captureMetadata.deviceFingerprint,
                    timestamp: captureData.captureMetadata.timestamp,
                    userAgent: navigator.userAgent,
                    screenResolution: `${screen.width}x${screen.height}`,
                    cameraInfo: captureData.captureMetadata.cameraProperties,
                    environmentData: captureData.captureMetadata.environmentalFactors
                }
            }

            // Submit live capture to backend
            const response = await kycService.submitLiveDocumentCapture(liveCaptureRequest);

            if (!response) {
                throw new Error('Live capture submission failed');
            }

            const result = response.data;

            // Create document record
            const newDoc: DocumentUpload = {
                id: result.captureId,
                type: selectedDocumentType,
                preview: captureData.imageData.map(i => i.imageData),
                uploadDate: new Date(),
                encryptedHash: await Promise.all(captureData.imageData.map(async i => await computeImageHash(i.imageData))),
                isLiveCapture: true,
                captureData
            };

            setDocuments(prev => [...prev, newDoc]);
            message.success('Document captured successfully!');
        } catch (error) {
            console.error('Live capture error:', error);
            message.error('Live capture failed. Please try again.');
        } finally {
            setLoading(false);
        }
    }, [sessionId, selectedDocumentType]);

    // Enhanced file validation with security checks
    const validateFile = (file: File): Promise<boolean> => {
        return new Promise((resolve) => {
            // Check file type
            const allowedTypes = [
                'image/jpeg', 'image/png', 'image/webp',
                'application/pdf'
            ];

            if (!allowedTypes.includes(file.type)) {
                message.error('Invalid file type. Only JPEG, PNG, WebP, and PDF files are allowed.');
                resolve(false);
                return;
            }

            // Check file size (max 10MB)
            const maxSize = 10 * 1024 * 1024;
            if (file.size > maxSize) {
                message.error('File size too large. Maximum size is 10MB.');
                resolve(false);
                return;
            }

            // Check for malicious content (basic)
            const reader = new FileReader();
            reader.onload = (e) => {
                const result = e.target?.result as string;
                // Basic checks for malicious patterns
                const maliciousPatterns = [
                    /<script/i,
                    /javascript:/i,
                    /%3Cscript/i,
                    /onload=/i,
                    /onerror=/i
                ];

                const isMalicious = maliciousPatterns.some(pattern => pattern.test(result));
                if (isMalicious) {
                    message.error('File contains potentially malicious content.');
                    resolve(false);
                    return;
                }
                resolve(true);
            };
            reader.onerror = () => resolve(false);
            reader.readAsText(file.slice(0, 1024)); // Read first 1KB for pattern checking
        });
    };

    // Enhanced document upload with encryption
    const handleDocumentUpload: UploadProps['customRequest'] = async (options) => {
        const { file, onSuccess, onError, onProgress } = options;

        try {
            setLoading(true);

            // Validate file
            const isValid = await validateFile(file as File);
            if (!isValid) {
                onError?.(new Error('File validation failed'));
                return;
            }

            // Check if this document type requires live capture
            const docType = DocumentTypes[selectedDocumentType];
            if (docType?.requiresLive && captureMode === 'upload') {
                message.warning('This document type requires live camera capture for security reasons.');
                onError?.(new Error('Live capture required'));
                return;
            }

            // Create encrypted hash for integrity verification
            const arrayBuffer = await (file as File).arrayBuffer();
            const hashBuffer = await crypto.subtle.digest('SHA-256', arrayBuffer);
            const hashArray = Array.from(new Uint8Array(hashBuffer));
            const encryptedHash = hashArray.map(b => b.toString(16).padStart(2, '0')).join('');

            // Simulate upload progress
            let progress = 0;
            const progressInterval = setInterval(() => {
                progress += 10;
                onProgress?.({ percent: progress });
                if (progress >= 100) {
                    clearInterval(progressInterval);
                }
            }, 100);

            const response = await kycService.uploadDocument(sessionId, file as File, selectedDocumentType);

            if (!response) {
                throw new Error('Upload failed');
            }

            const result = await response;

            // Add to documents list
            const newDoc: DocumentUpload = {
                id: result.documentId,
                type: selectedDocumentType,
                files: [file as File],
                preview: [URL.createObjectURL(file as File)],
                uploadDate: result.uploadedAt || new Date(),
                encryptedHash: [encryptedHash],
                isLiveCapture: false
            };

            setDocuments(prev => [...prev, newDoc]);
            onSuccess?.(result);
            message.success('Document uploaded successfully');

        } catch (error) {
            console.error('Upload error:', error);
            onError?.(error as Error);
            message.error('Upload failed. Please try again.');
        } finally {
            setLoading(false);
        }
    };

    // Handle document removal
    const handleRemoveDocument = (documentId: string) => {
        setDocuments(prev => {
            const updatedDocuments = prev.filter(doc => doc.id !== documentId);

            // Clean up preview URL to prevent memory leaks
            const removedDoc = prev.find(doc => doc.id === documentId);
            if (removedDoc?.preview && removedDoc.preview.some(p => p.startsWith('blob:'))) {
                removedDoc.preview.forEach(p => URL.revokeObjectURL(p));
            }

            return updatedDocuments;
        });

        message.success('Document removed successfully');
    };

    // Final submission with comprehensive validation
    const handleFinalSubmission = async () => {
        if (!consentGiven || !termsAccepted) {
            message.error('Please accept all terms and conditions');
            return;
        }

        try {
            setLoading(true);

            // Prepare submission data
            const submissionData: AdvancedVerificationData = {
                documents: documents.map(doc => ({
                    id: doc.id,
                    type: doc.type,
                    hash: doc.encryptedHash,
                    uploadDate: doc.uploadDate,
                    isLiveCapture: doc.isLiveCapture,
                }))
            };

            await onSubmit(submissionData);
        } catch (error) {
            console.error('Submission error:', error);
            message.error('Submission failed. Please try again.');
        } finally {
            setLoading(false);
        }
    };

    // Helper function to compute image hash
    const computeImageHash = async (imageData: string): Promise<string> => {
        const response = await fetch(imageData);
        const arrayBuffer = await response.arrayBuffer();
        const hashBuffer = await crypto.subtle.digest('SHA-256', arrayBuffer);
        const hashArray = Array.from(new Uint8Array(hashBuffer));
        return hashArray.map(b => b.toString(16).padStart(2, '0')).join('');
    };

    // Step configurations
    const desktopSteps = [

        {
            title: 'Document Verification',
            icon: <VideoCameraOutlined />
        },
        {
            title: 'Review & Submit',
            icon: <CheckCircleOutlined />
        }
    ];

    const mobileSteps = [
        {
            icon: <VideoCameraOutlined />
        },
        {
            icon: <CheckCircleOutlined />
        }
    ];

    // Render current step content
    const renderStepContent = () => {
        switch (currentStep) {

            case 0:
                return (
                    <motion.div
                        initial={{ opacity: 0, x: 20 }}
                        animate={{ opacity: 1, x: 0 }}
                        exit={{ opacity: 0, x: -20 }}
                        transition={{ duration: 0.3 }}
                    >
                        <Card className="shadow-lg border-0">
                            <div className="text-center mb-6">
                                <div className="inline-flex items-center justify-center w-16 h-16 bg-green-100 rounded-full mb-4">
                                    <VideoCameraOutlined className="text-2xl text-green-600" />
                                </div>
                                <Title level={3} className="text-gray-800">Document Verification</Title>
                            </div>

                            <div className="mb-6">
                                <Form.Item label="Document Type" required>
                                    <Select
                                        size="large"
                                        value={selectedDocumentType}
                                        onChange={(value) => {
                                            setSelectedDocumentType(value);
                                            const docType = DocumentTypes[value];
                                            if (docType?.requiresLive) {
                                                setCaptureMode('live');
                                            }
                                        }}
                                        placeholder="Select document type"
                                    >
                                        {Object.values(DocumentTypes).map(docType => (
                                            <Option key={docType.value} value={docType.value}>
                                                <div className="flex items-center justify-between">
                                                    <span className="flex items-center">
                                                        {docType.icon}
                                                        <span className="ml-2">{docType.label}</span>
                                                    </span>
                                                    <span>
                                                        {docType.requiresLive && (
                                                            <Badge
                                                                count="Live"
                                                                style={{
                                                                    backgroundColor: '#52c41a',
                                                                    fontSize: '10px',
                                                                    height: '18px',
                                                                    lineHeight: '18px',
                                                                    padding: '0 6px'
                                                                }}
                                                            />
                                                        )}
                                                        {docType.requiresDuplex && (
                                                            <Badge
                                                                count="Duplex"
                                                                style={{
                                                                    backgroundColor: '#123456',
                                                                    fontSize: '10px',
                                                                    height: '18px',
                                                                    lineHeight: '18px',
                                                                    padding: '0 6px'
                                                                }}
                                                            />
                                                        )}
                                                    </span>
                                                </div>
                                            </Option>
                                        ))}
                                    </Select>
                                </Form.Item>
                            </div>

                            {/* Live Document Capture Component */}
                            {DocumentTypes[selectedDocumentType]?.requiresLive ? (
                                <div className="mb-6">
                                    <Alert
                                        message="Live Capture Required"
                                        description="This document type requires live camera capture for enhanced security. Please use the camera below to capture your document."
                                        type="info"
                                        showIcon
                                        icon={<VideoCameraOutlined />}
                                        className="mb-4"
                                    />
                                    {DocumentTypes[selectedDocumentType]?.requiresDuplex && (
                                        <Alert
                                            message="Duplex Capture Required"
                                            description="This document type requires fornt and back camera capture. Please capture both sides of the document."
                                            type="info"
                                            showIcon
                                            icon={<VideoCameraOutlined />}
                                            className="mb-4"
                                        />
                                    )}
                                    <LiveDocumentCapture
                                        documentType={selectedDocumentType}
                                        onCapture={handleLiveDocumentCapture}
                                        sessionId={sessionId}
                                        requiresDuplex={DocumentTypes[selectedDocumentType]?.requiresDuplex || false}
                                    />
                                </div>
                            ) : (
                                <div className="mb-6">
                                    <Upload.Dragger
                                        customRequest={handleDocumentUpload}
                                        showUploadList={false}
                                        accept="image/jpeg,image/png,image/webp,application/pdf"
                                        disabled={loading}
                                    >
                                        <p className="ant-upload-drag-icon">
                                            <UploadOutlined className="text-4xl text-blue-600" />
                                        </p>
                                        <p className="ant-upload-text text-lg">
                                            Click or drag files here to upload
                                        </p>
                                        <p className="ant-upload-hint text-gray-500">
                                            Supports: JPG, PNG, WebP, PDF (Max 10MB)
                                        </p>
                                    </Upload.Dragger>
                                </div>
                            )}

                            {documents.length > 0 && (
                                <div className="space-y-4">
                                    <Title level={4}>Processed Documents</Title>
                                    {documents.map((doc) => (
                                        <Card key={doc.id} size="small" className="border border-gray-200">
                                            <div className="flex items-center justify-between">
                                                <div className="flex items-center space-x-3">
                                                    <div className="w-12 h-12 bg-gray-100 rounded-lg flex items-center justify-center">
                                                        {doc.isLiveCapture ? (
                                                            <VideoCameraOutlined className="text-green-600" />
                                                        ) : (
                                                            <IdcardOutlined className="text-gray-600" />
                                                        )}
                                                    </div>
                                                    <div>
                                                        <div className="flex items-center space-x-2">
                                                            <Text className="font-medium">
                                                                {`${DocumentTypes[doc.type]?.label}`}
                                                            </Text>
                                                            {doc.isLiveCapture && (
                                                                <Badge
                                                                    count="Live"
                                                                    style={{
                                                                        backgroundColor: '#52c41a',
                                                                        fontSize: '10px'
                                                                    }}
                                                                />
                                                            )}
                                                        </div>
                                                        <div className="text-sm text-gray-500">
                                                            {doc.files &&
                                                                `${(doc.files.map(f => f.size).reduce((p, c) => p + c) / 1024 / 1024).toFixed(2)} MB`
                                                            }
                                                            {doc.captureData && (
                                                                (() => {
                                                                    // Calculate total size of all images in the live capture
                                                                    const totalSizeBytes = doc.captureData.imageData.reduce((total, imageItem) => {
                                                                        try {
                                                                            // Extract the base64 data portion (after the comma)
                                                                            const base64Data = imageItem.imageData.split(',')[1];
                                                                            // Calculate the actual byte size from base64
                                                                            // Base64 encoding increases size by ~33%, so we need to account for padding
                                                                            const paddingChars = (base64Data.match(/=/g) || []).length;
                                                                            const sizeInBytes = (base64Data.length * 3) / 4 - paddingChars;
                                                                            return total + sizeInBytes;
                                                                        } catch (error) {
                                                                            console.warn('Error calculating image size:', error);
                                                                            return total;
                                                                        }
                                                                    }, 0);

                                                                    // Convert to MB and format
                                                                    const sizeInMB = totalSizeBytes / (1024 * 1024);
                                                                    return `${sizeInMB.toFixed(2)} MB`;
                                                                })()
                                                            )}
                                                        </div>
                                                    </div>
                                                </div>
                                                <div className="flex items-center space-x-2">
                                                    <div className="flex items-center space-x-2">
                                                        <Tooltip title="Preview">
                                                            <Button
                                                                type="text"
                                                                icon={<EyeOutlined />}
                                                                onClick={() => setPreviewModal({
                                                                    visible: true,
                                                                    images: doc.preview,
                                                                    title: `${doc.type} (Live Capture)`
                                                                })}
                                                            />
                                                        </Tooltip>
                                                    </div>
                                                    <div className="flex items-center space-x-2">
                                                        <Tooltip title="Remove">
                                                            <Button
                                                                type="text"
                                                                icon={<CloseOutlined />}
                                                                onClick={() => handleRemoveDocument(doc.id)}
                                                                className="text-red-500 hover:text-red-700 hover:bg-red-50"
                                                            />
                                                        </Tooltip>
                                                    </div>
                                                </div>
                                            </div>
                                        </Card>
                                    ))}
                                </div>
                            )}

                            <div className="flex justify-end pt-6">
                                <Button
                                    type="primary"
                                    size="large"
                                    disabled={documents.length === 0}
                                    onClick={() => setCurrentStep(1)}
                                    className="bg-blue-600 hover:bg-blue-700"
                                >
                                    Continue to Review
                                </Button>
                            </div>
                        </Card>
                    </motion.div>
                );

            case 1:
                return (
                    <motion.div
                        initial={{ opacity: 0, x: 20 }}
                        animate={{ opacity: 1, x: 0 }}
                        exit={{ opacity: 0, x: -20 }}
                        transition={{ duration: 0.3 }}
                    >
                        <Card className="shadow-lg border-0">
                            <div className="text-center mb-6">
                                <div className="inline-flex items-center justify-center w-16 h-16 bg-green-100 rounded-full mb-4">
                                    <CheckCircleOutlined className="text-2xl text-green-600" />
                                </div>
                                <Title level={3} className="text-gray-800">Review & Submit</Title>
                                <Paragraph className="text-gray-600">
                                    Please review your information and submit for verification.
                                </Paragraph>
                            </div>
                            <div className="space-y-6">
                                {/* Documents section*/}
                                <Card size="small" title="Verified Documents" className="border border-gray-200">
                                    <Space direction="vertical" className="w-full">
                                        {documents.map((doc) => (
                                            <Card key={doc.id} size="small" className="border border-gray-200">
                                                <div className="flex items-center justify-between">
                                                    <div className="flex items-center space-x-3">
                                                        <div className="w-12 h-12 bg-gray-100 rounded-lg flex items-center justify-center">
                                                            {doc.isLiveCapture ? (
                                                                <VideoCameraOutlined className="text-green-600" />
                                                            ) : (
                                                                <IdcardOutlined className="text-gray-600" />
                                                            )}
                                                        </div>
                                                        <div>
                                                            <div className="flex items-center space-x-2">
                                                                <Text className="font-medium">
                                                                    {`${DocumentTypes[doc.type]?.label}`}
                                                                </Text>
                                                                {doc.isLiveCapture && (
                                                                    <Badge
                                                                        count="Live"
                                                                        style={{
                                                                            backgroundColor: '#52c41a',
                                                                            fontSize: '10px'
                                                                        }}
                                                                    />
                                                                )}
                                                            </div>
                                                        </div>
                                                    </div>
                                                    <div className="flex items-center space-x-2">
                                                        <div className="flex items-center space-x-2">
                                                            <Tooltip title="Preview">
                                                                <Button
                                                                    type="text"
                                                                    icon={<EyeOutlined />}
                                                                    onClick={() => setPreviewModal({
                                                                        visible: true,
                                                                        images: doc.preview,
                                                                        title: `${doc.type} (Live Capture)`
                                                                    })}
                                                                />
                                                            </Tooltip>
                                                        </div>
                                                    </div>
                                                </div>
                                            </Card>
                                        ))}
                                    </Space>
                                </Card>
                                {/* Terms and conditions section*/}
                                <Card size="small" title="Terms and Conditions" className="border border-gray-200">
                                    <Space direction="vertical" className="w-full">
                                        <Checkbox
                                            checked={consentGiven}
                                            onChange={(e) => setConsentGiven(e.target.checked)}
                                        >
                                            I consent to the collection and processing of my personal data for identity verification purposes.
                                        </Checkbox>
                                        <Checkbox
                                            checked={termsAccepted}
                                            onChange={(e) => setTermsAccepted(e.target.checked)}
                                        >
                                            I accept the terms and conditions and privacy policy.
                                        </Checkbox>
                                    </Space>
                                </Card>

                            </div>

                            <div className="flex justify-between pt-6">
                                <Button
                                    size="large"
                                    onClick={() => setCurrentStep(0)}
                                    className="text-gray-600 border-gray-300"
                                >
                                    Back
                                </Button>
                                <Button
                                    type="primary"
                                    size="large"
                                    loading={loading}
                                    disabled={!consentGiven || !termsAccepted}
                                    onClick={handleFinalSubmission}
                                    className="bg-green-600 hover:bg-green-700 border-green-600 hover:border-green-700"
                                >
                                    Submit Verification
                                </Button>
                            </div>
                        </Card>
                    </motion.div>
                );

            default:
                return null;
        }
    };

    return (
        <>
            <div className="max-w-6xl">
                <Card className="shadow-xl">
                    {/* Mobile Steps - Icons Only */}
                    <div className="block md:hidden">
                        <Steps
                            current={currentStep}
                            items={mobileSteps}
                            direction="horizontal"
                            size="small"
                            responsive={false}
                        />
                    </div>

                    {/* Desktop Steps - Icons + Titles */}
                    <div className="hidden md:block">
                        <Steps
                            current={currentStep}
                            items={desktopSteps}
                            direction="horizontal"
                            size="small"
                            labelPlacement="vertical"
                            responsive={false}
                        />
                    </div>
                </Card>

                <AnimatePresence mode="wait">
                    {renderStepContent()}
                </AnimatePresence>

                {error && (
                    <Alert
                        message="Verification Error"
                        description={error}
                        type="error"
                        closable
                        className="mt-4"
                        onClose={() => setError(null)}
                    />
                )}
            </div>

            {/* Preview Modal */}
            <Modal
                open={previewModal.visible}
                title={previewModal.title}
                footer={null}
                onCancel={() => setPreviewModal({ visible: false, images: [""], title: '' })}
            >
                {previewModal.images.map(i => (
                    <img
                        src={i}
                        alt="Document preview"
                        className="w-full h-auto"
                    />
                ))}

            </Modal>
        </>
    );
};

export default AdvancedVerification;