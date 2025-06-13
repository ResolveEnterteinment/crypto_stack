/**
 * Client-side encryption service for securing API communication
 * Enhanced with proper error handling and security best practices
 */
class EncryptionService {
    private initialized: boolean;
    private cryptoKey: CryptoKey | null;

    constructor() {
        this.initialized = false;
        this.cryptoKey = null;
    }

    /**
     * Initialize the encryption service with the server-provided key
     * @param {string} encryptionKey - Base64 encoded encryption key from server
     * @returns {Promise<void>}
     * @throws {Error} If encryption key is invalid or initialization fails
     */
    async initialize(encryptionKey: string): Promise<void> {
        if (!encryptionKey) {
            throw new Error('Encryption key is required');
        }

        // If already initialized with the same key, skip
        if (this.initialized && this.cryptoKey) {
            console.debug("Encryption service already initialized");
            return;
        }

        try {
            await this._initializeWithKey(encryptionKey);
            this.initialized = true;
            console.info("✅ Encryption service initialized successfully");
        } catch (error) {
            console.error("❌ Failed to initialize encryption service:", error);
            this.initialized = false;
            this.cryptoKey = null;
            throw error;
        }
    }

    /**
     * Internal method to initialize with a key
     */
    private async _initializeWithKey(encryptionKey: string): Promise<void> {
        // Clean the key (remove any JSON wrapper if present)
        const cleanKey = this._cleanEncryptionKey(encryptionKey);

        // Validate and convert key
        const keyData = this._validateAndDecodeKey(cleanKey);

        // Import the key for use with Web Crypto API
        this.cryptoKey = await crypto.subtle.importKey(
            'raw',
            keyData,
            { name: 'AES-CBC', length: 256 },
            false,
            ['encrypt', 'decrypt']
        );
    }

    /**
     * Clean encryption key from any wrapper formats
     */
    private _cleanEncryptionKey(encryptionKey: string): string {
        let cleanKey = encryptionKey.trim();

        // Remove JSON wrapper if present
        if (cleanKey.includes('"')) {
            try {
                const parsed = JSON.parse(cleanKey);
                if (typeof parsed === 'string') {
                    cleanKey = parsed;
                }
            } catch {
                // Not JSON, use as is
            }
        }

        return cleanKey;
    }

    /**
     * Validate and decode the encryption key
     */
    private _validateAndDecodeKey(cleanKey: string): ArrayBuffer {
        if (!cleanKey) {
            throw new Error('Empty encryption key provided');
        }

        try {
            const keyData = this._base64ToArrayBuffer(cleanKey);

            // Validate key length (must be 32 bytes for AES-256)
            if (keyData.byteLength !== 32) {
                throw new Error(`Invalid key length: ${keyData.byteLength} bytes. Expected 32 bytes for AES-256`);
            }

            return keyData;
        } catch (error) {
            if (error instanceof Error && error.message.includes('Invalid key length')) {
                throw error;
            }
            throw new Error(`Invalid key format: ${error instanceof Error ? error.message : 'Unknown error'}`);
        }
    }

    /**
     * Encrypt data for sending to the server
     * @param {Object|string} data - Data to encrypt
     * @returns {Promise<string>} - JSON string with encrypted payload
     * @throws {Error} If encryption service not initialized or encryption fails
     */
    async encrypt(data: any): Promise<string> {
        if (!this.initialized || !this.cryptoKey) {
            throw new Error('Encryption service not initialized');
        }

        try {
            // Convert data to string if it's an object
            const dataString = typeof data === 'object' ? JSON.stringify(data) : String(data);

            // Convert to buffer for encryption
            const encodedData = new TextEncoder().encode(dataString);

            // Generate a new IV for each encryption operation (security best practice)
            const iv = crypto.getRandomValues(new Uint8Array(16));

            // Encrypt the data
            const encryptedData = await crypto.subtle.encrypt(
                { name: 'AES-CBC', iv },
                this.cryptoKey,
                encodedData
            );

            // Create the encrypted payload compatible with server expectations
            const payload = {
                iv: this._arrayBufferToBase64(iv),
                data: this._arrayBufferToBase64(encryptedData)
            };

            return JSON.stringify(payload);
        } catch (error) {
            console.error('Encryption failed:', error);
            throw new Error(`Encryption failed: ${error instanceof Error ? error.message : 'Unknown error'}`);
        }
    }

