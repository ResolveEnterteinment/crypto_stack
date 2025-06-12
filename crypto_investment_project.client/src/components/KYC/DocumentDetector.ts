/**
 * Professional document detection utility using OpenCV.js
 * Features multi-stage detection pipeline for robust detection across varying conditions
 */

export interface DocumentDetectionResult {
    detected: boolean;
    confidence: number;
    corners?: { x: number; y: number }[];
    boundingBox?: { x: number; y: number; width: number; height: number };
    guidance?: {
        message: string;
        fillPercentage: number;
        isOptimal: boolean;
    };
}

export class DocumentDetector {
    // Increased minimum size to ensure document fills at least 80% of the frame
    private readonly MIN_DOCUMENT_AREA_RATIO = 0.5;  // Minimum document size relative to frame
    private readonly MAX_DOCUMENT_AREA_RATIO = 0.9; // Maximum document size relative to frame
    private readonly OPTIMAL_AREA_RATIO = 0.75;      // Ideal document size for best scanning
    private readonly CONFIDENCE_THRESHOLD = 0.4;     // Lowered threshold for positive detection
    private readonly MIN_CONTOUR_AREA = 5000;        // Reduced minimum contour area for better detection
    private readonly PROCESS_WIDTH = 640;            // Processing width for performance optimization

    /**
 * Detects document in an image or video frame using multi-stage detection pipeline
 * @param imageElement - HTML Image or Video element to analyze
 * @param debugCanvas - Optional canvas element to display detection visualization
 * @returns Detection result with confidence score, coordinates, and guidance information
 */
    public async detectDocument(
        imageElement: HTMLImageElement | HTMLVideoElement,
        debugCanvas?: HTMLCanvasElement
    ): Promise<DocumentDetectionResult> {
        if (!window.cv?.Mat) {
            console.error('OpenCV not available');
            return { detected: false, confidence: 0 };
        }

        const cv = window.cv;
        let result: DocumentDetectionResult = { detected: false, confidence: 0 };

        // Create a canvas to work with the image
        const canvas = document.createElement('canvas');
        const ctx = canvas.getContext('2d');
        if (!ctx) {
            return result;
        }

        try {
            // Properly handle video vs image elements for width/height
            const width = 'videoWidth' in imageElement ?
                imageElement.videoWidth : imageElement.width;
            const height = 'videoHeight' in imageElement ?
                imageElement.videoHeight : imageElement.height;

            // Calculate aspect ratio safely
            const aspectRatio = width && height ? width / height : 1;

            // Set canvas dimensions
            canvas.width = this.PROCESS_WIDTH;
            canvas.height = Math.round(this.PROCESS_WIDTH / aspectRatio);

            // Draw image to canvas
            ctx.drawImage(imageElement, 0, 0, canvas.width, canvas.height);

            // Primary matrices
            const src = cv.imread(canvas);
            const gray = new cv.Mat();
            const blurred = new cv.Mat();
            const canny = new cv.Mat();
            const dilated = new cv.Mat();

            try {
                // Stage 1: Basic preprocessing
                cv.cvtColor(src, gray, cv.COLOR_RGBA2GRAY);
                cv.GaussianBlur(gray, blurred, new cv.Size(5, 5), 0);

                // Stage 2: Edge detection - adaptive approach based on image statistics
                const median = this.calculateMedianBrightness(gray);
                const sigma = 0.33;
                const lower = Math.max(0, (1.0 - sigma) * median);
                const upper = Math.min(255, (1.0 + sigma) * median);

                cv.Canny(blurred, canny, lower, upper);

                // Dilate edges to connect broken lines
                const kernel = cv.Mat.ones(2, 2, cv.CV_8U);
                cv.dilate(canny, dilated, kernel, new cv.Point(-1, -1), 1);
                kernel.delete();

                // Stage 3: First detection attempt - Contour-based approach
                result = await this.attemptContourDetection(cv, src, dilated);

                // Stage 4: If contour-based failed, try with adaptive thresholding
                if (!result.detected) {
                    const binary = new cv.Mat();
                    try {
                        cv.adaptiveThreshold(
                            blurred, binary, 255,
                            cv.ADAPTIVE_THRESH_GAUSSIAN_C,
                            cv.THRESH_BINARY, 11, 2
                        );

                        // Invert for better contour detection
                        cv.bitwise_not(binary, binary);

                        result = await this.attemptContourDetection(cv, src, binary);
                    } finally {
                        binary.delete();
                    }
                }

                // Stage 5: If still not detected, try Hough transform as fallback
                if (!result.detected) {
                    result = this.attemptHoughLineDetection(cv, src, canny);
                }

                // Add guidance information to help user position document
                if (result.detected && result.boundingBox) {
                    const imageArea = src.rows * src.cols;
                    const docArea = result.boundingBox.width * result.boundingBox.height;
                    const fillPercentage = docArea / imageArea;

                    // Add user guidance information based on document size
                    result.guidance = this.generateGuidance(fillPercentage);
                } else {
                    result.guidance = {
                        message: "No document detected. Please ensure the document is fully visible.",
                        fillPercentage: 0,
                        isOptimal: false
                    };
                }

            } finally {
                // Clean up OpenCV resources
                src.delete();
                gray.delete();
                blurred.delete();
                canny.delete();
                dilated.delete();
            }

        } catch (error) {
            console.error('Error during document detection:', error);
            return { detected: false, confidence: 0 };
        }

        // If debug canvas is provided, draw the guidance frame on it
        if (debugCanvas) {
            // If dimensions don't match, resize the debug canvas to match the processing canvas
            if (debugCanvas.width !== canvas.width || debugCanvas.height !== canvas.height) {
                debugCanvas.width = canvas.width;
                debugCanvas.height = canvas.height;
            }

            // Copy the image from our temporary canvas to the debug canvas
            const debugCtx = debugCanvas.getContext('2d');
            if (debugCtx) {
                //debugCtx.drawImage(canvas, 0, 0);

                // Draw detection visualization on the debug canvas
                this.drawGuidanceFrame(debugCanvas, result, true);
            }
        }

        return result;
    }

