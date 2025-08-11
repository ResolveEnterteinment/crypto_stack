import api from './api';
import { WithdrawalLimits, CryptoWithdrawalRequest, NetworkDto, WithdrawalResponse, BankWithdrawalRequest } from '../types/withdrawal';

const withdrawalService = {
    // Get user withdrawal levels
    getLevels: async (): Promise<WithdrawalLimits> => {
        const response = await api.safeRequest<WithdrawalLimits>('get', '/withdrawal/limits');
        console.log("withdrawalService::getLevels => response: ", response)
        if (response == null || response.data == null || !response.success)
            throw "Failed to get withdrawal limits";
        return response.data;
    },
    getUserLevels: async (userId: string): Promise<WithdrawalLimits> => {
        const response = await api.safeRequest<WithdrawalLimits>('get', `/withdrawal/limits/user/${userId}`);
        console.log("withdrawalService::getLevels => response: ", response)
        if (response == null || response.data == null || !response.success)
            throw "Failed to get withdrawal limits";
        return response.data;
    },
    getSupportedNetworks: async (assetTicker: string): Promise<NetworkDto[]> => {
        const response = await api.safeRequest<NetworkDto[]>('get', `/withdrawal/networks/${assetTicker}`);
        console.log("withdrawalService::getSupportedNetworks => response: ", response)
        if (response == null || !response.success || response.data == null)
            throw "Failed to get supported networks";
        return response.data;
    },
    getMinimumWithdrawalAmount: async (assetTicker: string): Promise<number> => {
        const response = await api.safeRequest<number>('get', `/withdrawal/minimum/${assetTicker}`);
        console.log("withdrawalService::getMinimumWithdrawalAmount => response: ", response)
        if (response == null || response.data == null || !response.success)
            throw "Failed to get minimum withdrawal amount";
        return response.data;
    },
    canUserWithdraw: async (amount: number, ticker: string): Promise<{ data: boolean, message: string | undefined }> => {
        const response = await api.safeRequest<boolean>('post', `/withdrawal/can-withdraw`,
            {
                amount: amount,
                ticker: ticker
            });
        console.log("withdrawalService::canUserWithdraw => response: ", response)
        if (response == null || response.data == null || !response.success)
            throw "Failed to check user withdrawal eligibility";
        return { data: response.data, message: response.message };
    },
    requestCryproWithdrawal: async (data: CryptoWithdrawalRequest): Promise<WithdrawalResponse> => {
        const response = await api.safeRequest('post', '/withdrawal/crypto/request', data);
        if (response == null || response.data == null || response.success != true)
            throw new Error(response.message ?? "Failed to make crypto withdrawal request");
        return response.data;
    },
    requestBankWithdrawal: async (data: BankWithdrawalRequest): Promise<WithdrawalResponse> => {
        const response = await api.safeRequest('post', '/withdrawal/bank/request', data);
        if (response == null || response.data == null || response.success != true)
            throw "Failed to make bank withdrawal request";
        return response.data;
    },
    getHistory: async (): Promise<WithdrawalResponse[]> => {
        const response = await api.safeRequest('get', '/withdrawal/history');
        console.log("withdrawalService::getHistory => response: ", response)
        if (response == null || response.data == null || response.success != true)
            throw "Failed to fetch withdrawal history";
        return response.data;
    },

    getUserPendingTotals: async (userId: string, assetTicker: string): Promise<number> => {
        const response = await api.safeRequest('get', `/withdrawal/pending/total/${assetTicker}/user/${userId}`);
        console.log("withdrawalService::getPendingTotals => response: ", response)
        if (response == null || response.data == null || response.success != true)
            throw "Failed to fetch pending withdrawal totals";
        return response.data;
    },
    getPendingTotals: async (assetTicker: string): Promise<number> => {
        const response = await api.safeRequest('get', `/withdrawal/pending/total/${assetTicker}`);
        console.log("withdrawalService::getPendingTotals => response: ", response)
        if (response == null || response.data == null || response.success != true)
            throw "Failed to fetch pending withdrawal totals";
        return response.data;
    },
    cancelWithdrawal: async (withdrawalId: string): Promise<boolean> => {
        try {
            const response = await api.safeRequest('put', `/withdrawal/${withdrawalId}/cancel`);
            console.log("cancelWithdrawal response:", response); // Add logging
            if (response == null || response.success !== true) {
                throw new Error("Failed to cancel withdrawal request");
            }
            return true;
        } catch (error) {
            console.error("Error cancelling withdrawal:", error);
            throw error; // Re-throw to allow proper error handling
        }
    }
};

export default withdrawalService;