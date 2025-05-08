// src/config/kycProviders.ts
export interface KycProviderConfig {
    name: string;
    displayName: string;
    logoUrl: string;
    description: string;
}

export const kycProviders: KycProviderConfig[] = [
    {
        name: 'Onfido',
        displayName: 'Onfido',
        logoUrl: '/images/onfido-logo.svg',
        description: 'Fast identity verification with advanced document and biometric checks.'
    },
    {
        name: 'SumSub',
        displayName: 'Sum&Substance',
        logoUrl: '/images/sumsub-logo.svg',
        description: 'Global compliance platform covering 220+ countries and territories.'
    }
];

export const getKycProviderConfig = (name: string): KycProviderConfig | undefined => {
    return kycProviders.find(provider => provider.name === name);
};