    /**
     * Generate guidance message based on document fill percentage
     */
    private generateGuidance(fillPercentage: number): { message: string, fillPercentage: number, isOptimal: boolean } {
        if (fillPercentage < this.MIN_DOCUMENT_AREA_RATIO) {
            return {
                message: "Move closer to the document to fill more of the frame",
                fillPercentage,
                isOptimal: false
            };
        } else if (fillPercentage > this.MAX_DOCUMENT_AREA_RATIO) {
            return {
                message: "Move further from the document to fit within the frame",
                fillPercentage,
                isOptimal: false
            };
        } else {
            // Within acceptable range, check if it's optimal
            const isOptimal = fillPercentage >= this.OPTIMAL_AREA_RATIO &&
                fillPercentage <= this.MAX_DOCUMENT_AREA_RATIO;
            return {
                message: isOptimal ? "Perfect! Hold still while we capture the document." : "Document detected. Hold still.",
                fillPercentage,
                isOptimal
            };
        }
    }

    /**
     * Calculate median brightness of an image for adaptive thresholding
     */
    private calculateMedianBrightness(grayMat: any): number {
        // Sample the image for performance (every 10th pixel)
        const values: number[] = [];
        for (let y = 0; y < grayMat.rows; y += 10) {
            for (let x = 0; x < grayMat.cols; x += 10) {
                values.push(grayMat.ucharPtr(y, x)[0]);
            }
        }

        // Calculate median
        values.sort((a, b) => a - b);
        const mid = Math.floor(values.length / 2);
        return values.length % 2 === 0 ? (values[mid - 1] + values[mid]) / 2 : values[mid];
    }

