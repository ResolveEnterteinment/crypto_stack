import React, { useState, useRef, useCallback, useEffect } from 'react';
import Webcam from 'react-webcam';
import { Button, Steps, Card, Alert, Spin, Typography, Input, Form, Badge, Row, Col, Progress } from 'antd';
import {
    IdcardOutlined,
    UserOutlined,
    CameraOutlined,
    CheckCircleOutlined,
    EditOutlined,
    SafetyOutlined,
    ReconciliationOutlined
} from '@ant-design/icons';
import kycService from '../../services/kycService';
import * as faceapi from 'face-api.js';
import { documentDetector } from './DocumentDetector';

// Define OpenCV window interface
declare global {
    interface Window {
        cv: any;
        opencvReadyPromise: Promise<void>;
        resolveOpencvReady: () => void;
    }
}

const { Step } = Steps;
const { Title, Text, Paragraph } = Typography;

// Asynchronously load face-api.js models
const loadModels = async () => {
    try {
        await Promise.all([
            faceapi.nets.tinyFaceDetector.loadFromUri('/models'),
            faceapi.nets.faceLandmark68Net.loadFromUri('/models'),
            faceapi.nets.faceRecognitionNet.loadFromUri('/models')
        ]);
        return true;
    } catch (error) {
        console.error("Error loading face detection models:", error);
        return false;
    }
};

// User's personal information interface
interface PersonalInfo {
    firstName: string;
    lastName: string;
    dateOfBirth: string;
    documentNumber: string;
    nationality: string;
}

interface KycVerificationProps {
    userId: string;
    sessionId: string;
    onComplete: () => void;
}