    /**
     * Decrypt data received from the server
     * @param {string} encryptedData - Encrypted payload from server
     * @returns {Promise<any>} - Decrypted data
     * @throws {Error} If encryption service not initialized or decryption fails
     */
    async decrypt(encryptedData: string): Promise<any> {
        if (!this.initialized || !this.cryptoKey) {
            throw new Error('Encryption service not initialized');
        }

        try {
            // Extract payload from server response wrapper if present
            const actualPayload = this._extractPayload(encryptedData);

            // Parse the encrypted payload
            const payload = JSON.parse(actualPayload);

            if (!payload.iv || !payload.data) {
                throw new Error('Invalid encrypted payload format: missing iv or data');
            }

            // Extract IV and encrypted data
            const iv = this._base64ToArrayBuffer(payload.iv);
            const data = this._base64ToArrayBuffer(payload.data);

            // Decrypt the data
            const decryptedData = await crypto.subtle.decrypt(
                { name: 'AES-CBC', iv },
                this.cryptoKey,
                data
            );

            // Convert back to string
            const decryptedString = new TextDecoder().decode(decryptedData);

            // Try to parse as JSON, return as string if not valid JSON
            try {
                return JSON.parse(decryptedString);
            } catch {
                return decryptedString;
            }
        } catch (error) {
            console.error('Decryption failed:', error);
            throw new Error(`Decryption failed: ${error instanceof Error ? error.message : 'Unknown error'}`);
        }
    }

    /**
     * Extract payload from server response wrapper
     */
    private _extractPayload(encryptedData: string): string {
        try {
            const payloadWrapper = JSON.parse(encryptedData);
            if (payloadWrapper.Payload || payloadWrapper.payload) {
                return JSON.stringify(payloadWrapper.Payload || payloadWrapper.payload);
            }
        } catch {
            // Not a JSON wrapper, use as is
        }
        return encryptedData;
    }

    /**
     * Convert Base64 string to ArrayBuffer
     * @param {string} base64 - Base64 encoded string
     * @returns {ArrayBuffer} - Decoded ArrayBuffer
     * @private
     */
    private _base64ToArrayBuffer(base64: string): ArrayBuffer {
        try {
            const binaryString = window.atob(base64);
            const bytes = new Uint8Array(binaryString.length);
            for (let i = 0; i < binaryString.length; i++) {
                bytes[i] = binaryString.charCodeAt(i);
            }
            return bytes.buffer;
        } catch (error) {
            throw new Error(`Invalid base64 string: ${error instanceof Error ? error.message : 'Unknown error'}`);
        }
    }

    /**
     * Convert ArrayBuffer to Base64 string
     * @param {ArrayBuffer} buffer - ArrayBuffer to encode
     * @returns {string} - Base64 encoded string
     * @private
     */
    private _arrayBufferToBase64(buffer: ArrayBuffer): string {
        const bytes = new Uint8Array(buffer);
        let binary = '';
        for (let i = 0; i < bytes.byteLength; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        return window.btoa(binary);
    }

    /**
     * Check if encryption service is initialized
     * @returns {boolean} - True if initialized and ready to use
     */
    isInitialized(): boolean {
        return this.initialized && this.cryptoKey !== null;
    }

    /**
     * Reset the encryption service (for testing or key rotation)
     */
    reset(): void {
        this.initialized = false;
        this.cryptoKey = null;
        console.info("Encryption service reset");
    }
}

export default new EncryptionService();