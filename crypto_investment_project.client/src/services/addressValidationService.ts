import { ADDRESS_VALIDATION_RULES, MEMO_VALIDATION_RULES, ValidationResult, AddressValidationRule } from '../constants/AddressValidation';

export class AddressValidationService {
    /**
     * Validates a cryptocurrency address for a specific network
     */
    static validateAddress(address: string, network: string, currency: string): ValidationResult {
        const result: ValidationResult = {
            isValid: false,
            errors: [],
            warnings: [],
            suggestions: []
        };

        // Basic input validation
        if (!address || !network) {
            result.errors.push('Address and network are required');
            return result;
        }

        // Clean the address
        const cleanAddress = address.trim();
        
        // Get validation rule for the network
        const rule = ADDRESS_VALIDATION_RULES[network];
        if (!rule) {
            result.errors.push(`Validation rules not found for network: ${network}`);
            result.suggestions.push('Please select a supported network');
            return result;
        }

        // Check if currency is supported on this network
        if (!rule.supportedAssets.includes(currency.toUpperCase())) {
            result.errors.push(`${currency} is not supported on ${network} network`);
            result.suggestions.push(`Supported assets on ${network}: ${rule.supportedAssets.join(', ')}`);
            return result;
        }

        // Length validation
        if (cleanAddress.length < rule.minLength) {
            result.errors.push(`Address too short. Minimum length: ${rule.minLength}, current: ${cleanAddress.length}`);
        }

        if (cleanAddress.length > rule.maxLength) {
            result.errors.push(`Address too long. Maximum length: ${rule.maxLength}, current: ${cleanAddress.length}`);
        }

        // Regex pattern validation
        if (!rule.regex.test(cleanAddress)) {
            result.errors.push(`Invalid address format for ${network}`);
            result.suggestions.push(`Expected format: ${rule.addressFormat}`);
            result.suggestions.push(`Example: ${rule.examples[0]}`);
        }

        // Network-specific validations
        this.performNetworkSpecificValidation(cleanAddress, network, result);

        // If no errors, mark as valid
        result.isValid = result.errors.length === 0;

        // Add helpful suggestions if validation failed
        if (!result.isValid) {
            result.suggestions.push(...rule.commonMistakes.map(mistake => `Avoid: ${mistake}`));
            result.suggestions.push('Double-check the address in your wallet');
            result.suggestions.push('Copy the address directly to avoid typos');
        }

        return result;
    }

    /**
     * Validates memo/tag for networks that require it
     */
    static validateMemo(memo: string, network: string): ValidationResult {
        const result: ValidationResult = {
            isValid: false,
            errors: [],
            warnings: [],
            suggestions: []
        };

        const rule = ADDRESS_VALIDATION_RULES[network];
        if (!rule) {
            result.errors.push(`Unknown network: ${network}`);
            return result;
        }

        if (!rule.requiresMemo) {
            result.isValid = true;
            if (memo && memo.trim()) {
                result.warnings.push(`Memo not required for ${network}, but will be included`);
            }
            return result;
        }

        if (!memo || !memo.trim()) {
            result.errors.push(`Memo/tag is required for ${network}`);
            result.suggestions.push('Check if your destination wallet requires a memo/tag');
            return result;
        }

        // Get memo validation rule
        const memoRule = MEMO_VALIDATION_RULES[rule.tokenStandard as keyof typeof MEMO_VALIDATION_RULES];
        if (memoRule && !memoRule.regex.test(memo.trim())) {
            result.errors.push(`Invalid memo format for ${network}`);
            result.suggestions.push(memoRule.description);
            result.suggestions.push(`Example: ${memoRule.examples[0]}`);
        } else {
            result.isValid = true;
        }

        return result;
    }

    /**
     * Performs network-specific validation checks
     */
    private static performNetworkSpecificValidation(address: string, network: string, result: ValidationResult): void {
        switch (network) {
            case 'Bitcoin':
                this.validateBitcoinAddress(address, result);
                break;
            case 'Ethereum':
            case 'Binance Smart Chain':
                this.validateEthereumLikeAddress(address, result);
                break;
            case 'Tron':
                this.validateTronAddress(address, result);
                break;
            case 'Solana':
                this.validateSolanaAddress(address, result);
                break;
            case 'Ripple':
                this.validateRippleAddress(address, result);
                break;
        }
    }

    private static validateBitcoinAddress(address: string, result: ValidationResult): void {
        // Check for common Bitcoin address format issues
        if (address.startsWith('bc1') && address.includes('1') && address.includes('0')) {
            result.warnings.push('Bech32 addresses should not contain 1 and 0 characters together');
        }
        
        if (address.length === 34 && address.startsWith('1')) {
            // Legacy address
            result.warnings.push('Using legacy address format. Consider using newer formats for lower fees');
        }
    }

    private static validateEthereumLikeAddress(address: string, result: ValidationResult): void {
        // Check for EIP-55 checksum (mixed case)
        const hasUpperCase = /[A-F]/.test(address.substring(2));
        const hasLowerCase = /[a-f]/.test(address.substring(2));
        
        if (hasUpperCase && hasLowerCase) {
            result.warnings.push('Address appears to use EIP-55 checksum. Ensure case sensitivity is preserved');
        }
    }

    private static validateTronAddress(address: string, result: ValidationResult): void {
        // Check for base58 characters
        const invalidChars = address.match(/[0OIl]/g);
        if (invalidChars) {
            result.errors.push('TRON addresses cannot contain: 0, O, I, l');
        }
    }

    private static validateSolanaAddress(address: string, result: ValidationResult): void {
        // Check for base58 characters
        const invalidChars = address.match(/[0OIl]/g);
        if (invalidChars) {
            result.errors.push('Solana addresses cannot contain: 0, O, I, l');
        }
    }

    private static validateRippleAddress(address: string, result: ValidationResult): void {
        // XRP addresses use base58 but different validation
        if (address.includes('0') || address.includes('O') || address.includes('I') || address.includes('l')) {
            result.warnings.push('XRP addresses should not contain: 0, O, I, l');
        }
    }

    /**
     * Gets helpful information about a network's address format
     */
    static getNetworkInfo(network: string): AddressValidationRule | null {
        return ADDRESS_VALIDATION_RULES[network] || null;
    }

    /**
     * Gets all supported networks
     */
    static getSupportedNetworks(): string[] {
        return Object.keys(ADDRESS_VALIDATION_RULES);
    }

    /**
     * Checks if a network requires memo/tag
     */
    static requiresMemo(network: string): boolean {
        const rule = ADDRESS_VALIDATION_RULES[network];
        return rule ? rule.requiresMemo : false;
    }
}