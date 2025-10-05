// src/utils/SecureSessionManager.ts
// Secure session management for KYC verification process

interface SessionData {
    sessionId: string;
    fingerprint: string;
    expiry: number;
}

export class SecureSessionManager {
    private static readonly SESSION_KEY = 'kyc_session';
    private static readonly FINGERPRINT_KEY = 'kyc_fp';
    private static readonly EXPIRY_KEY = 'kyc_expiry';
    private static readonly SESSION_DURATION_MS = 30 * 60 * 1000; // 30 minutes

    /**
     * Set a secure session with encryption and fingerprinting
     */
    static async setSession(sessionId: string): Promise<void> {
        try {
            // Generate device fingerprint
            const fingerprint = await this.generateFingerprint();
            
            // Encrypt session ID with fingerprint
            const encrypted = await this.encrypt(sessionId, fingerprint);
            
            // Calculate expiry time
            const expiry = Date.now() + this.SESSION_DURATION_MS;
            
            // Use sessionStorage (cleared on tab close) for better security
            sessionStorage.setItem(this.SESSION_KEY, encrypted);
            sessionStorage.setItem(this.FINGERPRINT_KEY, fingerprint);
            sessionStorage.setItem(this.EXPIRY_KEY, expiry.toString());
            
            console.log('[SecureSession] Session set successfully');
        } catch (error) {
            console.error('[SecureSession] Failed to set session:', error);
            throw new Error('Failed to create secure session');
        }
    }

    /**
     * Get session ID if valid
     */
    static async getSession(): Promise<string | null> {
        try {
            const encrypted = sessionStorage.getItem(this.SESSION_KEY);
            const storedFingerprint = sessionStorage.getItem(this.FINGERPRINT_KEY);
            const expiryStr = sessionStorage.getItem(this.EXPIRY_KEY);
            
            // Check if session exists
            if (!encrypted || !storedFingerprint || !expiryStr) {
                console.log('[SecureSession] No session found');
                return null;
            }
            
            // Check expiry
            const expiry = parseInt(expiryStr);
            if (Date.now() > expiry) {
                console.log('[SecureSession] Session expired');
                this.clearSession();
                return null;
            }
            
            // Verify fingerprint hasn't changed (prevents session hijacking)
            const currentFingerprint = await this.generateFingerprint();
            if (currentFingerprint !== storedFingerprint) {
                console.warn('[SecureSession] Fingerprint mismatch - possible session hijacking attempt');
                this.clearSession();
                return null;
            }
            
            // Decrypt and return session ID
            const sessionId = await this.decrypt(encrypted, storedFingerprint);
            
            // Refresh expiry on access (sliding window)
            this.refreshExpiry();
            
            return sessionId;
        } catch (error) {
            console.error('[SecureSession] Failed to get session:', error);
            this.clearSession();
            return null;
        }
    }

    /**
     * Clear the session
     */
    static clearSession(): void {
        sessionStorage.removeItem(this.SESSION_KEY);
        sessionStorage.removeItem(this.FINGERPRINT_KEY);
        sessionStorage.removeItem(this.EXPIRY_KEY);
        console.log('[SecureSession] Session cleared');
    }

    /**
     * Check if session is valid without retrieving it
     */
    static async isSessionValid(): Promise<boolean> {
        const session = await this.getSession();
        return session !== null;
    }

    /**
     * Get remaining session time in milliseconds
     */
    static getRemainingTime(): number {
        const expiryStr = sessionStorage.getItem(this.EXPIRY_KEY);
        if (!expiryStr) return 0;
        
        const expiry = parseInt(expiryStr);
        const remaining = expiry - Date.now();
        return Math.max(0, remaining);
    }

    /**
     * Refresh session expiry (sliding window)
     */
    private static refreshExpiry(): void {
        const newExpiry = Date.now() + this.SESSION_DURATION_MS;
        sessionStorage.setItem(this.EXPIRY_KEY, newExpiry.toString());
    }

    /**
     * Generate device fingerprint for session validation
     */
    private static async generateFingerprint(): Promise<string> {
        // Collect device-specific information
        const components = [
            navigator.userAgent,
            navigator.language,
            navigator.hardwareConcurrency?.toString() || '',
            screen.width + 'x' + screen.height,
            screen.colorDepth?.toString() || '',
            new Date().getTimezoneOffset().toString(),
            navigator.platform,
            // Canvas fingerprinting
            await this.getCanvasFingerprint(),
            // WebGL fingerprinting
            await this.getWebGLFingerprint()
        ];
        
        const componentsStr = components.filter(Boolean).join('|');
        
        // Hash the components
        const encoder = new TextEncoder();
        const data = encoder.encode(componentsStr);
        const hashBuffer = await crypto.subtle.digest('SHA-256', data);
        const hashArray = Array.from(new Uint8Array(hashBuffer));
        return hashArray.map(b => b.toString(16).padStart(2, '0')).join('');
    }

