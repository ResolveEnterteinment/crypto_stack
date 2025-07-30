import React, { useState, useRef, useCallback, useMemo } from 'react';
import {
    Button, Steps, Card, Alert, Typography, Input, Form,
    Badge, Row, Col, Progress, Upload, message, Modal, Tooltip,
    Space, Checkbox, Select
} from 'antd';
import {
    IdcardOutlined, UserOutlined, CameraOutlined, CheckCircleOutlined,
    UploadOutlined, EyeOutlined, WarningOutlined, VideoCameraOutlined,
    CloseOutlined
} from '@ant-design/icons';
import { motion, AnimatePresence } from 'framer-motion';
import type { UploadProps } from 'antd/es/upload/interface';
import { Country } from 'country-state-city';
import kycService from "../../services/kycService";
import LiveDocumentCapture from './LiveDocumentCapture';
import { DocumentCaptureData, DocumentUpload, LiveDocumentCaptureRequest, LiveSelfieCaptureRequest, SelfieCaptureData, DocumentType, StandardPersonalInfo, StandardVerificationData } from '../../types/kyc';
import LiveSelfieCapture from './LiveSelfieCapture';

const { Title, Text, Paragraph } = Typography;
const { Option } = Select;

interface StandardVerificationProps {
    userId: string;
    sessionId: string;
    level: string;
    onSubmit: (data: any) => void;
}

// Document type options for live capture
const DocumentTypes: Record<string, DocumentType> = {
    passport: { value: 'passport', label: 'Passport', icon: <IdcardOutlined />, requiresLive: true, requiresDuplex: false },
    drivers_license: { value: 'drivers_license', label: 'Driver\'s License', icon: <IdcardOutlined />, requiresLive: true, requiresDuplex: true },
    national_id: { value: 'national_id', label: 'National ID', icon: <IdcardOutlined />, requiresLive: true, requiresDuplex: true }
};

