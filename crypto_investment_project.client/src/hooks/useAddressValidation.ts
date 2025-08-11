import { useState, useCallback, useMemo } from 'react';
import { AddressValidationService } from '../services/addressValidationService';
import { ValidationResult } from '../constants/AddressValidation';

interface UseAddressValidationProps {
    network: string;
    currency: string;
    validateOnChange?: boolean;
    debounceMs?: number;
}

interface AddressValidationState {
    address: string;
    memo: string;
    addressValidation: ValidationResult | null;
    memoValidation: ValidationResult | null;
    isValidating: boolean;
}

export const useAddressValidation = ({
    network,
    currency,
    validateOnChange = true,
    debounceMs = 300
}: UseAddressValidationProps) => {
    const [state, setState] = useState<AddressValidationState>({
        address: '',
        memo: '',
        addressValidation: null,
        memoValidation: null,
        isValidating: false
    });

    const validateAddress = useCallback(async (address: string) => {
        if (!address.trim()) {
            setState(prev => ({ ...prev, addressValidation: null }));
            return;
        }

        setState(prev => ({ ...prev, isValidating: true }));

        // Simulate async validation (in case you want to add server-side validation later)
        setTimeout(() => {
            const validation = AddressValidationService.validateAddress(address, network, currency);
            setState(prev => ({
                ...prev,
                addressValidation: validation,
                isValidating: false
            }));
        }, 100);
    }, [network, currency]);

    const validateMemo = useCallback(async (memo: string) => {
        const validation = AddressValidationService.validateMemo(memo, network);
        setState(prev => ({ ...prev, memoValidation: validation }));
    }, [network]);

    const setAddress = useCallback((address: string) => {
        setState(prev => ({ ...prev, address }));
        if (validateOnChange) {
            validateAddress(address);
        }
    }, [validateAddress, validateOnChange]);

    const setMemo = useCallback((memo: string) => {
        setState(prev => ({ ...prev, memo }));
        if (validateOnChange) {
            validateMemo(memo);
        }
    }, [validateMemo, validateOnChange]);

    const networkInfo = useMemo(() => {
        return AddressValidationService.getNetworkInfo(network);
    }, [network]);

    const isValid = useMemo(() => {
        const addressValid = state.addressValidation?.isValid ?? false;
        const memoValid = networkInfo?.requiresMemo 
            ? (state.memoValidation?.isValid ?? false)
            : true;
        return addressValid && memoValid;
    }, [state.addressValidation, state.memoValidation, networkInfo]);

    return {
        ...state,
        setAddress,
        setMemo,
        validateAddress,
        validateMemo,
        networkInfo,
        isValid,
        requiresMemo: networkInfo?.requiresMemo ?? false
    };
};