    /**
     * Attempt to detect document using contour analysis
     */
    private async attemptContourDetection(cv: any, srcMat: any, edgeMat: any): Promise<DocumentDetectionResult> {
        const contours = new cv.MatVector();
        const hierarchy = new cv.Mat();

        try {
            // Find contours in the edge image
            cv.findContours(
                edgeMat, contours, hierarchy,
                cv.RETR_LIST, cv.CHAIN_APPROX_SIMPLE
            );

            // Variables to track best document candidate
            let bestContour = null;
            let bestRect = null;
            let bestConfidence = 0;

            // Process each contour to find the document
            for (let i = 0; i < contours.size(); i++) {
                const contour = contours.get(i);
                const area = cv.contourArea(contour);
                const imageArea = srcMat.rows * srcMat.cols;

                // Skip if too small or too large
                if (area < this.MIN_CONTOUR_AREA ||
                    area < imageArea * this.MIN_DOCUMENT_AREA_RATIO ||
                    area > imageArea * this.MAX_DOCUMENT_AREA_RATIO) {
                    continue;
                }

                // Approximate the contour to simplify
                const perimeter = cv.arcLength(contour, true);
                const approx = new cv.Mat();
                cv.approxPolyDP(contour, approx, 0.02 * perimeter, true);

                // Calculate quality metrics for 4-sided polygons
                let confidence = 0;

                if (approx.rows === 4 && cv.isContourConvex(approx)) {
                    // Calculate various document quality metrics
                    confidence = this.calculateDocumentConfidence(cv, approx, area, imageArea);

                    if (confidence > bestConfidence) {
                        if (bestRect) bestRect.delete();
                        bestConfidence = confidence;
                        bestContour = contour;
                        bestRect = approx.clone();
                    }
                }

                approx.delete();
            }

            // If we found a good document contour
            if (bestRect && bestConfidence >= this.CONFIDENCE_THRESHOLD) {
                // Extract corners
                const corners = [];
                for (let i = 0; i < 4; i++) {
                    corners.push({
                        x: bestRect.data32S[i * 2],
                        y: bestRect.data32S[i * 2 + 1]
                    });
                }

                // Sort corners into consistent order
                const sortedCorners = this.sortCorners(corners);

                // Calculate bounding box
                const xCoords = sortedCorners.map(p => p.x);
                const yCoords = sortedCorners.map(p => p.y);
                const minX = Math.min(...xCoords);
                const maxX = Math.max(...xCoords);
                const minY = Math.min(...yCoords);
                const maxY = Math.max(...yCoords);

                bestRect.delete();

                return {
                    detected: true,
                    confidence: bestConfidence,
                    corners: sortedCorners,
                    boundingBox: {
                        x: minX,
                        y: minY,
                        width: maxX - minX,
                        height: maxY - minY
                    }
                };
            }

            if (bestRect) bestRect.delete();
            return { detected: false, confidence: bestConfidence };

        } finally {
            contours.delete();
            hierarchy.delete();
        }
    }

    /**
     * Calculate confidence score for document detection based on multiple metrics
     * Adjusted to prioritize document size close to optimal range
     */
    private calculateDocumentConfidence(cv: any, approx: any, area: number, imageArea: number): number {
        // Extract corners
        const corners = [];
        for (let i = 0; i < 4; i++) {
            corners.push({
                x: approx.data32S[i * 2],
                y: approx.data32S[i * 2 + 1]
            });
        }
        const sortedCorners = this.sortCorners(corners);

        // Calculate aspect ratio
        const width = Math.sqrt(
            Math.pow(sortedCorners[1].x - sortedCorners[0].x, 2) +
            Math.pow(sortedCorners[1].y - sortedCorners[0].y, 2)
        );
        const height = Math.sqrt(
            Math.pow(sortedCorners[3].x - sortedCorners[0].x, 2) +
            Math.pow(sortedCorners[3].y - sortedCorners[0].y, 2)
        );
        const aspectRatio = width / height;

        // Most ID cards have aspect ratios between 1.4 and 1.6
        // Passports typically have aspect ratios around 1.4
        // Expanded range for better detection
        const aspectScore =
            (aspectRatio > 1.2 && aspectRatio < 2.0) ?
                (1 - Math.min(Math.abs(aspectRatio - 1.5), 0.5) / 0.5) : 0.3;

        // Area score - how much of the image is occupied by the document
        // We want documents that take up at least 80% of the frame
        const areaRatio = area / imageArea;
        const optimalRatio = this.OPTIMAL_AREA_RATIO;

        // Heavily penalize documents that don't meet minimum size requirements
        let areaScore = 0;
        if (areaRatio >= this.MIN_DOCUMENT_AREA_RATIO && areaRatio <= this.MAX_DOCUMENT_AREA_RATIO) {
            // For documents within range, higher scores for those closer to optimal size
            areaScore = 1 - (Math.abs(areaRatio - optimalRatio) / 0.15);
            areaScore = Math.max(0.7, Math.min(1.0, areaScore)); // Clamp between 0.7-1.0 for documents in range
        } else {
            // Low score for documents outside of range
            areaScore = 0.3;
        }

        // Angle regularity - check if the corners form right angles
        let angleScore = 0;
        try {
            // Calculate vectors between adjacent corners
            const vectors = [];
            for (let i = 0; i < 4; i++) {
                const current = sortedCorners[i];
                const next = sortedCorners[(i + 1) % 4];
                vectors.push({
                    x: next.x - current.x,
                    y: next.y - current.y
                });
            }

            // Check angles between vectors (should be close to 90 degrees)
            let angleSum = 0;
            for (let i = 0; i < 4; i++) {
                const v1 = vectors[i];
                const v2 = vectors[(i + 1) % 4];

                // Calculate angle in degrees
                const dot = v1.x * v2.x + v1.y * v2.y;
                const mag1 = Math.sqrt(v1.x * v1.x + v1.y * v1.y);
                const mag2 = Math.sqrt(v2.x * v2.x + v2.y * v2.y);

                const cosAngle = dot / (mag1 * mag2);
                // Handle potential numerical issues
                const clampedCosAngle = Math.max(-1, Math.min(1, cosAngle));
                const angle = Math.acos(clampedCosAngle) * (180 / Math.PI);
                angleSum += Math.abs(angle - 90);
            }

            // Average angle deviation from 90 degrees
            const avgAngleDeviation = angleSum / 4;
            angleScore = Math.max(0, 1 - avgAngleDeviation / 45);
        } catch (e) {
            angleScore = 0.5; // Default if calculation fails
        }

        // Combine all metrics with different weights
        // Give more weight to area score to prioritize document size
        return (aspectScore * 0.25 + areaScore * 0.5 + angleScore * 0.25);
    }

