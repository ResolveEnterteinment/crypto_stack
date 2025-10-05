// Interfaces for the component

import { JSX } from "react/jsx-runtime";

export interface VerificationData {
    id: string;
    userId: string;
    email: string;
    provider: string;
    status: string;
    verificationLevel: string;
    isHighRisk: boolean;
    isPep: boolean;
    country: string;
    submittedAt: string;
    completedAt?: string;
    processingTime?: number | null;
}

export interface KycStatus {
    status: 'NOT_STARTED' | 'PENDING' | 'APPROVED' | 'REJECTED' | 'NEEDS_REVIEW' | 'BLOCKED';
    verificationLevel: 'NONE' | 'BASIC' | 'STANDARD' | 'ADVANCED' | 'ENHANCED';
    submittedAt?: string;
    updatedAt?: string;
    verifiedAt?: string;
    expiresAt?: string;
    nextSteps: string[];
}

export interface KycEligibility {
    isVerified: boolean;
    isTradingEligible: boolean;
    requiredLevel: string;
    checkedAt: string;
}

export interface KycRequirements {
    verificationLevel: string;
    requiredDocuments: string[];
    supportedFileTypes: string[];
    maxFileSize: number;
    processingTime: string;
    securityFeatures: string[];
}

export interface CreateSessionRequest {
    verificationLevel: 'BASIC' | 'STANDARD' | 'ADVANCED' | 'ENHANCED';
}

export interface VerificationSubmission {
    sessionId: string;
    verificationLevel: string;
    data: any;
    consentGiven: boolean;
    termsAccepted: boolean;
}

export interface StandardPersonalInfo {
    nationality: string;
    governmentIdNumber: string;
    phoneNumber: string;
    occupation: string;
}

export interface StandardVerificationData {
    personalInfo: StandardPersonalInfo;
    documents: Array<{
        id: string;
        type: string;
        hash: string[];
        uploadDate: Date;
        isLiveCapture: boolean;
    }>;
    selfieHash: string | null;
}

export interface AdvancedVerificationData {
    documents: Array<{
        id: string;
        type: string;
        hash: string[];
        uploadDate: Date;
        isLiveCapture: boolean;
    }>;
}

export interface DocumentType { value: string, label: string, icon: JSX.Element, requiresLive: boolean, requiresDuplex: boolean }

export interface BasicVerificationData {
    personalInfo: {
        fullName: string;
        dateOfBirth: string;
        address: {
            street: string;
            city: string;
            state: string;
            zipCode: string;
            country: string;
        };
    }
}

export const DOCUMENT_TYPES = {
    PASSPORT : "passport",
    DRIVERS_LISCENSE: "drivers_license",
    NATIONAL_ID: 'national_id',
    UTILITY_BILL: 'utility_bill',
    RESIDENCE_PERMIT: 'residence_permit',
    BANK_STATEMENT: 'bank_statement'
}

export interface DocumentUpload {
    id: string;
    type: "passport" | 'drivers_license' | 'national_id' | 'utility_bill' | 'residence_permit' | 'bank_statement';
    files?: File[];
    preview: Array<string>;
    uploadDate: Date;
    encryptedHash: string[];
    isLiveCapture: boolean;
    captureData?: DocumentCaptureData;
}

export interface LiveDocumentCaptureRequest {
    sessionId: string;
    documentType: string;
    imageData: ImageData[];
    isLive: boolean;
    isDuplex?: boolean;
    captureMetadata: {
        deviceFingerprint: string;
        timestamp: number;
        userAgent: string;
        screenResolution: string;
        cameraInfo: any;
        environmentData: any;
    };
}

export interface LiveSelfieCaptureRequest {
    sessionId: string;
    imageData: ImageData;
    isLive: boolean;
    captureMetadata: {
        deviceFingerprint: string;
        timestamp: number;
        userAgent: string;
        screenResolution: string;
        cameraInfo: any;
        environmentData: any;
    };
}

export interface LiveCaptureResponse {
    captureId: string;
    status: string;
    processedAt: Date;
}

export interface DocumentUploadResponse {
    documentId: string;
    status: string;
    uploadedAt: Date;
}

export interface DocumentCaptureData {
    documentType?: string;
    isLive: boolean;
    isDuplex?: boolean;
    imageData: ImageData[];
    captureMetadata: {
        timestamp: number;
        deviceFingerprint: string;
        cameraProperties: any;
        environmentalFactors: any;
    };
}

export interface SelfieCaptureData {
    isLive: boolean;
    imageData: ImageData;
    captureMetadata: {
        timestamp: number;
        deviceFingerprint: string;
        cameraProperties: any;
        environmentalFactors: any;
    };
}

export interface ImageData {
    side?: 'front' | 'back'; // Add side information
    isLive: boolean;
    imageData: string;
}