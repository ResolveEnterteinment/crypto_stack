import api from './api';
import { WithdrawalLimits, WithdrawalRequest, Withdrawal, NetworkDto, WithdrawalResponse } from '../types/withdrawal';

const withdrawalService = {
    // Get user withdrawal levels
    getLevels: async (): Promise<WithdrawalLimits> => {
        const response = await api.safeRequest('get', '/withdrawal/limits');
        console.log("withdrawalService::getLevels => response: ", response)
        if (response == null || response.data == null || response.success != true)
            throw "Failed to get withdrawal limits";
        return response.data;
    },
    getSupportedNetworks: async (assetTicker:string): Promise<NetworkDto[]> => {
        const response = await api.safeRequest('get', `/withdrawal/networks/${assetTicker}`);
        if (response == null || response.success != true || response.data == null)
            throw "Failed to get supported networks";
        return response.data.data;
    },
    requestWithdrawal: async (data: WithdrawalRequest): Promise<Withdrawal> => {
        const response = await api.safeRequest('post', '/withdrawal/request', data);
        if (response == null || response.data == null || response.success != true)
            throw "Failed to make withdrawal request";
        return response.data;
    },
    getHistory: async (): Promise<WithdrawalResponse[]> => {
        const response = await api.safeRequest('get', '/withdrawal/history');
        console.log("withdrawalService::getHistory => response: ", response)
        if (response == null || response.data == null || response.success != true)
            throw "Failed to fetch withdrawal history";
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