    /**
     * Fallback detection method using Hough line transform
     */
    private attemptHoughLineDetection(cv: any, srcMat: any, edgeMat: any): DocumentDetectionResult {
        const lines = new cv.Mat();
        try {
            // Apply Hough Line Transform
            cv.HoughLinesP(edgeMat, lines, 1, Math.PI / 180, 50, 50, 10);

            // Too few lines to form a document
            if (lines.rows < 4) {
                return { detected: false, confidence: 0.3 };
            }

            // Calculate bounding box of all detected lines
            let minX = srcMat.cols, minY = srcMat.rows;
            let maxX = 0, maxY = 0;

            for (let i = 0; i < lines.rows; i++) {
                const startX = lines.data32S[i * 4];
                const startY = lines.data32S[i * 4 + 1];
                const endX = lines.data32S[i * 4 + 2];
                const endY = lines.data32S[i * 4 + 3];

                minX = Math.min(minX, startX, endX);
                minY = Math.min(minY, startY, endY);
                maxX = Math.max(maxX, startX, endX);
                maxY = Math.max(maxY, startY, endY);
            }

            const width = maxX - minX;
            const height = maxY - minY;
            const area = width * height;
            const imageArea = srcMat.rows * srcMat.cols;

            // Check if the bounding box has reasonable properties for a document
            if (width > 0 && height > 0) {
                const areaRatio = area / imageArea;

                if (areaRatio >= this.MIN_DOCUMENT_AREA_RATIO && areaRatio <= this.MAX_DOCUMENT_AREA_RATIO) {
                    const aspectRatio = width / height;

                    // Basic confidence based on aspect ratio and size
                    let confidence = (aspectRatio > 1.2 && aspectRatio < 2.0) ? 0.6 : 0.4;

                    // Adjust confidence based on how close it is to our optimal size
                    const sizeOptimalityScore = 1 - Math.abs(areaRatio - this.OPTIMAL_AREA_RATIO) / 0.1;
                    confidence = Math.min(0.85, confidence + sizeOptimalityScore * 0.25);

                    // Also adjust confidence based on line count (more lines = more confidence)
                    confidence = Math.min(0.9, confidence + (lines.rows / 50) * 0.1);

                    return {
                        detected: confidence >= this.CONFIDENCE_THRESHOLD,
                        confidence,
                        boundingBox: {
                            x: minX,
                            y: minY,
                            width,
                            height
                        }
                    };
                }
            }

            return { detected: false, confidence: 0.2 };
        } finally {
            lines.delete();
        }
    }

