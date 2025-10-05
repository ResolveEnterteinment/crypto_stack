// src/utils/SecureFileValidator.ts
// Comprehensive file validation for KYC document uploads

export interface FileValidationResult {
    valid: boolean;
    reason?: string;
    warnings?: string[];
}

export class SecureFileValidator {
    // File magic numbers (first bytes that identify file type)
    private static readonly MAGIC_NUMBERS: Record<string, number[]> = {
        jpeg: [0xFF, 0xD8, 0xFF],
        png: [0x89, 0x50, 0x4E, 0x47],
        pdf: [0x25, 0x50, 0x44, 0x46], // %PDF
        webp: [0x52, 0x49, 0x46, 0x46] // RIFF (first 4 bytes)
    };

    private static readonly ALLOWED_TYPES = [
        'image/jpeg',
        'image/png',
        'image/webp',
        'application/pdf'
    ];

    private static readonly MAX_FILE_SIZE = 10 * 1024 * 1024; // 10MB
    private static readonly MIN_IMAGE_DIMENSION = 300;
    private static readonly MAX_IMAGE_DIMENSION = 4096;

    /**
     * Comprehensive file validation
     */
    static async validateFile(file: File): Promise<FileValidationResult> {
        const warnings: string[] = [];

        // 1. Check file size
        if (file.size > this.MAX_FILE_SIZE) {
            return { valid: false, reason: `File size exceeds maximum of ${this.MAX_FILE_SIZE / 1024 / 1024}MB` };
        }

        if (file.size === 0) {
            return { valid: false, reason: 'File is empty' };
        }

        // 2. Check declared MIME type
        if (!this.ALLOWED_TYPES.includes(file.type)) {
            return { 
                valid: false, 
                reason: `File type "${file.type}" not allowed. Accepted: ${this.ALLOWED_TYPES.join(', ')}` 
            };
        }

        // 3. Validate actual file type via magic numbers
        const actualType = await this.getActualFileType(file);
        if (actualType === 'unknown') {
            return { 
                valid: false, 
                reason: 'Unable to determine file type. File may be corrupted.' 
            };
        }

        if (!this.typesMatch(actualType, file.type)) {
            return { 
                valid: false, 
                reason: `File type mismatch detected. File appears to be ${actualType} but is labeled as ${file.type}. This may indicate file tampering.` 
            };
        }

        // 4. For images, validate dimensions
        if (actualType.startsWith('image/')) {
            const dimensions = await this.getImageDimensions(file);
            
            if (dimensions.width === 0 || dimensions.height === 0) {
                return { valid: false, reason: 'Unable to read image dimensions. File may be corrupted.' };
            }

            if (dimensions.width < this.MIN_IMAGE_DIMENSION || dimensions.height < this.MIN_IMAGE_DIMENSION) {
                return { 
                    valid: false, 
                    reason: `Image resolution too low. Minimum required: ${this.MIN_IMAGE_DIMENSION}x${this.MIN_IMAGE_DIMENSION}px` 
                };
            }

            if (dimensions.width > this.MAX_IMAGE_DIMENSION || dimensions.height > this.MAX_IMAGE_DIMENSION) {
                return { 
                    valid: false, 
                    reason: `Image resolution too high. Maximum allowed: ${this.MAX_IMAGE_DIMENSION}x${this.MAX_IMAGE_DIMENSION}px` 
                };
            }

            // Check aspect ratio
            const aspectRatio = dimensions.width / dimensions.height;
            if (aspectRatio < 0.5 || aspectRatio > 2.0) {
                warnings.push('Unusual aspect ratio detected. Ensure document is properly framed.');
            }
        }

        // 5. Scan for embedded malicious content
        const malwareCheck = await this.scanForMalware(file);
        if (!malwareCheck.clean) {
            return { 
                valid: false, 
                reason: malwareCheck.threat || 'Potentially malicious content detected in file' 
            };
        }

        if (malwareCheck.warnings) {
            warnings.push(...malwareCheck.warnings);
        }

        // 6. For PDFs, validate structure
        if (actualType === 'application/pdf') {
            const pdfValidation = await this.validatePDF(file);
            if (!pdfValidation.valid) {
                return pdfValidation;
            }
            if (pdfValidation.warnings) {
                warnings.push(...pdfValidation.warnings);
            }
        }

        return { 
            valid: true, 
            warnings: warnings.length > 0 ? warnings : undefined 
        };
    }

    /**
     * Get actual file type by reading magic numbers
     */
    private static async getActualFileType(file: File): Promise<string> {
        return new Promise((resolve) => {
            const reader = new FileReader();
            
            reader.onloadend = (e) => {
                if (!e.target?.result) {
                    resolve('unknown');
                    return;
                }
                
                const arr = new Uint8Array(e.target.result as ArrayBuffer);
                const header = arr.subarray(0, 4);
                
                // Check JPEG
                if (header[0] === 0xFF && header[1] === 0xD8 && header[2] === 0xFF) {
                    resolve('image/jpeg');
                    return;
                }
                
                // Check PNG
                if (header[0] === 0x89 && header[1] === 0x50 && header[2] === 0x4E && header[3] === 0x47) {
                    resolve('image/png');
                    return;
                }
                
                // Check PDF
                if (header[0] === 0x25 && header[1] === 0x50 && header[2] === 0x44 && header[3] === 0x46) {
                    resolve('application/pdf');
                    return;
                }
                
                // Check WebP (RIFF header, need to check further bytes)
                if (header[0] === 0x52 && header[1] === 0x49 && header[2] === 0x46 && header[3] === 0x46) {
                    // Read more bytes to confirm WebP
                    const webpCheck = arr.subarray(8, 12);
                    const webpStr = String.fromCharCode(...Array.from(webpCheck));
                    if (webpStr === 'WEBP') {
                        resolve('image/webp');
                        return;
                    }
                }
                
                resolve('unknown');
            };
            
            reader.onerror = () => resolve('unknown');
            reader.readAsArrayBuffer(file.slice(0, 12)); // Read first 12 bytes
        });
    }