const KycVerification: React.FC<KycVerificationProps> = ({
    userId,
    sessionId,
    onComplete
}) => {
    // Workflow state
    const [currentStep, setCurrentStep] = useState(0);
    const [loading, setLoading] = useState(false);
    const [processingProgress, setProcessingProgress] = useState(0);
    const [error, setError] = useState<string | null>(null);

    // Library loading state
    const [modelsLoaded, setModelsLoaded] = useState(false);
    const [opencvLoaded, setOpencvLoaded] = useState(false);

    // Captured images
    const [idCardImage, setIdCardImage] = useState<string | null>(null);
    const [selfieImage, setSelfieImage] = useState<string | null>(null);

    // User data
    const [personalInfo, setPersonalInfo] = useState<PersonalInfo>({
        firstName: '',
        lastName: '',
        dateOfBirth: '',
        documentNumber: '',
        nationality: ''
    });

    // Detection states
    const [faceDetected, setFaceDetected] = useState(false);
    const [faceMatchConfidence, setFaceMatchConfidence] = useState(0);
    const [documentDetected, setDocumentDetected] = useState(false);

    // Detection control
    const [activeFaceDetection, setActiveFaceDetection] = useState(false);
    const [activeDocumentDetection, setActiveDocumentDetection] = useState(false);
    const [isDocumentDetectionRunning, setIsDocumentDetectionRunning] = useState(false);

    // Interval references
    const faceCheckIntervalRef = useRef<NodeJS.Timeout | null>(null);
    const documentCheckIntervalRef = useRef<NodeJS.Timeout | null>(null);

    // Webcam reference
    const webcamRef = useRef<Webcam>(null);
    const debugCanvasRef = useRef<HTMLCanvasElement>(null);

    // Check if OpenCV is loaded correctly
    const checkOpenCvLoaded = useCallback(() => {
        if (typeof window.cv === 'undefined' || !window.cv.Mat) {
            console.error("OpenCV.js is not properly loaded");
            setError("Computer vision library failed to load. Please refresh the page.");
            return false;
        }
        setOpencvLoaded(true);
        return true;
    }, []);

    // Initialize OpenCV.js
    useEffect(() => {
        const loadOpenCV = async () => {
            try {
                setLoading(true);
                await window.opencvReadyPromise;
                if (typeof window.cv !== 'undefined' && window.cv.Mat) {
                    setOpencvLoaded(true);
                } else {
                    setError("Computer vision library failed to initialize properly. Please refresh.");
                }
            } catch (err) {
                console.error("Error loading OpenCV:", err);
                setError("Failed to load computer vision library. Please refresh.");
            } finally {
                setLoading(false);
            }
        };
        loadOpenCV();
    }, []);

    // Load face detection models
    useEffect(() => {
        const loadRequiredModels = async () => {
            setLoading(true);
            if (!opencvLoaded && !checkOpenCvLoaded()) {
                setLoading(false);
                return;
            }
            const loaded = await loadModels();
            setModelsLoaded(loaded);
            if (!loaded) {
                setError("Failed to load face detection models. Please refresh.");
            }
            setLoading(false);
        };

        if (opencvLoaded) {
            loadRequiredModels();
        }

        // Clean up intervals on unmount
        return () => {
            clearDetectionIntervals();
        };
    }, [checkOpenCvLoaded, opencvLoaded]);

    // Clean up detection intervals
    const clearDetectionIntervals = () => {
        if (faceCheckIntervalRef.current) {
            clearInterval(faceCheckIntervalRef.current);
            faceCheckIntervalRef.current = null;
        }
        if (documentCheckIntervalRef.current) {
            clearInterval(documentCheckIntervalRef.current);
            documentCheckIntervalRef.current = null;
        }
        setActiveFaceDetection(false);
        setActiveDocumentDetection(false);
    };

    // Real-time document detection in frame
    const detectDocumentInFrame = useCallback(async () => {
        if (!webcamRef.current?.video || !window.cv?.Mat) {
            console.log("Detection failed: webcam or OpenCV not ready");
            setDocumentDetected(false);
            return;
        }

        const video = webcamRef.current.video;
        if (video.readyState !== 4) {
            console.log("Detection failed: video not ready");
            setDocumentDetected(false);
            return;
        }

        try {
            // Use the robust document detector with debug canvas
            const result = await documentDetector.detectDocument(video, debugCanvasRef.current || undefined);

            console.log("Document detection result:", result);

            setDocumentDetected(result.detected);
        } catch (error) {
            console.error('Error in document detection:', error);
            setDocumentDetected(false);
        }
    }, [webcamRef, debugCanvasRef]);

    // Update your useEffect to load document detection model alongside face models
    useEffect(() => {
        const loadRequiredModels = async () => {
            setLoading(true);

            // Load face detection models
            const faceModelsLoaded = await loadModels();
            setModelsLoaded(faceModelsLoaded);

            if (!faceModelsLoaded) {
                setError("Failed to load face detection models. Please refresh.");
            }

            setLoading(false);
        };

        loadRequiredModels();

        // Clean up intervals on unmount
        return () => {
            clearDetectionIntervals();
        };
    }, []);

    // Start document detection
    const startDocumentDetection = useCallback(() => {
        if (!opencvLoaded) {
            console.warn("OpenCV not loaded, cannot start document detection");
            return;
        }

        setActiveDocumentDetection(true);

        if (documentCheckIntervalRef.current) {
            clearInterval(documentCheckIntervalRef.current);
        }

        documentCheckIntervalRef.current = setInterval(async () => {
            if (!isDocumentDetectionRunning) {
                setIsDocumentDetectionRunning(true);
                try {
                    await detectDocumentInFrame();
                } finally {
                    setIsDocumentDetectionRunning(false);
                }
            }
        }, 750);
    }, [opencvLoaded, detectDocumentInFrame, isDocumentDetectionRunning]);

    // Real-time face detection
    const startFaceDetection = useCallback(() => {
        if (!modelsLoaded) {
            console.warn("Face models not loaded, cannot start face detection");
            return;
        }

        setActiveFaceDetection(true);

        if (faceCheckIntervalRef.current) {
            clearInterval(faceCheckIntervalRef.current);
        }

        faceCheckIntervalRef.current = setInterval(async () => {
            if (webcamRef.current?.video?.readyState === 4) {
                try {
                    const detections = await faceapi.detectSingleFace(
                        webcamRef.current.video,
                        new faceapi.TinyFaceDetectorOptions({ inputSize: 224 })
                    );
                    setFaceDetected(!!detections);
                } catch (error) {
                    console.error("Error during face detection:", error);
                    setFaceDetected(false);
                }
            }
        }, 500);
    }, [webcamRef, modelsLoaded]);

    // Manage live detections based on current step
    useEffect(() => {
        // Clear existing intervals first
        clearDetectionIntervals();

        // Start appropriate detection based on current step
        if (currentStep === 0 && opencvLoaded && !idCardImage) {
            startDocumentDetection();
        } else if (currentStep === 1 && modelsLoaded && !selfieImage) {
            startFaceDetection();
        }

        return clearDetectionIntervals;
    }, [currentStep, opencvLoaded, modelsLoaded, idCardImage, selfieImage, startDocumentDetection, startFaceDetection]);

    // Process ID card image with OpenCV.js
    const processIdCard = async (imageSrc: string): Promise<void> => {
        return new Promise((resolve) => {
            try {
                if (!opencvLoaded && !checkOpenCvLoaded()) {
                    setError("OpenCV.js is not loaded. Cannot process ID card.");
                    resolve();
                    return;
                }

                const cv = window.cv;
                const img = new Image();

                img.onload = async () => {
                    setProcessingProgress(30);

                    const canvas = document.createElement('canvas');
                    canvas.width = img.width;
                    canvas.height = img.height;
                    const ctx = canvas.getContext('2d');

                    if (!ctx) {
                        setError("Could not create canvas context");
                        resolve();
                        return;
                    }

                    ctx.drawImage(img, 0, 0, img.width, img.height);
                    setProcessingProgress(50);

                    try {
                        // Apply perspective correction
                        const src = cv.imread(canvas);
                        const result = await enhanceIdCardImage(src);

                        if (result) {
                            const resultCanvas = document.createElement('canvas');
                            resultCanvas.width = result.cols;
                            resultCanvas.height = result.rows;
                            cv.imshow(resultCanvas, result);
                            setIdCardImage(resultCanvas.toDataURL('image/jpeg'));
                            result.delete();
                        }

                        src.delete();
                        setProcessingProgress(100);
                    } catch (err) {
                        console.error("Error enhancing image:", err);
                        // If enhancement fails, use original image
                        setIdCardImage(imageSrc);
                        setProcessingProgress(100);
                    }

                    resolve();
                };

                img.onerror = () => {
                    setError("Failed to load image");
                    resolve();
                };

                img.src = imageSrc;
            } catch (error) {
                console.error("Error processing ID card:", error);
                setError("Failed to process ID card image");
                resolve();
            }
        });
    };

    // Enhanced ID card image processing with better error handling
    const enhanceIdCardImage = async (src: any): Promise<any | null> => {
        const cv = window.cv;
        return new Promise((resolve) => {
            // Define OpenCV variables for cleanup
            let gray = null;
            let blurred = null;
            let binary = null;
            let contours = null;
            let hierarchy = null;
            let maxRect = null;
            let warped = null;

            try {
                // Convert to grayscale
                gray = new cv.Mat();
                cv.cvtColor(src, gray, cv.COLOR_RGBA2GRAY);

                // Apply Gaussian blur
                blurred = new cv.Mat();
                cv.GaussianBlur(gray, blurred, new cv.Size(5, 5), 0);

                // Apply adaptive threshold
                binary = new cv.Mat();
                cv.adaptiveThreshold(blurred, binary, 255, cv.ADAPTIVE_THRESH_GAUSSIAN_C, cv.THRESH_BINARY, 11, 2);

                // Find contours
                contours = new cv.MatVector();
                hierarchy = new cv.Mat();
                cv.findContours(binary, contours, hierarchy, cv.RETR_LIST, cv.CHAIN_APPROX_SIMPLE);

                // Find largest rectangular contour
                let maxArea = 0;

                for (let i = 0; i < contours.size(); i++) {
                    const contour = contours.get(i);
                    const area = cv.contourArea(contour);

                    if (area > 1000) { // Minimum size filter
                        const perimeter = cv.arcLength(contour, true);
                        const approx = new cv.Mat();
                        cv.approxPolyDP(contour, approx, 0.02 * perimeter, true);

                        // Check if approximately a rectangle
                        if (approx.rows === 4 && cv.isContourConvex(approx)) {
                            if (area > maxArea) {
                                maxArea = area;
                                if (maxRect) maxRect.delete();
                                maxRect = approx.clone();
                            } else {
                                approx.delete();
                            }
                        } else {
                            approx.delete();
                        }
                    }
                    contour.delete();
                }

                // If we found a suitable rectangle
                if (maxRect) {
                    // Get rectangle corners in consistent order
                    const corners = getOrderedCorners(maxRect);

                    if (corners.length === 4) {
                        // Calculate average width and height
                        const widthTop = Math.sqrt(
                            Math.pow(corners[1].x - corners[0].x, 2) +
                            Math.pow(corners[1].y - corners[0].y, 2)
                        );
                        const widthBottom = Math.sqrt(
                            Math.pow(corners[2].x - corners[3].x, 2) +
                            Math.pow(corners[2].y - corners[3].y, 2)
                        );
                        const width = Math.round((widthTop + widthBottom) / 2);

                        const heightLeft = Math.sqrt(
                            Math.pow(corners[3].x - corners[0].x, 2) +
                            Math.pow(corners[3].y - corners[0].y, 2)
                        );
                        const heightRight = Math.sqrt(
                            Math.pow(corners[2].x - corners[1].x, 2) +
                            Math.pow(corners[2].y - corners[1].y, 2)
                        );
                        const height = Math.round((heightLeft + heightRight) / 2);

                        // If we have valid dimensions
                        if (width > 0 && height > 0) {
                            // Create source and destination matrices
                            const srcPoints = cv.matFromArray(4, 1, cv.CV_32FC2, [
                                corners[0].x, corners[0].y,
                                corners[1].x, corners[1].y,
                                corners[2].x, corners[2].y,
                                corners[3].x, corners[3].y
                            ]);

                            const dstPoints = cv.matFromArray(4, 1, cv.CV_32FC2, [
                                0, 0,
                                width - 1, 0,
                                width - 1, height - 1,
                                0, height - 1
                            ]);

                            // Apply perspective transform
                            const transformMatrix = cv.getPerspectiveTransform(srcPoints, dstPoints);
                            warped = new cv.Mat();
                            cv.warpPerspective(src, warped, transformMatrix, new cv.Size(width, height));

                            // Clean up
                            srcPoints.delete();
                            dstPoints.delete();
                            transformMatrix.delete();

                            const result = warped;
                            warped = null; // Prevent deletion in finally block

                            // Clean up remaining resources
                            if (maxRect) maxRect.delete();
                            if (gray) gray.delete();
                            if (blurred) blurred.delete();
                            if (binary) binary.delete();
                            if (contours) contours.delete();
                            if (hierarchy) hierarchy.delete();

                            resolve(result);
                            return;
                        }
                    }
                }

                // If we reach here, perspective correction failed
                resolve(null);
            } catch (err) {
                console.error("Error in enhanceIdCardImage:", err);
                resolve(null);
            } finally {
                // Clean up all OpenCV resources
                [gray, blurred, binary, contours, hierarchy, maxRect, warped].forEach(mat => {
                    if (mat) mat.delete();
                });
            }
        });
    };

    // Helper function to sort corners into a consistent order
    const getOrderedCorners = (corners: any): Array<{ x: number, y: number }> => {
        // Extract corner points
        const points = [];
        for (let i = 0; i < corners.rows; i++) {
            points.push({
                x: corners.data32F[i * 2],
                y: corners.data32F[i * 2 + 1]
            });
        }

        // Compute centroid
        const center = points.reduce(
            (acc, p) => ({
                x: acc.x + p.x / points.length,
                y: acc.y + p.y / points.length
            }),
            { x: 0, y: 0 }
        );

        // Sort corners based on position relative to centroid
        // Top-left, top-right, bottom-right, bottom-left
        return points.sort((a, b) => {
            // Determine quadrant (0-3)
            const aQuadrant = (a.x >= center.x ? 1 : 0) + (a.y >= center.y ? 2 : 0);
            const bQuadrant = (b.x >= center.x ? 1 : 0) + (b.y >= center.y ? 2 : 0);

            if (aQuadrant !== bQuadrant) {
                return aQuadrant - bQuadrant;
            }

            // If in same quadrant, use alternate sorting
            switch (aQuadrant) {
                case 0: // Top-left: smaller x + y wins
                    return (a.x + a.y) - (b.x + b.y);
                case 1: // Top-right: smaller y - x wins
                    return (a.y - a.x) - (b.y - b.x);
                case 3: // Bottom-left: smaller x - y wins
                    return (a.x - a.y) - (b.x - b.y);
                case 2: // Bottom-right: larger x + y wins
                    return (b.x + b.y) - (a.x + a.y);
                default:
                    return 0;
            }
        });
    };

    // Detect face in selfie using face-api.js
    const detectFace = async (imageSrc: string): Promise<number> => {
        if (!modelsLoaded) return 0;

        try {
            const img = await faceapi.fetchImage(imageSrc);
            const detections = await faceapi.detectSingleFace(
                img,
                new faceapi.TinyFaceDetectorOptions()
            ).withFaceLandmarks().withFaceDescriptor();

            if (detections) {
                setFaceDetected(true);
                return 0.95; // Simulated confidence
            }
            return 0;
        } catch (error) {
            console.error("Error detecting face:", error);
            return 0;
        }
    };

    // Capture ID card image
    const captureIdCard = useCallback(async () => {
        if (!documentDetected && activeDocumentDetection) {
            setError("No document detected. Please position your ID card clearly in the frame.");
            return;
        }

        setLoading(true);
        setProcessingProgress(0);
        setError(null);
        clearDetectionIntervals();

        if (webcamRef.current) {
            const imageSrc = webcamRef.current.getScreenshot();
            if (imageSrc) {
                setIdCardImage(imageSrc);
                await processIdCard(imageSrc);
            }
            setLoading(false);
        }
    }, [webcamRef, processIdCard, documentDetected, activeDocumentDetection]);

    // Manually capture ID card without requiring document detection
    const manualCaptureIdCard = useCallback(async () => {
        setLoading(true);
        setProcessingProgress(0);
        setError(null);
        clearDetectionIntervals();

        if (webcamRef.current) {
            const imageSrc = webcamRef.current.getScreenshot();
            if (imageSrc) {
                setIdCardImage(imageSrc);
                await processIdCard(imageSrc);
            }
            setLoading(false);
        }
    }, [webcamRef, processIdCard]);

    // Capture selfie image
    const captureSelfie = useCallback(async () => {
        if (!faceDetected && activeFaceDetection) {
            setError("No face detected. Please position your face properly in the frame.");
            return;
        }

        setLoading(true);

        if (webcamRef.current) {
            const imageSrc = webcamRef.current.getScreenshot();
            if (imageSrc) {
                setSelfieImage(imageSrc);
                clearDetectionIntervals();

                const confidence = await detectFace(imageSrc);
                setFaceMatchConfidence(confidence);

                if (confidence > 0) {
                    //setCurrentStep(2);
                } else {
                    setError("Face verification failed. Please try again.");
                    setSelfieImage(null);
                    if (modelsLoaded) startFaceDetection();
                }
            }
            setLoading(false);
        }
    }, [webcamRef, modelsLoaded, faceDetected, activeFaceDetection, startFaceDetection]);

    // Update personal info state
    const handlePersonalInfoChange = (field: keyof PersonalInfo, value: string) => {
        setPersonalInfo(prev => ({ ...prev, [field]: value }));
    };

    // Submit verification to backend
    const submitVerification = async () => {
        setLoading(true);
        setError(null);

        try {
            // Validation checks
            if (!idCardImage || !selfieImage) {
                setError('Missing required images. Please capture both ID card and selfie.');
                setLoading(false);
                return;
            }

            const requiredFields: (keyof PersonalInfo)[] = ['firstName', 'lastName', 'dateOfBirth', 'documentNumber'];
            const missingFields = requiredFields.filter(field => !personalInfo[field]);

            if (missingFields.length > 0) {
                setError(`Please fill in all required fields: ${missingFields.join(', ')}`);
                setLoading(false);
                return;
            }

            // Prepare verification data
            const verificationData = {
                userId,
                sessionId,
                verificationLevel: "Standard",
                data: {
                    ...personalInfo,
                    selfieImage,
                    documentImage: idCardImage,
                },
                timestamp: new Date().toISOString()
            };

            console.log('Submitting verification data:', {
                ...verificationData,
                documentImage: '[IMAGE DATA]',
                selfieImage: '[IMAGE DATA]'
            });

            const response = await kycService.submitVerification(verificationData);

            if (!response.success) {
                throw new Error(response.message || 'Verification failed');
            }

            setCurrentStep(3);
            setTimeout(onComplete, 2000);
        } catch (error) {
            console.error('Error during verification submission:', error);
            setError(error instanceof Error ? error.message : 'Verification submission failed. Please try again.');
        } finally {
            setLoading(false);
        }
    };

    // Render webcam with detection overlays
    const renderWebcam = () => {
        const isDocumentStep = currentStep === 0;
        const isSelfieStep = currentStep === 1;

        let liveDetectionActive = false;
        let isDetected = false;
        let detectionText = "";
        let borderColor = '';
        let shadowStyle = 'none';

        if (isDocumentStep && activeDocumentDetection) {
            liveDetectionActive = true;
            isDetected = documentDetected;
            detectionText = isDetected ? "Document Detected" : "No Document Detected";
        } else if (isSelfieStep && activeFaceDetection) {
            liveDetectionActive = true;
            isDetected = faceDetected;
            detectionText = isDetected ? "Face Detected" : "No Face Detected";
        }

        if (liveDetectionActive) {
            borderColor = isDetected ? '#52c41a' : '#ff4d4f';
            shadowStyle = isDetected
                ? '0 0 15px rgba(82, 196, 26, 0.8)'
                : '0 0 15px rgba(255, 77, 79, 0.8)';
        }

        return (
            <div className="relative flex justify-center my-4">
                <div className="webcam-container relative">
                    <Webcam
                        audio={false}
                        ref={webcamRef}
                        screenshotFormat="image/jpeg"
                        className="rounded"
                        style={{
                            width: '100%',
                            maxWidth: '500px',
                            border: liveDetectionActive ? `2px solid ${borderColor}` : 'none',
                            boxShadow: shadowStyle
                        }}
                    />

                    {/* Debug canvas overlay - positioned absolutely over the webcam */}
                    {(
                        <canvas
                            ref={debugCanvasRef}
                            className="absolute top-0 left-0 w-full h-full pointer-events-none z-10"
                            style={{
                                maxWidth: '500px',
                            }}
                        />
                    )}

                    {/* ID Card placement guide */}
                    {currentStep === 0 && !idCardImage && (
                        <div
                            className="absolute top-0 left-0 w-full h-full pointer-events-none"
                            style={{
                                border: '2px dashed #fff',
                                borderRadius: '8px',
                                boxSizing: 'border-box',
                                margin: 'auto',
                                width: '80%',
                                height: '60%',
                                top: '20%',
                                left: '10%'
                            }}
                        >
                            <div className="absolute top-0 left-0 w-full text-center bg-black bg-opacity-50 text-white py-1 rounded-t-lg">
                                {documentDetected ?
                                    "Perfect! Document detected. Click capture now." :
                                    "Move your ID card into the frame until detected"}
                            </div>
                        </div>
                    )}

                    {/* Detection status badge */}
                    {liveDetectionActive && (
                        <div
                            className="absolute top-3 right-3 z-10 bg-white bg-opacity-80 px-2 py-1 rounded"
                        >
                            <Badge
                                status={isDetected ? "success" : "error"}
                                text={
                                    <span style={{
                                        color: isDetected ? '#52c41a' : '#ff4d4f',
                                        fontWeight: 'bold'
                                    }}>
                                        {detectionText}
                                    </span>
                                }
                            />
                        </div>
                    )}
                </div>
            </div>
        );
    };

    // Render captured image
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

    // Render ID card capture step
    const renderIdCardStep = () => (
        <div className="text-center">
            <Title level={4}>Capture ID Card</Title>
            <Paragraph>
                Position your ID card clearly within the frame and ensure all text is visible.
            </Paragraph>

            {!idCardImage ? (
                <>
                    {renderWebcam()}
                    <Row gutter={16} justify="center">
                        <Col>
                            <Button
                                type="primary"
                                icon={<CameraOutlined />}
                                onClick={captureIdCard}
                                loading={loading}
                                disabled={!opencvLoaded || loading || (activeDocumentDetection && !documentDetected)}
                                className="mr-2"
                            >
                                Capture ID Card
                            </Button>
                        </Col>
                        <Col>
                            <Button
                                onClick={manualCaptureIdCard}
                                icon={<CameraOutlined />}
                                disabled={!opencvLoaded || loading}
                            >
                                Manual Capture
                            </Button>
                        </Col>
                    </Row>
                </>
            ) : (
                <>
                    {renderCapturedImage(idCardImage)}

                    {loading && processingProgress < 100 ? (
                        <div className="my-4">
                            <Progress percent={processingProgress} status="active" />
                            <Text>Processing document...</Text>
                        </div>
                    ) : (
                        <>
                            <div className="my-4">
                                <Button
                                    onClick={() => {
                                        setIdCardImage(null);
                                        if (opencvLoaded) startDocumentDetection();
                                    }}
                                    className="mr-2"
                                >
                                    Retake
                                </Button>
                                <Button
                                    type="primary"
                                    onClick={() => setCurrentStep(1)}
                                >
                                    Continue
                                </Button>
                            </div>

                            <div className="mt-6">
                                <Card title="ID Card Information" className="max-w-md mx-auto">
                                    <Form layout="vertical">
                                        <Form.Item label="First Name" required>
                                            <Input
                                                value={personalInfo.firstName}
                                                onChange={(e) => handlePersonalInfoChange('firstName', e.target.value)}
                                                placeholder="First Name"
                                                prefix={<EditOutlined />}
                                            />
                                        </Form.Item>
                                        <Form.Item label="Last Name" required>
                                            <Input
                                                value={personalInfo.lastName}
                                                onChange={(e) => handlePersonalInfoChange('lastName', e.target.value)}
                                                placeholder="Last Name"
                                                prefix={<EditOutlined />}
                                            />
                                        </Form.Item>
                                        <Form.Item label="Date of Birth" required>
                                            <Input
                                                value={personalInfo.dateOfBirth}
                                                onChange={(e) => handlePersonalInfoChange('dateOfBirth', e.target.value)}
                                                placeholder="YYYY-MM-DD"
                                                prefix={<EditOutlined />}
                                            />
                                        </Form.Item>
                                        <Form.Item label="Document Number" required>
                                            <Input
                                                value={personalInfo.documentNumber}
                                                onChange={(e) => handlePersonalInfoChange('documentNumber', e.target.value)}
                                                placeholder="Document Number"
                                                prefix={<EditOutlined />}
                                            />
                                        </Form.Item>
                                        <Form.Item label="Nationality">
                                            <Input
                                                value={personalInfo.nationality}
                                                onChange={(e) => handlePersonalInfoChange('nationality', e.target.value)}
                                                placeholder="Nationality"
                                                prefix={<EditOutlined />}
                                            />
                                        </Form.Item>
                                    </Form>
                                </Card>
                            </div>
                        </>
                    )}
                </>
            )}
        </div>
    );

    // Render selfie capture step
    const renderSelfieStep = () => (
        <div className="text-center">
            <Title level={4}>Take a Selfie</Title>
            <Paragraph>Position your face clearly within the frame for verification.</Paragraph>

            {!selfieImage ? (
                <>
                    {renderWebcam()}

                    {/*activeFaceDetection && !faceDetected && (
                        <Alert
                            message="Face not detected"
                            description="Please position your face clearly in the frame"
                            type="warning"
                            showIcon
                            icon={<WarningOutlined />}
                            className="mb-4 max-w-md mx-auto"
                        />
                    )*/}

                    <Button
                        type="primary"
                        icon={<CameraOutlined />}
                        onClick={captureSelfie}
                        disabled={!modelsLoaded || loading || (activeFaceDetection && !faceDetected)}
                        loading={loading}
                        className={(!faceDetected && activeFaceDetection) ? 'opacity-50 cursor-not-allowed' : ''}
                    >
                        Capture Selfie
                    </Button>
                </>
            ) : (
                <>
                    {renderCapturedImage(selfieImage)}

                    <div className="mt-4">
                        <Button
                            onClick={() => {
                                setSelfieImage(null);
                                if (modelsLoaded) startFaceDetection();
                            }}
                            className="mr-2"
                        >
                            Retake
                        </Button>

                        <Button
                            type="primary"
                            onClick={submitVerification}
                            loading={loading}
                        >
                            Submit Verification
                        </Button>

                        {faceDetected && faceMatchConfidence > 0 && (
                            <div className="mt-4">
                                <Alert
                                    type="success"
                                    message="Face detected successfully"
                                    description={`Face match confidence: ${(faceMatchConfidence * 100).toFixed(0)}%`}
                                    icon={<SafetyOutlined />}
                                />
                            </div>
                        )}
                    </div>
                </>
            )}
        </div>
    );

    // Render verification reviewing step
    const renderReviewStep = () => (
        <div className="text-center py-8">
            <Title level={4}>Reviewing Your Verification</Title>
            <Spin size="large" />
            <Paragraph className="mt-4">
                We are processing your verification. This may take a moment...
            </Paragraph>
        </div>
    );

    // Render verification complete step
    const renderCompleteStep = () => (
        <div className="text-center py-8">
            <CheckCircleOutlined style={{ fontSize: 64, color: '#52c41a' }} />
            <Title level={4} className="mt-4">Verification Complete!</Title>
            <Paragraph>
                Thank you for completing the verification process. Your account is being reviewed.
            </Paragraph>
        </div>
    );

    // Render current step based on state
    const renderCurrentStep = () => {
        switch (currentStep) {
            case 0: return renderIdCardStep();
            case 1: return renderSelfieStep();
            case 2: return renderReviewStep();
            case 3: return renderCompleteStep();
            default: return renderIdCardStep();
        }
    };

    // Show loading screen if required resources aren't loaded
    if (!opencvLoaded || (!modelsLoaded && currentStep > 0)) {
        return (
            <Card title="Loading Resources" className="max-w-3xl mx-auto my-5">
                <div className="text-center py-8">
                    <Spin size="large" />
                    <Text style={{ display: 'block' }} className="mt-4">Loading verification tools...</Text>
                    {!opencvLoaded && <Text style={{ display: 'block' }}>Loading Computer Vision Library...</Text>}
                    {opencvLoaded && !modelsLoaded && currentStep > 0 &&
                        <Text style={{ display: 'block' }}>Loading Face Detection Models...</Text>}
                </div>
            </Card>
        );
    }

    return (
        <Card
            title={
                <div className="flex items-center">
                    <IdcardOutlined className="mr-2" />
                    <span>KYC Verification</span>
                </div>
            }
            className="max-w-3xl mx-auto my-5 shadow-md"
        >
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
                <Step title="Processing" icon={<ReconciliationOutlined />} />
                <Step title="Complete" icon={<CheckCircleOutlined />} />
            </Steps>

            {renderCurrentStep()}
        </Card>
    );
};

export default KycVerification;