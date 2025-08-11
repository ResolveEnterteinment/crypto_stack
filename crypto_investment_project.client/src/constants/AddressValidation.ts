/**
 * Comprehensive cryptocurrency address validation configuration
 * Contains regex patterns, validation rules, and network-specific settings
 */

export interface AddressValidationRule {
    network: string;
    tokenStandard: string;
    regex: RegExp;
    minLength: number;
    maxLength: number;
    requiresMemo: boolean;
    supportedAssets: string[];
    examples: string[];
    description: string;
    addressFormat: string;
    memoFormat?: string;
    commonMistakes: string[];
}

export interface ValidationResult {
    isValid: boolean;
    errors: string[];
    warnings: string[];
    suggestions: string[];
}

export const ADDRESS_VALIDATION_RULES: Record<string, AddressValidationRule> = {
    'Bitcoin': {
        network: 'Bitcoin',
        tokenStandard: 'BTC',
        regex: /^(bc1|[13])[a-zA-HJ-NP-Z0-9]{25,62}$/,
        minLength: 26,
        maxLength: 90,
        requiresMemo: false,
        supportedAssets: ['BTC'],
        examples: [
            '1A1zP1eP5QGefi2DMPTfTL5SLmv7DivfNa',
            '3J98t1WpEZ73CNmQviecrnyiWrnqRhWNLy',
            'bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4'
        ],
        description: 'Bitcoin addresses can be Legacy (1...), Script Hash (3...), or Bech32 (bc1...)',
        addressFormat: 'Legacy: 1... | Script Hash: 3... | Bech32: bc1...',
        commonMistakes: [
            'Confusing Bitcoin Cash addresses with Bitcoin addresses',
            'Using uppercase letters in Bech32 addresses',
            'Including spaces or special characters'
        ]
    },
    'Ethereum': {
        network: 'Ethereum',
        tokenStandard: 'ERC20',
        regex: /^0x[a-fA-F0-9]{40}$/,
        minLength: 42,
        maxLength: 42,
        requiresMemo: false,
        supportedAssets: ['ETH', 'USDT', 'USDC', 'LINK'],
        examples: [
            '0x742d35Cc4C7D8E7F4A0a1f9b8F8B8D8E8F4A0a1f9b',
            '0x8ba1f109551bD432803012645Hac136c2C5a9532'
        ],
        description: 'Ethereum addresses start with 0x followed by 40 hexadecimal characters',
        addressFormat: '0x + 40 hexadecimal characters',
        commonMistakes: [
            'Missing 0x prefix',
            'Wrong length (not exactly 40 hex characters)',
            'Using invalid characters (only 0-9, a-f, A-F allowed)'
        ]
    },
    'Tron': {
        network: 'Tron',
        tokenStandard: 'TRC20',
        regex: /^T[a-zA-Z0-9]{33}$/,
        minLength: 34,
        maxLength: 34,
        requiresMemo: false,
        supportedAssets: ['TRX', 'USDT'],
        examples: [
            'TLyqzVGLV1srkB7dToTAEqgDSfPtXRJZYH',
            'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t'
        ],
        description: 'TRON addresses start with T followed by 33 base58 characters',
        addressFormat: 'T + 33 base58 characters',
        commonMistakes: [
            'Not starting with T',
            'Wrong length (must be exactly 34 characters)',
            'Using invalid base58 characters (0, O, I, l not allowed)'
        ]
    },
    'Binance Smart Chain': {
        network: 'Binance Smart Chain',
        tokenStandard: 'BEP20',
        regex: /^0x[a-fA-F0-9]{40}$/,
        minLength: 42,
        maxLength: 42,
        requiresMemo: false,
        supportedAssets: ['BNB', 'USDT'],
        examples: [
            '0x742d35Cc4C7D8E7F4A0a1f9b8F8B8D8E8F4A0a1f9b',
            '0x8ba1f109551bD432803012645Hac136c2C5a9532'
        ],
        description: 'BSC addresses use the same format as Ethereum addresses',
        addressFormat: '0x + 40 hexadecimal characters',
        commonMistakes: [
            'Confusing with Ethereum addresses (same format but different network)',
            'Missing 0x prefix',
            'Wrong length or invalid characters'
        ]
    },
    'Ripple': {
        network: 'Ripple',
        tokenStandard: 'XRP',
        regex: /^r[0-9a-zA-Z]{24,34}$/,
        minLength: 25,
        maxLength: 35,
        requiresMemo: true,
        supportedAssets: ['XRP'],
        examples: [
            'rDNa8D3d9w2b2c3XN9HUHE8Y8zc3XN9HUHE8Y8',
            'rN7n2otiU4E1K5h2b2c3XN9HUHE8Y8c3XN9HUH'
        ],
        description: 'XRP addresses start with r followed by 24-34 characters',
        addressFormat: 'r + 24-34 alphanumeric characters',
        memoFormat: 'Numeric destination tag (usually required for exchanges)',
        commonMistakes: [
            'Forgetting the destination tag when sending to exchanges',
            'Wrong address length',
            'Not starting with r'
        ]
    },
    'Solana': {
        network: 'Solana',
        tokenStandard: 'SPL',
        regex: /^[1-9A-HJ-NP-Za-km-z]{32,44}$/,
        minLength: 32,
        maxLength: 44,
        requiresMemo: false,
        supportedAssets: ['SOL', 'USDC'],
        examples: [
            '7xKXtg2CW87d97TXJSDpbD5jBkheTqA83TZRuJosgAsU',
            'DRpbCBMxVnDK7maPM5tGv6MvB3v1sRMC7DRpbCBMxVnD'
        ],
        description: 'Solana addresses are 32-44 characters in base58 format',
        addressFormat: '32-44 base58 characters',
        commonMistakes: [
            'Using invalid base58 characters (0, O, I, l not allowed)',
            'Wrong length',
            'Confusing with other base58 addresses'
        ]
    },
    'Cardano': {
        network: 'Cardano',
        tokenStandard: 'ADA',
        regex: /^(addr1|stake1)[0-9a-z]{53,98}$/,
        minLength: 59,
        maxLength: 104,
        requiresMemo: false,
        supportedAssets: ['ADA'],
        examples: [
            'addr1qxy2tnlkr5lcmhac73v8p8v8p8v8p8v8p8v8p8v8p8v8p8v8p8v8p8v8p8v8p8v8p8',
            'addr1q9v3v8p8v8p8v8p8v8p8v8p8v8p8v8p8v8p8v8p8v8p8v8p8v8p8v8p8v8p8v8p8'
        ],
        description: 'Cardano addresses start with addr1 or stake1 followed by bech32 characters',
        addressFormat: 'addr1 or stake1 + bech32 characters',
        commonMistakes: [
            'Not starting with addr1 or stake1',
            'Wrong length',
            'Using uppercase letters in bech32 format'
        ]
    }
};

export const MEMO_VALIDATION_RULES = {
    XRP: {
        regex: /^\d{1,10}$/,
        description: 'Destination tag must be a number between 1-10 digits',
        examples: ['12345', '9876543210']
    },
    EOS: {
        regex: /^[a-zA-Z0-9]{1,256}$/,
        description: 'Memo can contain alphanumeric characters, up to 256 characters',
        examples: ['exchange-deposit', 'user123']
    }
};