const StandardVerification: React.FC<StandardVerificationProps> = ({
    sessionId,
    level,
    onSubmit
}) => {
    // State management
    const [currentStep, setCurrentStep] = useState(0);
    const [loading, setLoading] = useState(false);
    const [processingProgress, setProcessingProgress] = useState(0);
    const [error, setError] = useState<string | null>(null);
    const [personalInfo, setPersonalInfo] = useState<StandardPersonalInfo>({
        governmentIdNumber: '',
        nationality: '',
        phoneNumber: '',
        occupation: ''
    });

    const [documents, setDocuments] = useState<DocumentUpload[]>([]);
    const [selfieImage, setSelfieImage] = useState<string | null>(null);
    const [consentGiven, setConsentGiven] = useState(false);
    const [termsAccepted, setTermsAccepted] = useState(false);
    const [previewModal, setPreviewModal] = useState({ visible: false, images: [''], title: '' });
    const [selectedDocumentType, setSelectedDocumentType] = useState<'passport' | 'drivers_license' | 'national_id'>('national_id');
    const [captureMode, setCaptureMode] = useState<'upload' | 'live'>('live');

    const [form] = Form.useForm();

    // Get all countries using country-state-city package
    const countries = useMemo(() => {
        return Country.getAllCountries().map(country => ({
            value: country.isoCode,
            label: country.name,
            name: country.name
        }));
    }, []);

    const handleCountryChange = (countryCode: string) => {
        // Reset state field when country changes
        form.setFieldsValue({ state: undefined });

        const country = countries.find(c => c.value === countryCode);
        setPersonalInfo(prev => ({ ...prev, nationality: country?.name! }));
    };

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

    // Enhanced biometric capture with liveness detection
    const handleLiveSelfieCapture = useCallback(async (captureData: SelfieCaptureData) => {
        try {
            setLoading(true);

            const imageSrc = captureData.imageData.imageData;
            if (!imageSrc) {
                throw new Error('Failed to capture image');
            }
            setSelfieImage(imageSrc);

            var liveCaptureRequest: LiveSelfieCaptureRequest = {
                sessionId,
                imageData: captureData.imageData,
                isLive: true,
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
            const response = await kycService.submitLiveSelfieCapture(liveCaptureRequest);

            if (!response.success) {
                throw new Error('Live capture submission failed');
            }

            const result = response.data;
            message.success('Selfie captured successfully!');
        } catch (error) {
            console.error('Biometric capture error:', error);
            message.error('Biometric capture failed. Please try again.');
        } finally {
            setLoading(false);
        }
    }, []);

    // Enhanced form validation
    const validatePersonalInfo = async () => {
        try {
            await form.validateFields();

            // Additional security validations
            const { governmentIdNumber, nationality, occupation, phoneNumber } = personalInfo;

            // TO-DO: Add nationality and occupation validations

            // Document number validation (alphanumeric only)
            if (!governmentIdNumber || governmentIdNumber.trim().length === 0) {
                return Promise.reject(new Error('Document number is required'));
            }

            // Only allow alphanumeric characters
            const docRegex = /^[a-zA-Z0-9]+$/;
            if (!docRegex.test(governmentIdNumber)) {
                return Promise.reject(new Error('Only letters and numbers are allowed'));
            }

            if (governmentIdNumber.trim().length < 5 || governmentIdNumber.trim().length > 20) {
                return Promise.reject(new Error('Document number must be between 5 and 20 characters'));
            }

            return true;
        } catch (error) {
            message.error('Please fill in all required fields correctly');
            return false;
        }
    };

    const validatePhoneNumber = (_: any, value: string) => {
        if (!value) return Promise.resolve(); // Optional field

        const phoneRegex = /^\+?[\d\s\-()]+$/;
        if (!phoneRegex.test(value)) {
            return Promise.reject(new Error('Please enter a valid phone number'));
        }

        if (value.length < 10 || value.length > 20) {
            return Promise.reject(new Error('Phone number must be between 10 and 20 characters'));
        }

        return Promise.resolve();
    };

    const validateDocumentNumber = (_: any, value: string) => {
        if (!value || value.trim().length === 0) {
            return Promise.reject(new Error('Document number is required'));
        }

        // Only allow alphanumeric characters
        const docRegex = /^[a-zA-Z0-9]+$/;
        if (!docRegex.test(value)) {
            return Promise.reject(new Error('Only letters and numbers are allowed'));
        }

        if (value.length < 5 || value.length > 20) {
            return Promise.reject(new Error('Document number must be between 5 and 20 characters'));
        }

        return Promise.resolve();
    };

    const validateOccupation = (_: any, value: string) => {
        if (!value || value.trim().length === 0) {
            return Promise.reject(new Error('Occupation is required'));
        }

        // Only allow letters, spaces, hyphens, apostrophes, and periods
        const occupationRegex = /^[a-zA-Z\s\-'.]+$/;
        if (!occupationRegex.test(value)) {
            return Promise.reject(new Error('Occupation can only contain letters, spaces, hyphens, apostrophes, and periods'));
        }

        if (value.trim().length < 3 || value.trim().length > 50) {
            return Promise.reject(new Error('Occupation must be between 3 and 50 characters'));
        }

        return Promise.resolve();
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
            const submissionData: StandardVerificationData = {
                    personalInfo,
                    documents: documents.map(doc => ({
                        id: doc.id,
                        type: doc.type,
                        hash: doc.encryptedHash,
                        uploadDate: doc.uploadDate,
                        isLiveCapture: doc.isLiveCapture,
                    })),
                    selfieHash: selfieImage ? await computeImageHash(selfieImage) : null,
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
            title: 'Personal Information',
            icon: <UserOutlined />
        },
        {
            title: 'Document Verification',
            icon: <VideoCameraOutlined />
        },
        {
            title: 'Biometric Verification',
            icon: <CameraOutlined />
        },
        {
            title: 'Review & Submit',
            icon: <CheckCircleOutlined />
        }
    ];

    const mobileSteps = [
        {
            icon: <UserOutlined />
        },
        {
            icon: <VideoCameraOutlined />
        },
        {
            icon: <CameraOutlined />
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
                                <div className="inline-flex items-center justify-center w-16 h-16 bg-blue-100 rounded-full mb-4">
                                    <UserOutlined className="text-2xl text-blue-600" />
                                </div>
                                <Title level={3} className="text-gray-800">Personal Information</Title>
                                <Paragraph className="text-gray-600">
                                    Please provide accurate information as it appears on your official documents.
                                </Paragraph>
                            </div>

                            <Form
                                form={form}
                                layout="vertical"
                                onFinish={async () => {
                                    const isValid = await validatePersonalInfo();
                                    if (isValid) {
                                        setCurrentStep(1);
                                    }
                                }}
                                className="space-y-4"
                            >
                                <Row gutter={16}>
                                    <Col xs={24} md={12}>
                                        <Form.Item
                                            name="nationality"
                                            label="Nationality"
                                            rules={[
                                                { required: true, message: 'Please select your nationality' }
                                            ]}
                                        >
                                            <Select
                                                placeholder="Select your nationality"
                                                showSearch
                                                filterOption={(input, option) =>
                                                    (option?.children as unknown as string)?.toLowerCase().includes(input.toLowerCase())
                                                }
                                                onChange={handleCountryChange}
                                            >
                                                {countries.map(country => (
                                                    <Option key={country.value} value={country.value}>
                                                        {country.label}
                                                    </Option>
                                                ))}
                                            </Select>
                                        </Form.Item>

                                        <Form.Item
                                            label="Government ID Number"
                                            name="governmentIdNumber"
                                            rules={[
                                                { required: true, message: 'Please enter your government ID number' },
                                                { min: 5, message: 'Government ID number must be at least 5 characters' },
                                                { validator: validateDocumentNumber }
                                            ]}
                                        >
                                            <Input
                                                size="large"
                                                placeholder="Enter government ID number"
                                                onChange={(e) => setPersonalInfo(prev => ({ ...prev, governmentIdNumber: e.target.value }))}
                                            />
                                        </Form.Item>
                                    </Col>
                                </Row>

                                <Form.Item
                                    label="Phone Number"
                                    name="phoneNumber"
                                    rules={[
                                        { required: true, message: 'Please enter your phone number' },
                                        { validator: validatePhoneNumber },
                                    ]}
                                >
                                    <Input
                                        size="large"
                                        placeholder="Enter your phone number"
                                        onChange={(e) => setPersonalInfo(prev => ({ ...prev, phoneNumber: e.target.value }))}
                                    />
                                </Form.Item>

                                <Form.Item
                                    label="Occupation"
                                    name="occupation"
                                    rules={[
                                        { required: true, message: 'Please enter your occupation' },
                                        { validator: validateOccupation },
                                    ]}
                                >
                                    <Input
                                        size="large"
                                        placeholder="Enter your occupation"
                                        onChange={(e) => setPersonalInfo(prev => ({ ...prev, occupation: e.target.value }))}
                                    />
                                </Form.Item>

                                <div className="flex justify-end pt-4">
                                    <Button
                                        type="primary"
                                        size="large"
                                        htmlType="submit"
                                        className="bg-blue-600 hover:bg-blue-700 border-blue-600 hover:border-blue-700"
                                    >
                                        Continue to Documents
                                    </Button>
                                </div>
                            </Form>
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
                                    disabled={documents.length === 0}
                                    onClick={() => setCurrentStep(2)}
                                    className="bg-blue-600 hover:bg-blue-700"
                                >
                                    Continue to Biometric Verification
                                </Button>
                            </div>
                        </Card>
                    </motion.div>
                );

            case 2:
                return (
                    <motion.div
                        initial={{ opacity: 0, x: 20 }}
                        animate={{ opacity: 1, x: 0 }}
                        exit={{ opacity: 0, x: -20 }}
                        transition={{ duration: 0.3 }}
                    >
                        <Card className="shadow-lg border-0">
                            <div className="text-center mb-6">
                                <div className="inline-flex items-center justify-center w-16 h-16 bg-purple-100 rounded-full mb-4">
                                    <CameraOutlined className="text-2xl text-purple-600" />
                                </div>
                                <Title level={3} className="text-gray-800">Biometric Verification</Title>
                                <Paragraph className="text-gray-600">
                                    Take a clear photo of yourself for identity verification.
                                </Paragraph>
                            </div>

                            <div className="bg-amber-50 rounded-lg p-4 mb-6">
                                <div className="flex items-center space-x-2">
                                    <WarningOutlined className="text-amber-600" />
                                    <Text className="text-amber-800 font-medium">Important Instructions</Text>
                                </div>
                                <ul className="text-amber-700 mt-2 space-y-1">
                                    <li>• Ensure good lighting on your face</li>
                                    <li>• Look directly at the camera</li>
                                    <li>• Remove sunglasses or hats</li>
                                    <li>• Keep your face centered in the frame</li>
                                </ul>
                            </div>

                            <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
                                <LiveSelfieCapture
                                    onCapture={handleLiveSelfieCapture}
                                />
                                <div>
                                    <Text className="font-medium mb-3 block">Captured Photo</Text>
                                    <div className="bg-gray-100 rounded-lg h-[300px] flex items-center justify-center">
                                        {selfieImage ? (
                                            <img
                                                src={selfieImage}
                                                alt="Captured selfie"
                                                className="max-w-full max-h-full object-contain rounded-lg"
                                            />
                                        ) : (
                                            <div className="text-center text-gray-500">
                                                <CameraOutlined className="text-4xl mb-2" />
                                                <Text>No photo captured yet</Text>
                                            </div>
                                        )}
                                    </div>
                                </div>
                            </div>

                            {loading && (
                                <div className="mt-6">
                                    <Progress
                                        percent={processingProgress}
                                        status="active"
                                        strokeColor={{
                                            '0%': '#108ee9',
                                            '100%': '#87d068',
                                        }}
                                    />
                                    <Text className="block text-center mt-2 text-gray-600">
                                        Processing biometric data...
                                    </Text>
                                </div>
                            )}

                            <div className="flex justify-between pt-6">
                                <Button
                                    size="large"
                                    onClick={() => setCurrentStep(1)}
                                    className="text-gray-600 border-gray-300"
                                >
                                    Back
                                </Button>
                                <Button
                                    type="primary"
                                    size="large"
                                    disabled={!selfieImage /*|| !securityValidation.faceMatch || !securityValidation.livenessDetected*/}
                                    onClick={() => setCurrentStep(3)}
                                    className="bg-blue-600 hover:bg-blue-700"
                                >
                                    Continue to Review
                                </Button>
                            </div>
                        </Card>
                    </motion.div>
                );

            case 3:
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
                                {/* Personal Information section*/}
                                <Card size="small" title="Personal Information" className="border border-gray-200">
                                    <Row gutter={16}>
                                        <Col xs={24} md={12}>
                                            <Text className="text-gray-600">Government ID Number: </Text>
                                            <Text className="font-medium">{personalInfo.governmentIdNumber}</Text>
                                        </Col>
                                        <Col xs={24} md={12}>
                                            <Text className="text-gray-600">Nationality: </Text>
                                            <Text className="font-medium">{personalInfo.nationality}</Text>
                                        </Col>
                                        <Col xs={24} md={12}>
                                            <Text className="text-gray-600">Occupation: </Text>
                                            <Text className="font-medium">{personalInfo.occupation}</Text>
                                        </Col>
                                        <Col xs={24} md={12}>
                                            <Text className="text-gray-600">Phone Number: </Text>
                                            <Text className="font-medium">{personalInfo.phoneNumber}</Text>
                                        </Col>
                                    </Row>
                                </Card>
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
                                {/* Selfie section*/}
                                <Card size="small" title="Biometric Verification" className="border border-gray-200">
                                    <div className="flex items-center space-x-4">
                                        <div className="w-24 h-24 bg-gray-100 rounded-lg overflow-hidden">
                                            {selfieImage ? (
                                                <img
                                                    src={selfieImage}
                                                    alt="Your selfie"
                                                    className="w-full h-full object-cover"
                                                />
                                            ) : (
                                                <div className="w-full h-full flex items-center justify-center text-gray-400">
                                                    <CameraOutlined className="text-2xl" />
                                                </div>
                                            )}
                                        </div>
                                    </div>
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
                                    onClick={() => setCurrentStep(2)}
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

export default StandardVerification;