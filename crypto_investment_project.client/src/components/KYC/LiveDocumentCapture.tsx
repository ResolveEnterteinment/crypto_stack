import React, { useState, useRef, useCallback, useEffect } from 'react';
import Webcam from 'react-webcam';
import { Button, Card, Typography, Alert, Progress, Space, Steps } from 'antd';
import { CameraOutlined, CheckCircleOutlined, SwapOutlined } from '@ant-design/icons';
import { DocumentCaptureData, ImageData } from '../../types/kyc';
import DocumentOverlay from './DocumentOverlay';

const { Text } = Typography;
const { Step } = Steps;

interface LiveDocumentCaptureProps {
    documentType: 'passport' | 'drivers_license' | 'national_id';
    onCapture: (captureData: DocumentCaptureData) => Promise<void>;
    sessionId: string;
    requiresDuplex?: boolean; // Add this prop
}

const LiveDocumentCapture: React.FC<LiveDocumentCaptureProps> = ({
    documentType,
    onCapture,
    sessionId,
    requiresDuplex = false
}) => {
    const webcamRef = useRef<Webcam>(null);
    const webcamContainerRef = useRef<HTMLDivElement>(null);
    const [isCapturing, setIsCapturing] = useState(false);
    const [processingProgress, setProcessingProgress] = useState(0);
    const [captureResult, setCaptureResult] = useState<DocumentCaptureData | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [webcamDimensions, setWebcamDimensions] = useState({ width: 0, height: 0 });

    // Duplex capture state
    const [currentSide, setCurrentSide] = useState<'front' | 'back'>('front');
    const [frontCapture, setFrontCapture] = useState<ImageData | null>(null);
    const [backCapture, setBackCapture] = useState<ImageData | null>(null);
    const [captureStep, setCaptureStep] = useState(0);

    // Track webcam container dimensions to properly constrain overlay
    useEffect(() => {
        const updateWebcamDimensions = () => {
            if (webcamContainerRef.current) {
                const rect = webcamContainerRef.current.getBoundingClientRect();
                setWebcamDimensions({
                    width: rect.width,
                    height: rect.height
                });
            }
        };

        // Initial measurement
        updateWebcamDimensions();

        // Add resize listener with debouncing
        let resizeTimeout: NodeJS.Timeout;
        const handleResize = () => {
            clearTimeout(resizeTimeout);
            resizeTimeout = setTimeout(updateWebcamDimensions, 100);
        };

        window.addEventListener('resize', handleResize);
        window.addEventListener('orientationchange', () => {
            setTimeout(updateWebcamDimensions, 300);
        });

        // Use ResizeObserver for more accurate tracking
        let resizeObserver: ResizeObserver | null = null;
        if (window.ResizeObserver && webcamContainerRef.current) {
            resizeObserver = new ResizeObserver(updateWebcamDimensions);
            resizeObserver.observe(webcamContainerRef.current);
        }

        // Cleanup
        return () => {
            clearTimeout(resizeTimeout);
            window.removeEventListener('resize', handleResize);
            window.removeEventListener('orientationchange', handleResize);
            if (resizeObserver) {
                resizeObserver.disconnect();
            }
        };
    }, []);

    // Enhanced capture with real-time analysis
    const captureDocument = useCallback(async () => {
        if (!webcamRef.current) return;

        setIsCapturing(true);
        setProcessingProgress(0);
        setError(null);

        try {
            // Step 1: Capture high-quality image
            setProcessingProgress(20);
            const imageSrc = webcamRef.current.getScreenshot({
                width: 1920,
                height: 1080
            });

            if (!imageSrc) {
                throw new Error('Failed to capture image');
            }

            // Step 2: Generate device fingerprint for authenticity
            setProcessingProgress(40);
            const deviceFingerprint = await generateDeviceFingerprint();

            // Step 3: Analyze image quality and detect tampering
            setProcessingProgress(60);
            const qualityAnalysis = await analyzeImageQuality(imageSrc);
            const livenessDetection = await detectLiveCapture(imageSrc);

            // Step 4: Extract environmental factors
            setProcessingProgress(80);
            const environmentalFactors = await getEnvironmentalFactors();

            const imageData: ImageData = {
                side: currentSide,
                imageData: imageSrc,
                isLive: livenessDetection.isLive,
            }
            // Step 5: Compile capture data
            setProcessingProgress(100);
            const captureData: DocumentCaptureData = {
                documentType: documentType,
                isLive: livenessDetection.isLive,
                imageData: [],
                captureMetadata: {
                    timestamp: Date.now(),
                    deviceFingerprint,
                    cameraProperties: await getCameraProperties(),
                    environmentalFactors
                },
            };

            captureData.imageData.push(imageData);

            setCaptureResult(captureData);

            if (qualityAnalysis.score >= 70 && livenessDetection.isLive && livenessDetection.confidence >= 0.8) {
                if (requiresDuplex) {
                    await handleDuplexCapture(captureData);
                } else {
                    await onCapture(captureData);
                }
            } else {
                setError('Document capture quality insufficient. Please ensure good lighting and hold document steady.');
            }

        } catch (error: any) {
            console.error('Capture error:', error);

            // Handle specific error types
            if (error.message?.includes('EXIF data')) {
                setError('Camera capture validation failed. Please try again or use a different device.');
            } else if (error.message?.includes('LIVE_CAPTURE_VALIDATION_FAILED')) {
                setError('Live capture validation failed. Please ensure you are using a real camera and try again.');
            } else {
                setError('Failed to capture document. Please try again.');
            }
        } finally {
            setIsCapturing(false);
            setProcessingProgress(0);
        }
    }, [onCapture, currentSide, requiresDuplex, documentType]);

    // Handle duplex capture workflow
    const handleDuplexCapture = async (captureData: DocumentCaptureData) => {
        console.log("LiveDocumentCapture::handleDuplexCapture => captureData: ", captureData);
        if (currentSide === 'front') {
            setFrontCapture(captureData.imageData[0]);
            setCurrentSide('back');
            setCaptureStep(1);
        } else {
            setBackCapture(captureData.imageData[0]);
            setCaptureStep(2);

            // Submit both sides
            const duplexCaptureData: DocumentCaptureData = {
                documentType: captureData.documentType,
                isLive: frontCapture?.isLive! || backCapture?.isLive!,
                isDuplex: true,
                imageData: [
                    frontCapture!,
                    captureData.imageData[0]!
                ],
                captureMetadata: captureData.captureMetadata,
            };

            await onCapture(duplexCaptureData);
        }
    };

    // Reset duplex capture
    const resetDuplexCapture = () => {
        setFrontCapture(null);
        setBackCapture(null);
        setCurrentSide('front');
        setCaptureStep(0);
        setCaptureResult(null);
        setError(null);
    };

    // Generate unique device fingerprint
    const generateDeviceFingerprint = async (): Promise<string> => {
        const canvas = document.createElement('canvas');
        const ctx = canvas.getContext('2d');
        if (ctx) {
            ctx.textBaseline = 'top';
            ctx.font = '14px Arial';
            ctx.fillText('Device fingerprint', 2, 2);
        }

        const fingerprint = {
            userAgent: navigator.userAgent,
            language: navigator.language,
            platform: navigator.platform,
            timezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
            screen: `${screen.width}x${screen.height}`,
            canvas: canvas.toDataURL(),
            timestamp: Date.now()
        };

        const encoder = new TextEncoder();
        const data = encoder.encode(JSON.stringify(fingerprint));
        const hashBuffer = await crypto.subtle.digest('SHA-256', data);
        const hashArray = Array.from(new Uint8Array(hashBuffer));
        return hashArray.map(b => b.toString(16).padStart(2, '0')).join('');
    };

    // Analyze image quality for document verification
    const analyzeImageQuality = async (imageData: string): Promise<{ score: number; issues: string[] }> => {
        return new Promise((resolve) => {
            const img = new Image();
            img.onload = () => {
                const canvas = document.createElement('canvas');
                const ctx = canvas.getContext('2d');
                if (!ctx) {
                    resolve({ score: 0, issues: ['Failed to analyze image'] });
                    return;
                }

                canvas.width = img.width;
                canvas.height = img.height;
                ctx.drawImage(img, 0, 0);

                const imageData = ctx.getImageData(0, 0, canvas.width, canvas.height);
                const data = imageData.data;

                // Calculate brightness and contrast
                let totalBrightness = 0;
                let brightnessSamples = 0;

                for (let i = 0; i < data.length; i += 16) { // Sample every 4th pixel
                    const brightness = (data[i] + data[i + 1] + data[i + 2]) / 3;
                    totalBrightness += brightness;
                    brightnessSamples++;
                }

                const avgBrightness = totalBrightness / brightnessSamples;
                const issues: string[] = [];
                let score = 100;

                // Quality checks
                if (avgBrightness < 50) {
                    issues.push('Image too dark');
                    score -= 30;
                } else if (avgBrightness > 200) {
                    issues.push('Image too bright');
                    score -= 20;
                }

                if (img.width < 800 || img.height < 600) {
                    issues.push('Resolution too low');
                    score -= 25;
                }

                resolve({ score: Math.max(0, score), issues });
            };
            img.src = imageData;
        });
    };

    // Detect if image is from live camera vs pre-captured
    const detectLiveCapture = async (imageData: string): Promise<{
        isLive: boolean;
        confidence: number;
    }> => {
        // Simulate advanced tamper detection
        const flags: string[] = [];
        let confidence = 1;

        // Check for typical signs of pre-captured images
        const img = new Image();
        return new Promise((resolve) => {
            img.onload = () => {
                // Check for compression artifacts typical of saved images
                const canvas = document.createElement('canvas');
                const ctx = canvas.getContext('2d');
                if (ctx) {
                    canvas.width = img.width;
                    canvas.height = img.height;
                    ctx.drawImage(img, 0, 0);

                    // Analyze for JPEG compression patterns
                    const imageData = ctx.getImageData(0, 0, canvas.width, canvas.height);

                    // Simplified analysis - in production, use ML models
                    const hasUniformNoise = analyzeNoisePattern(imageData);
                    if (!hasUniformNoise) {
                        flags.push('Suspicious noise pattern');
                        confidence -= 0.2;
                    }
                }

                resolve({
                    isLive: confidence > 0.7,
                    confidence
                });
            };
            img.src = imageData;
        });
    };

    const analyzeNoisePattern = (imageData: globalThis.ImageData): boolean => {
        // Simplified noise analysis
        const data = imageData.data;
        let variations = 0;

        for (let i = 0; i < data.length - 4; i += 4) {
            const diff = Math.abs(data[i] - data[i + 4]);
            if (diff > 5) variations++;
        }

        return variations / (data.length / 4) > 0.1; // Expect some natural camera noise
    };

    const getEnvironmentalFactors = async () => {
        return {
            timestamp: Date.now(),
            userAgent: navigator.userAgent,
            viewport: {
                width: window.innerWidth,
                height: window.innerHeight
            }
        };
    };

    const getCameraProperties = async () => {
        try {
            const stream = webcamRef.current?.stream;
            if (stream) {
                const track = stream.getVideoTracks()[0];
                const settings = track.getSettings();
                return {
                    width: settings.width,
                    height: settings.height,
                    frameRate: settings.frameRate,
                    facingMode: settings.facingMode
                };
            }
        } catch (error) {
            console.error('Error getting camera properties:', error);
        }
        return {};
    };

    const getCardTitle = () => {
        if (!requiresDuplex) {
            return `Live ${documentType.replace('_', ' ')} capture`;
        }

        return `Live ${documentType.replace('_', ' ')} capture - ${currentSide === 'front' ? 'Front' : 'Back'} side`;
    };

    const getInstructions = () => {
        if (!requiresDuplex) {
            return 'Position your document clearly in the frame and click capture';
        }

        if (currentSide === 'front') {
            return 'First, capture the front side of your document';
        } else {
            return 'Now flip your document and capture the back side';
        }
    };

    return (
        <Card title={getCardTitle()} className="live-capture-card">
            <Space direction="vertical" style={{ width: '100%' }}>
                {requiresDuplex && (
                    <div>
                        <Steps current={captureStep} size="small">
                            <Step
                                title="Front Side"
                                icon={frontCapture ? <CheckCircleOutlined /> : <CameraOutlined />}
                            />
                            <Step
                                title="Back Side"
                                icon={backCapture ? <CheckCircleOutlined /> : <SwapOutlined />}
                            />
                            <Step title="Complete" icon={<CheckCircleOutlined />} />
                        </Steps>
                        <div style={{ marginTop: 16, marginBottom: 16 }}>
                            <Alert
                                message={getInstructions()}
                                type="info"
                                showIcon
                            />
                        </div>
                    </div>
                )}

                <div
                    ref={webcamContainerRef}
                    style={{
                        position: 'relative',
                        textAlign: 'center',
                        overflow: 'hidden'
                    }}
                >
                    <Webcam
                        ref={webcamRef}
                        audio={false}
                        screenshotFormat="image/jpeg"
                        screenshotQuality={0.95}
                        width="100%"
                        height="auto"
                        style={{
                            border: '2px dashed #1890ff',
                            borderRadius: 8,
                        }}
                        videoConstraints={{
                            facingMode: 'environment',
                            width: { ideal: 1920 },
                            height: { ideal: 1080 },
                            aspectRatio: { ideal: 16 / 9 }
                        }}
                    />
                    {webcamDimensions.width > 0 && webcamDimensions.height > 0 && (
                        <DocumentOverlay
                            documentType={documentType}
                            isCapturing={isCapturing}
                            showQualityIndicators={false}
                            containerWidth={webcamDimensions.width}
                            containerHeight={webcamDimensions.height}
                        />
                    )}
                </div>

                {isCapturing && (
                    <div>
                        <Progress
                            percent={processingProgress}
                            status="active"
                            strokeColor={{
                                '0%': '#108ee9',
                                '100%': '#87d068',
                            }}
                        />
                        <Text>Processing live capture...</Text>
                    </div>
                )}

                {error && (
                    <Alert
                        message="Capture Failed"
                        description={error}
                        type="error"
                        showIcon
                        closable
                        onClose={() => setError(null)}
                    />
                )}

                <div style={{ display: 'flex', gap: '8px', justifyContent: 'center' }}>
                    <Button
                        type="primary"
                        size="large"
                        icon={<CameraOutlined />}
                        loading={isCapturing}
                        onClick={captureDocument}
                        disabled={isCapturing}
                    >
                        {isCapturing ? 'Processing...' : `Capture ${currentSide} side`}
                    </Button>

                    {requiresDuplex && (frontCapture || backCapture) && (
                        <Button
                            size="large"
                            onClick={resetDuplexCapture}
                            disabled={isCapturing}
                        >
                            Reset
                        </Button>
                    )}
                </div>

                {requiresDuplex && frontCapture && !backCapture && (
                    <Alert
                        message="Front side captured successfully!"
                        description="Please flip your document and capture the back side."
                        type="success"
                        showIcon
                    />
                )}
            </Space>
        </Card>
    );
};

export default LiveDocumentCapture;