    /**
     * Get canvas fingerprint
     */
    private static async getCanvasFingerprint(): Promise<string> {
        try {
            const canvas = document.createElement('canvas');
            const ctx = canvas.getContext('2d');
            if (!ctx) return '';
            
            canvas.width = 200;
            canvas.height = 50;
            
            // Draw text with specific styling
            ctx.textBaseline = 'top';
            ctx.font = '14px "Arial"';
            ctx.textBaseline = 'alphabetic';
            ctx.fillStyle = '#f60';
            ctx.fillRect(125, 1, 62, 20);
            ctx.fillStyle = '#069';
            ctx.fillText('KYC Fingerprint', 2, 15);
            ctx.fillStyle = 'rgba(102, 204, 0, 0.7)';
            ctx.fillText('Security', 4, 17);
            
            // Get data URL and hash it
            const dataURL = canvas.toDataURL();
            const encoder = new TextEncoder();
            const data = encoder.encode(dataURL);
            const hashBuffer = await crypto.subtle.digest('SHA-256', data);
            const hashArray = Array.from(new Uint8Array(hashBuffer));
            return hashArray.map(b => b.toString(16).padStart(2, '0')).join('').slice(0, 16);
        } catch {
            return '';
        }
    }

    /**
     * Get WebGL fingerprint
     */
    private static async getWebGLFingerprint(): Promise<string> {
        try {
            const canvas = document.createElement('canvas');
            const gl = canvas.getContext('webgl') || canvas.getContext('experimental-webgl') as WebGLRenderingContext;
            if (!gl) return '';
            
            const debugInfo = gl.getExtension('WEBGL_debug_renderer_info');
            if (!debugInfo) return '';
            
            const vendor = gl.getParameter(debugInfo.UNMASKED_VENDOR_WEBGL);
            const renderer = gl.getParameter(debugInfo.UNMASKED_RENDERER_WEBGL);
            
            const components = [vendor, renderer].join('|');
            const encoder = new TextEncoder();
            const data = encoder.encode(components);
            const hashBuffer = await crypto.subtle.digest('SHA-256', data);
            const hashArray = Array.from(new Uint8Array(hashBuffer));
            return hashArray.map(b => b.toString(16).padStart(2, '0')).join('').slice(0, 16);
        } catch {
            return '';
        }
    }

    /**
     * Encrypt data using XOR with fingerprint
     * Note: This is a simple encryption. For production, use Web Crypto API
     */
    private static async encrypt(data: string, key: string): Promise<string> {
        try {
            const encoder = new TextEncoder();
            const dataBytes = encoder.encode(data);
            const keyBytes = encoder.encode(key);
            
            const encrypted = new Uint8Array(dataBytes.length);
            for (let i = 0; i < dataBytes.length; i++) {
                encrypted[i] = dataBytes[i] ^ keyBytes[i % keyBytes.length];
            }
            
            // Convert to base64
            return btoa(String.fromCharCode(...encrypted));
        } catch (error) {
            throw new Error('Encryption failed');
        }
    }

    /**
     * Decrypt data using XOR with fingerprint
     */
    private static async decrypt(encrypted: string, key: string): Promise<string> {
        try {
            // Decode from base64
            const encryptedBytes = Uint8Array.from(atob(encrypted), c => c.charCodeAt(0));
            const encoder = new TextEncoder();
            const keyBytes = encoder.encode(key);
            
            const decrypted = new Uint8Array(encryptedBytes.length);
            for (let i = 0; i < encryptedBytes.length; i++) {
                decrypted[i] = encryptedBytes[i] ^ keyBytes[i % keyBytes.length];
            }
            
            return new TextDecoder().decode(decrypted);
        } catch (error) {
            throw new Error('Decryption failed');
        }
    }

    /**
     * Monitor session and trigger callback before expiry
     */
    static monitorSession(
        onExpiringSoon: (remainingMs: number) => void,
        warningThresholdMs: number = 5 * 60 * 1000 // 5 minutes
    ): () => void {
        const intervalId = setInterval(() => {
            const remaining = this.getRemainingTime();
            
            if (remaining === 0) {
                console.log('[SecureSession] Session expired during monitoring');
                this.clearSession();
                clearInterval(intervalId);
            } else if (remaining <= warningThresholdMs) {
                onExpiringSoon(remaining);
            }
        }, 30000); // Check every 30 seconds
        
        // Return cleanup function
        return () => clearInterval(intervalId);
    }

    /**
     * Extend session (useful for active users)
     */
    static async extendSession(): Promise<boolean> {
        const sessionId = await this.getSession();
        if (!sessionId) {
            return false;
        }
        
        // Re-set the session with new expiry
        await this.setSession(sessionId);
        return true;
    }
}

export default SecureSessionManager;
