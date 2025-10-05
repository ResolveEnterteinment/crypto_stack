// src/utils/DataSanitizer.ts
// Comprehensive data sanitization for KYC form inputs

export class DataSanitizer {
    /**
     * Remove all HTML tags and potentially dangerous characters
     */
    static sanitizeHTML(input: string): string {
        if (!input) return '';
        
        const div = document.createElement('div');
        div.textContent = input;
        return div.innerHTML
            .replace(/<[^>]*>/g, '') // Remove any HTML tags
            .replace(/&lt;/g, '<')
            .replace(/&gt;/g, '>')
            .replace(/&amp;/g, '&')
            .trim();
    }

    /**
     * Sanitize name fields (personal names)
     * Allows: Letters (Unicode), spaces, hyphens, apostrophes, periods
     */
    static sanitizeName(input: string): string {
        if (!input) return '';
        
        return input
            .trim()
            // Remove any HTML
            .replace(/<[^>]*>/g, '')
            // Only allow letters (Unicode aware), spaces, hyphens, apostrophes, periods
            .replace(/[^\p{L}\s\-'.]/gu, '')
            // Normalize multiple spaces to single space
            .replace(/\s+/g, ' ')
            // Remove leading/trailing special characters
            .replace(/^[\-'.]+|[\-'.]+$/g, '')
            // Limit length
            .slice(0, 100);
    }

    /**
     * Sanitize document/ID numbers
     * Allows: Alphanumeric characters and hyphens only
     */
    static sanitizeDocumentNumber(input: string): string {
        if (!input) return '';
        
        return input
            .trim()
            // Remove all non-alphanumeric except hyphens
            .replace(/[^A-Z0-9\-]/gi, '')
            // Convert to uppercase for consistency
            .toUpperCase()
            // Normalize multiple hyphens
            .replace(/\-+/g, '-')
            // Remove leading/trailing hyphens
            .replace(/^\-+|\-+$/g, '')
            // Limit length
            .slice(0, 50);
    }

    /**
     * Sanitize phone numbers
     * Allows: Numbers, +, -, spaces, parentheses
     */
    static sanitizePhone(input: string): string {
        if (!input) return '';
        
        return input
            .trim()
            // Only allow phone number characters
            .replace(/[^0-9+\-\s()]/g, '')
            // Normalize spaces
            .replace(/\s+/g, ' ')
            // Ensure + is only at the start
            .replace(/(?!^)\+/g, '')
            // Limit length
            .slice(0, 20);
    }

    /**
     * Sanitize email addresses
     */
    static sanitizeEmail(input: string): string {
        if (!input) return '';
        
        return input
            .trim()
            .toLowerCase()
            // Remove any spaces
            .replace(/\s/g, '')
            // Only allow valid email characters
            .replace(/[^a-z0-9@._\-+]/g, '')
            // Prevent multiple @ symbols (keep first one)
            .replace(/(@.*?)@/g, '$1')
            // Limit length
            .slice(0, 254); // RFC 5321
    }

    /**
     * Sanitize address fields
     * More permissive than names, allows numbers and common punctuation
     */
    static sanitizeAddress(input: string): string {
        if (!input) return '';
        
        return input
            .trim()
            // Remove HTML
            .replace(/<[^>]*>/g, '')
            // Remove dangerous characters but allow common address punctuation
            .replace(/[<>{}[\]]/g, '')
            // Normalize spaces
            .replace(/\s+/g, ' ')
            // Limit length
            .slice(0, 200);
    }

    /**
     * Sanitize city/state names
     */
    static sanitizeLocation(input: string): string {
        if (!input) return '';
        
        return input
            .trim()
            // Remove HTML
            .replace(/<[^>]*>/g, '')
            // Only allow letters, spaces, hyphens, apostrophes (Unicode aware)
            .replace(/[^\p{L}\s\-']/gu, '')
            // Normalize spaces
            .replace(/\s+/g, ' ')
            // Remove leading/trailing special characters
            .replace(/^[\-']+|[\-']+$/g, '')
            // Limit length
            .slice(0, 100);
    }

    /**
     * Sanitize postal/ZIP codes
     */
    static sanitizePostalCode(input: string): string {
        if (!input) return '';
        
        return input
            .trim()
            .toUpperCase()
            // Only allow alphanumeric and hyphens/spaces
            .replace(/[^A-Z0-9\-\s]/g, '')
            // Normalize spaces
            .replace(/\s+/g, ' ')
            // Limit length
            .slice(0, 20);
    }

    /**
     * Sanitize occupation/job title
     */
    static sanitizeOccupation(input: string): string {
        if (!input) return '';
        
        return input
            .trim()
            // Remove HTML
            .replace(/<[^>]*>/g, '')
            // Only allow letters, numbers, spaces, hyphens, apostrophes, periods, slashes
            .replace(/[^\p{L}0-9\s\-'./]/gu, '')
            // Normalize spaces
            .replace(/\s+/g, ' ')
            // Remove leading/trailing special characters
            .replace(/^[\-'./]+|[\-'./]+$/g, '')
            // Limit length
            .slice(0, 100);
    }

    /**
     * Sanitize nationality/country
     */
    static sanitizeNationality(input: string): string {
        if (!input) return '';
        
        return input
            .trim()
            // Remove HTML
            .replace(/<[^>]*>/g, '')
            // Only allow letters, spaces, hyphens (Unicode aware)
            .replace(/[^\p{L}\s\-]/gu, '')
            // Normalize spaces
            .replace(/\s+/g, ' ')
            // Remove leading/trailing hyphens
            .replace(/^\-+|\-+$/g, '')
            // Capitalize first letter of each word
            .replace(/\b\w/g, l => l.toUpperCase())
            // Limit length
            .slice(0, 100);
    }

    /**
     * Sanitize date strings
     * Returns ISO format (YYYY-MM-DD) or empty string
     */
    static sanitizeDate(input: string | Date): string {
        if (!input) return '';
        
        try {
            const date = input instanceof Date ? input : new Date(input);
            
            if (isNaN(date.getTime())) {
                return '';
            }
            
            // Return ISO date format (YYYY-MM-DD)
            return date.toISOString().split('T')[0];
        } catch {
            return '';
        }
    }

    /**
     * Sanitize numeric input
     */
    static sanitizeNumber(input: string | number): string {
        if (input === null || input === undefined || input === '') return '';
        
        const numStr = String(input);
        
        // Only allow numbers, decimal point, and minus sign
        const sanitized = numStr.replace(/[^0-9.\-]/g, '');
        
        // Ensure only one decimal point
        const parts = sanitized.split('.');
        if (parts.length > 2) {
            return parts[0] + '.' + parts.slice(1).join('');
        }
        
        // Ensure minus is only at the start
        return sanitized.replace(/(?!^)\-/g, '');
    }

    /**
     * Sanitize URL
     */
    static sanitizeURL(input: string): string {
        if (!input) return '';
        
        try {
            const url = new URL(input.trim());
            
            // Only allow http and https protocols
            if (!['http:', 'https:'].includes(url.protocol)) {
                return '';
            }
            
            return url.toString();
        } catch {
            return '';
        }
    }

    /**
     * Validate and sanitize based on field type
     */
    static sanitizeByType(input: string, fieldType: 
        'name' | 'email' | 'phone' | 'address' | 'city' | 'postal' | 
        'document' | 'occupation' | 'nationality' | 'date' | 'number' | 'url'
    ): string {
        switch (fieldType) {
            case 'name':
                return this.sanitizeName(input);
            case 'email':
                return this.sanitizeEmail(input);
            case 'phone':
                return this.sanitizePhone(input);
            case 'address':
                return this.sanitizeAddress(input);
            case 'city':
                return this.sanitizeLocation(input);
            case 'postal':
                return this.sanitizePostalCode(input);
            case 'document':
                return this.sanitizeDocumentNumber(input);
            case 'occupation':
                return this.sanitizeOccupation(input);
            case 'nationality':
                return this.sanitizeNationality(input);
            case 'date':
                return this.sanitizeDate(input);
            case 'number':
                return this.sanitizeNumber(input);
            case 'url':
                return this.sanitizeURL(input);
            default:
                return this.sanitizeHTML(input);
        }
    }

    /**
     * Deep sanitize an object recursively
     */
    static sanitizeObject<T extends Record<string, any>>(
        obj: T,
        fieldTypeMap?: Partial<Record<keyof T, string>>
    ): T {
        const sanitized = {} as T;
        
        for (const [key, value] of Object.entries(obj)) {
            if (value === null || value === undefined) {
                sanitized[key as keyof T] = value;
            } else if (typeof value === 'object' && !Array.isArray(value)) {
                sanitized[key as keyof T] = this.sanitizeObject(value);
            } else if (Array.isArray(value)) {
                sanitized[key as keyof T] = value.map(item => 
                    typeof item === 'string' ? this.sanitizeHTML(item) : item
                ) as any;
            } else if (typeof value === 'string') {
                const fieldType = fieldTypeMap?.[key as keyof T];
                sanitized[key as keyof T] = fieldType 
                    ? this.sanitizeByType(value, fieldType as any)
                    : this.sanitizeHTML(value) as any;
            } else {
                sanitized[key as keyof T] = value;
            }
        }
        
        return sanitized;
    }

    /**
     * Check if a string contains potentially malicious content
     */
    static containsMaliciousContent(input: string): boolean {
        if (!input) return false;
        
        const maliciousPatterns = [
            /<script[\s\S]*?>/gi,
            /javascript:/gi,
            /on\w+\s*=/gi,
            /%3Cscript/gi,
            /\x00/, // Null bytes
            /\.\.\//, // Path traversal
            /<iframe/gi,
            /eval\s*\(/gi,
            /document\.write/gi,
            /<embed/gi,
            /<object/gi
        ];
        
        return maliciousPatterns.some(pattern => pattern.test(input));
    }

    /**
     * Escape special characters for safe display
     */
    static escapeForDisplay(input: string): string {
        if (!input) return '';
        
        const div = document.createElement('div');
        div.textContent = input;
        return div.innerHTML;
    }
}

// Export validation helpers
export class InputValidator {
    /**
     * Validate name format
     */
    static isValidName(input: string): boolean {
        if (!input || input.length < 2 || input.length > 100) return false;
        return /^[\p{L}\s\-'.]+$/u.test(input);
    }

    /**
     * Validate email format
     */
    static isValidEmail(input: string): boolean {
        if (!input) return false;
        const emailRegex = /^[a-z0-9._+-]+@[a-z0-9.-]+\.[a-z]{2,}$/i;
        return emailRegex.test(input) && input.length <= 254;
    }

    /**
     * Validate phone format (flexible international format)
     */
    static isValidPhone(input: string): boolean {
        if (!input) return false;
        const phoneRegex = /^\+?[\d\s\-()]{10,20}$/;
        return phoneRegex.test(input);
    }

    /**
     * Validate date (must be in the past and not too old)
     */
    static isValidBirthDate(input: string | Date, minAge: number = 18, maxAge: number = 120): boolean {
        try {
            const date = input instanceof Date ? input : new Date(input);
            if (isNaN(date.getTime())) return false;
            
            const now = new Date();
            const age = (now.getTime() - date.getTime()) / (365.25 * 24 * 60 * 60 * 1000);
            
            return age >= minAge && age <= maxAge;
        } catch {
            return false;
        }
    }

    /**
     * Validate document number format
     */
    static isValidDocumentNumber(input: string): boolean {
        if (!input || input.length < 5 || input.length > 50) return false;
        return /^[A-Z0-9\-]+$/i.test(input);
    }
}

export default DataSanitizer;
