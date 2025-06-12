import React, { useState, useRef, useCallback } from 'react';
import Webcam from 'react-webcam';
import { Button, Steps, Card, Alert, Spin, Typography } from 'antd';
import {
    IdcardOutlined,
    UserOutlined,
    CameraOutlined,
    CheckCircleOutlined
} from '@ant-design/icons';
import kycService from '../../services/kycService';

const { Step } = Steps;
const { Title, Text } = Typography;

interface CustomKycVerificationProps {
    userId: string;
    sessionId: string;
    onComplete: () => void;
}

const CustomKycVerification: React.FC<CustomKycVerificationProps> = ({
    userId,
    sessionId,
    onComplete
}) => {
    const [currentStep, setCurrentStep] = useState(0);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [idCardImage, setIdCardImage] = useState<string | null>(null);
    const [selfieImage, setSelfieImage] = useState<string | null>(null);
    const [personData, setPersonData] = useState<any>({});
    const [processingProgress, setProcessingProgress] = useState(0);
    const webcamRef = useRef<Webcam>(null);

    const captureIdCard = useCallback(() => {
        setLoading(true);
        setProcessingProgress(0);
        setError(null);

        if (webcamRef.current) {
            const imageSrc = webcamRef.current.getScreenshot();
            setIdCardImage(imageSrc);
            setProcessingProgress(100);
            setCurrentStep(1);
        }
    }, [webcamRef]);

    const captureSelfie = useCallback(() => {
        if (webcamRef.current) {
            const imageSrc = webcamRef.current.getScreenshot();
            setSelfieImage(imageSrc);
            setCurrentStep(2);
        }
    }, [webcamRef]);

    const submitVerification = async () => {
        setLoading(true);

        try {
            // Check if images are available before submission
            if (!idCardImage || !selfieImage) {
                setError('Missing required images. Please capture both ID card and selfie.');
                setLoading(false);
                return;
            }

            // Prepare verification data
            const verificationData = {
                sessionId,
                userId,
                documentImage: idCardImage,
                selfieImage,
                personalInfo: personData,
                documentVerified: true,
                faceMatchConfidence: 0.95, // In a real implementation, calculate this
            };

            // Submit verification results using the kycService
            const response = await kycService.submitVerification(verificationData);

            if (!response.success) {
                throw new Error('Verification failed');
            }

            setCurrentStep(3);
            onComplete();
        } catch (error) {
            setError('Verification submission failed. Please try again.');
        } finally {
            setLoading(false);
        }
    };

    const renderWebcam = () => (
        <div className="flex justify-center my-4">
            <Webcam
                audio={false}
                ref={webcamRef}
                screenshotFormat="image/jpeg"
                className="border rounded"
                style={{ width: '100%', maxWidth: '500px' }}
            />
        </div>
    );

    const renderCapturedImage = (image: string | null) => (
        image && (
            <div className="flex justify-center my-4">
                <img
                    src={image}
                    alt="Captured"
                    className="border rounded"
                    style={{ width: '100%', maxWidth: '500px' }}
                />
            </div>
        )
    );

    const renderIdCardStep = () => (
        <div className="text-center">
            <Title level={4}>Capture ID Card</Title>
            <Text>Position your ID card clearly within the frame and ensure all text is visible.</Text>

            {!idCardImage ? (
                <>
                    {renderWebcam()}
                    <Button
                        type="primary"
                        icon={<CameraOutlined />}
                        onClick={captureIdCard}
                        loading={loading}
                    >
                        Capture ID Card
                    </Button>
                </>
            ) : (
                <>
                    {renderCapturedImage(idCardImage)}
                    {loading ? (
                        <div className="mt-4">
                            <Spin />
                            <Text className="ml-2">Processing document ({processingProgress}%)...</Text>
                        </div>
                    ) : (
                        <div className="mt-4">
                            <Button onClick={() => setIdCardImage(null)}>Retake</Button>
                            {/* Show extracted data for verification */}
                            {personData.firstName && (
                                <div className="mt-4 text-left p-4 border rounded bg-gray-50">
                                    <h4>Extracted Information:</h4>
                                    <p><strong>Name:</strong> {personData.firstName} {personData.lastName}</p>
                                    {personData.dateOfBirth && (
                                        <p><strong>Date of Birth:</strong> {personData.dateOfBirth}</p>
                                    )}
                                    {personData.documentNumber && (
                                        <p><strong>Document Number:</strong> {personData.documentNumber}</p>
                                    )}
                                    {personData.nationality && (
                                        <p><strong>Nationality:</strong> {personData.nationality}</p>
                                    )}
                                </div>
                            )}
                        </div>
                    )}
                </>
            )}
        </div>
    );

    const renderSelfieStep = () => (
        <div className="text-center">
            <Title level={4}>Take a Selfie</Title>
            <Text>Position your face clearly within the frame.</Text>

            {!selfieImage ? (
                <>
                    {renderWebcam()}
                    <Button
                        type="primary"
                        icon={<CameraOutlined />}
                        onClick={captureSelfie}
                    >
                        Capture Selfie
                    </Button>
                </>
            ) : (
                <>
                    {renderCapturedImage(selfieImage)}
                    <div className="mt-4">
                        <Button onClick={() => setSelfieImage(null)}>Retake</Button>
                        <Button
                            type="primary"
                            onClick={submitVerification}
                            loading={loading}
                            className="ml-4"
                        >
                            Submit Verification
                        </Button>
                    </div>
                </>
            )}
        </div>
    );

    const renderReviewStep = () => (
        <div className="text-center">
            <Title level={4}>Reviewing Your Verification</Title>
            <Spin size="large" />
            <Text className="block mt-4">We are processing your verification. This may take a moment...</Text>
        </div>
    );

    const renderCompleteStep = () => (
        <div className="text-center">
            <CheckCircleOutlined style={{ fontSize: 64, color: '#52c41a' }} />
            <Title level={4} className="mt-4">Verification Complete!</Title>
            <Text className="block">Thank you for completing the verification process.</Text>
        </div>
    );

    const renderCurrentStep = () => {
        switch (currentStep) {
            case 0: return renderIdCardStep();
            case 1: return renderSelfieStep();
            case 2: return renderReviewStep();
            case 3: return renderCompleteStep();
            default: return renderIdCardStep();
        }
    };

    return (
        <Card title="Custom KYC Verification" className="max-w-3xl mx-auto my-5">
            {error && (
                <Alert
                    message="Error"
                    description={error}
                    type="error"
                    closable
                    className="mb-4"
                    onClose={() => setError(null)}
                />
            )}

            <Steps current={currentStep} className="mb-8">
                <Step title="ID Document" icon={<IdcardOutlined />} />
                <Step title="Selfie" icon={<UserOutlined />} />
                <Step title="Processing" icon={<Spin size="small" />} />
                <Step title="Complete" icon={<CheckCircleOutlined />} />
            </Steps>

            {renderCurrentStep()}
        </Card>
    );
};

export default CustomKycVerification;