    /**
     * Sort corners into a consistent order (top-left, top-right, bottom-right, bottom-left)
     */
    private sortCorners(corners: Array<{ x: number, y: number }>): Array<{ x: number, y: number }> {
        // Compute centroid
        const center = corners.reduce(
            (acc, p) => ({
                x: acc.x + p.x / corners.length,
                y: acc.y + p.y / corners.length
            }),
            { x: 0, y: 0 }
        );

        // Sort corners based on position relative to centroid
        return corners.sort((a, b) => {
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
    }

    /**
 * Draws document guidance frame on a canvas element
 * @param canvas - Canvas element to draw on
 * @param result - Detection result with document position information
 * @param debug - Enable debug visualization
 * @returns void
 */
    public drawGuidanceFrame(canvas: HTMLCanvasElement, result?: DocumentDetectionResult, debug: boolean = false): void {
        const ctx = canvas.getContext('2d');
        if (!ctx) return;

        // Clear the canvas
        ctx.clearRect(0, 0, canvas.width, canvas.height);

        // Draw the optimal document area guide (75% of the canvas)
        /*
        const optimalWidth = canvas.width * 0.75;
        const optimalHeight = canvas.height * 0.75;
        const startX = (canvas.width - optimalWidth) / 2;
        const startY = (canvas.height - optimalHeight) / 2;
        

        // Draw the guidance frame
        ctx.strokeStyle = result?.detected && result?.guidance?.isOptimal ? '#00FF00' : '#FFFFFF';
        ctx.lineWidth = 3;
        ctx.setLineDash([]);
        ctx.strokeRect(startX, startY, optimalWidth, optimalHeight);
        */

        // If we have a detected document, draw its outline
        if (result?.detected) {
            if (result.corners) {
                // Standard outline
                ctx.beginPath();
                ctx.strokeStyle = debug ? '#00FF00' : '#00AAFF'; // Green in debug mode, blue in normal mode
                ctx.lineWidth = debug ? 3 : 2;

                // Draw the document outline using detected corners
                const corners = result.corners;
                ctx.moveTo(corners[0].x, corners[0].y);
                for (let i = 1; i < corners.length; i++) {
                    ctx.lineTo(corners[i].x, corners[i].y);
                }
                ctx.closePath();
                ctx.stroke();

                // Debug mode: Add corner markers and labels
                if (debug && result.confidence) {
                    // Draw corner points
                    corners.forEach((corner, i) => {
                        ctx.fillStyle = '#FF0000';
                        ctx.beginPath();
                        ctx.arc(corner.x, corner.y, 5, 0, 2 * Math.PI);
                        ctx.fill();

                        // Label corner numbers
                        ctx.fillStyle = 'white';
                        ctx.font = '12px Arial';
                        ctx.fillText(`${i}`, corner.x + 8, corner.y - 8);
                    });

                    // Display confidence score
                    ctx.font = '14px Arial';
                    ctx.fillStyle = 'yellow';
                    ctx.fillText(`Confidence: ${result.confidence.toFixed(2)}`, 10, 20);

                    // Display fill percentage if available
                    if (result.guidance?.fillPercentage) {
                        ctx.fillText(
                            `Fill: ${(result.guidance.fillPercentage * 100).toFixed(1)}%`,
                            10, 40
                        );
                    }
                }
            } else if (result.boundingBox && debug) {
                // If only bounding box is available (like from Hough lines), draw it in debug mode
                const bb = result.boundingBox;
                ctx.strokeStyle = '#00FF00';
                ctx.lineWidth = 3;
                ctx.strokeRect(bb.x, bb.y, bb.width, bb.height);

                // Display confidence score
                ctx.font = '14px Arial';
                ctx.fillStyle = 'yellow';
                ctx.fillText(`Confidence: ${result.confidence.toFixed(2)}`, 10, 20);
            }
        } else if (debug) {
            // If no document detected in debug mode, display a message
            ctx.font = '16px Arial';
            ctx.fillStyle = 'red';
            ctx.fillText("No document detected", canvas.width / 2 - 80, 30);
        }

        // Display guidance message if available
        if (result?.guidance?.message) {
            ctx.fillStyle = 'rgba(0, 0, 0, 0.6)';
            ctx.fillRect(0, canvas.height - 40, canvas.width, 40);

            ctx.font = '16px Arial';
            ctx.fillStyle = 'white';
            ctx.textAlign = 'center';
            ctx.fillText(result.guidance.message, canvas.width / 2, canvas.height - 15);
        }
    }

}

// Export singleton instance
export const documentDetector = new DocumentDetector();