    /**
     * Check if actual and declared types match
     */
    private static typesMatch(actual: string, declared: string): boolean {
        const typeMap: Record<string, string[]> = {
            'image/jpeg': ['image/jpeg', 'image/jpg'],
            'image/png': ['image/png'],
            'image/webp': ['image/webp'],
            'application/pdf': ['application/pdf']
        };
        
        return typeMap[actual]?.includes(declared) || false;
    }

    /**
     * Get image dimensions
     */
    private static async getImageDimensions(file: File): Promise<{ width: number; height: number }> {
        return new Promise((resolve) => {
            const img = new Image();
            const url = URL.createObjectURL(file);
            
            img.onload = () => {
                resolve({ width: img.width, height: img.height });
                URL.revokeObjectURL(url);
            };
            
            img.onerror = () => {
                resolve({ width: 0, height: 0 });
                URL.revokeObjectURL(url);
            };
            
            img.src = url;
        });
    }

    /**
     * Scan file for malicious content
     */
    private static async scanForMalware(file: File): Promise<{
        clean: boolean;
        threat?: string;
        warnings?: string[];
    }> {
        try {
            // Read first 10KB for pattern matching
            const chunk = file.slice(0, 1024 * 10);
            const text = await chunk.text();
            
            const warnings: string[] = [];
            
            // Check for script injection patterns
            const scriptPatterns = [
                { pattern: /<script[\s\S]*?>/gi, threat: 'Embedded JavaScript code detected' },
                { pattern: /javascript:/gi, threat: 'JavaScript protocol detected' },
                { pattern: /on\w+\s*=/gi, threat: 'HTML event handlers detected' },
                { pattern: /%3Cscript/gi, threat: 'URL-encoded script tags detected' },
                { pattern: /<iframe/gi, threat: 'Embedded iframe detected' }
            ];
            
            for (const { pattern, threat } of scriptPatterns) {
                if (pattern.test(text)) {
                    return { clean: false, threat };
                }
            }
            
            // Check for suspicious patterns (warnings, not blocking)
            if (/eval\s*\(/gi.test(text)) {
                warnings.push('File contains eval() statements');
            }
            
            if (/document\.write/gi.test(text)) {
                warnings.push('File contains document.write statements');
            }
            
            // Check for null bytes (potential file manipulation)
            if (text.includes('\x00')) {
                return { clean: false, threat: 'Null bytes detected in file' };
            }
            
            return { clean: true, warnings: warnings.length > 0 ? warnings : undefined };
        } catch (error) {
            console.error('Malware scan error:', error);
            return { clean: false, threat: 'Unable to scan file for malicious content' };
        }
    }

    /**
     * Validate PDF structure
     */
    private static async validatePDF(file: File): Promise<FileValidationResult> {
        try {
            const header = await file.slice(0, 1024).text();
            const warnings: string[] = [];
            
            // Check PDF version
            if (!header.startsWith('%PDF-1.')) {
                return { valid: false, reason: 'Invalid PDF header' };
            }
            
            // Extract PDF version
            const versionMatch = header.match(/%PDF-1\.(\d+)/);
            if (versionMatch) {
                const minorVersion = parseInt(versionMatch[1]);
                if (minorVersion > 7) {
                    warnings.push('PDF version may not be widely supported');
                }
            }
            
            // Check for potentially dangerous PDF features
            const dangerousPatterns = [
                { pattern: /\/JavaScript/i, warning: 'PDF contains JavaScript' },
                { pattern: /\/JS/i, warning: 'PDF contains JS actions' },
                { pattern: /\/AA/i, warning: 'PDF contains auto-actions' },
                { pattern: /\/OpenAction/i, warning: 'PDF contains open actions' },
                { pattern: /\/Launch/i, warning: 'PDF contains launch actions' }
            ];
            
            for (const { pattern, warning } of dangerousPatterns) {
                if (pattern.test(header)) {
                    // These are warnings, not blocking
                    warnings.push(warning);
                }
            }
            
            return { 
                valid: true, 
                warnings: warnings.length > 0 ? warnings : undefined 
            };
        } catch (error) {
            console.error('PDF validation error:', error);
            return { valid: false, reason: 'Unable to validate PDF structure' };
        }
    }

    /**
     * Validate file name
     */
    static validateFileName(fileName: string): FileValidationResult {
        // Check for path traversal attempts
        if (fileName.includes('../') || fileName.includes('..\\')) {
            return { valid: false, reason: 'Invalid file name: path traversal detected' };
        }
        
        // Check for suspicious characters
        const suspiciousChars = /[<>:"|?*\x00-\x1f]/;
        if (suspiciousChars.test(fileName)) {
            return { valid: false, reason: 'Invalid characters in file name' };
        }
        
        // Check length
        if (fileName.length > 255) {
            return { valid: false, reason: 'File name too long (max 255 characters)' };
        }
        
        // Check for double extensions (possible malware trick)
        const parts = fileName.split('.');
        if (parts.length > 2) {
            const executableExtensions = ['exe', 'bat', 'cmd', 'com', 'scr', 'js', 'vbs'];
            const hasExecutableExtension = parts.some(part => 
                executableExtensions.includes(part.toLowerCase())
            );
            
            if (hasExecutableExtension) {
                return { valid: false, reason: 'Suspicious file extension detected' };
            }
        }
        
        return { valid: true };
    }
}

export default SecureFileValidator;
