import api from './api';

export const keyExchangeService = {
    /**
     * Requests a new encryption key from the server and initializes encryption
     */
    initializeEncryption: async (): Promise<boolean> => {
        try {
            console.info("🔐 Initializing encryption through key exchange...");
            return await api.enableEncryption();
        } catch (error) {
            console.error('❌ Failed to initialize encryption:', error);
            return false;
        }
    },

    /**
     * Disables encryption for all API calls
     */
    disableEncryption: (): void => {
        console.info("🔓 Disabling encryption");
        api.disableEncryption();
    },

    /**
     * Check if encryption is currently enabled
     */
    isEncryptionEnabled: (): boolean => {
        return api.isEncryptionEnabled();
    }
};

export default keyExchangeService;