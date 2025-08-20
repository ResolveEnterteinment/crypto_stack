import api, { PaginatedResult } from './api';
import { WithdrawalLimits, CryptoWithdrawalRequest, NetworkDto, WithdrawalResponse, BankWithdrawalRequest, Withdrawal } from '../types/withdrawal';

// API endpoints
const ENDPOINTS = {
    GET_ONE: (id: string) => `/withdrawal/${id}`,
    GET_LIMITS_BY_USER: (userId: string) => `/withdrawal/admin/limits/user/${userId}`,
    GET_LIMITS_BY_CURRENT_USER: () => `/withdrawal/limits/user/current`,
    GET_SUPPORTED_NETWORKS: (assetTicker: string) => `/withdrawal/networks/${assetTicker}`,
    GET_WITHDRAWAL_MINIMUM: (assetTicker: string) => `/withdrawal/minimum/${assetTicker}`,
    GET_USER_PENDING_TOTALS_BY_ASSET: (userId: string, assetTicker: string) => `/withdrawal/admin/total/pending/user/${userId}/asset/${assetTicker}`,
    GET_CURRENT_USER_PENDING_TOTALS_BY_ASSET: (assetTicker: string) => `/withdrawal/admin/total/pending/user/current/asset/${assetTicker}`,
    CAN_USER_WITHDRAW: () => `/withdrawal/user/current/can-withdraw`,
    REQUEST_CRYPTO_WITHDRAWAL: () => '/withdrawal/crypto/request',
    REQUEST_BANK_WITHDRAWAL: () => '/withdrawal/bank/request',
    GET_HISTORY: () => '/withdrawal/history/user/current',
    CANCEL: (id: string) => `/withdrawal/cancel/${id}`,
    UPDATE_STATUS: (id: string) => `/withdrawal/admin/update-status/${id}`,
    GET_PENDING: () => `/withdrawal/admin/pending`,
    HEALTH_CHECK: '/health'
} as const;

const withdrawalService = {
    // Get user withdrawal levels
    getWithdrawalDetails: async (withdrawalId: string): Promise<WithdrawalResponse> => {
        const response = await api.get<WithdrawalResponse>(ENDPOINTS.GET_ONE(withdrawalId));
        if (response == null || response.data == null || !response.success)
            throw "Failed to get withdrawal details";
        return response.data;
    },
    getUserLimits: async (userId: string): Promise<WithdrawalLimits> => {
        const response = await api.get<WithdrawalLimits>(ENDPOINTS.GET_LIMITS_BY_USER(userId));
        if (response == null || response.data == null || !response.success)
            throw "Failed to get withdrawal limits";
        return response.data;
    },
    getCurrentUserLimits: async (): Promise<WithdrawalLimits> => {
        const response = await api.get<WithdrawalLimits>(ENDPOINTS.GET_LIMITS_BY_CURRENT_USER());
        if (response == null || response.data == null || !response.success)
            throw "Failed to get withdrawal limits";
        return response.data;
    },
    getSupportedNetworks: async (assetTicker: string): Promise<NetworkDto[]> => {
        const response = await api.get<NetworkDto[]>(ENDPOINTS.GET_SUPPORTED_NETWORKS(assetTicker));
        if (response == null || !response.success || response.data == null)
            throw "Failed to get supported networks";
        return response.data;
    },
    getMinimumWithdrawalAmount: async (assetTicker: string): Promise<number> => {
        const response = await api.get<number>(ENDPOINTS.GET_WITHDRAWAL_MINIMUM(assetTicker));
        if (response == null || response.data == null || !response.success)
            throw "Failed to get minimum withdrawal amount";
        return response.data;
    },
    canUserWithdraw: async (amount: number, ticker: string): Promise<{ data: boolean, message: string | undefined }> => {
        const response = await api.post<boolean>(ENDPOINTS.CAN_USER_WITHDRAW(),
            {
                amount: amount,
                ticker: ticker
            });
        if (response == null || response.data == null || !response.success)
            throw "Failed to check user withdrawal eligibility";
        return { data: response.data, message: response.message };
    },
    requestCryproWithdrawal: async (data: CryptoWithdrawalRequest): Promise<WithdrawalResponse> => {
        const response = await api.post<WithdrawalResponse>(ENDPOINTS.REQUEST_CRYPTO_WITHDRAWAL(), data);
        if (response == null || response.data == null || response.success != true)
            throw new Error(response.message ?? "Failed to make crypto withdrawal request");
        return response.data;
    },
    requestBankWithdrawal: async (data: BankWithdrawalRequest): Promise<WithdrawalResponse> => {
        const response = await api.post<WithdrawalResponse>(ENDPOINTS.REQUEST_BANK_WITHDRAWAL(), data);
        if (response == null || response.data == null || response.success != true)
            throw "Failed to make bank withdrawal request";
        return response.data;
    },
    getHistory: async (): Promise<WithdrawalResponse[]> => {
        const response = await api.get<WithdrawalResponse[]>(ENDPOINTS.GET_HISTORY());
        if (response == null || response.data == null || response.success != true)
            throw "Failed to fetch withdrawal history";
        return response.data;
    },
    getPending: async (): Promise<PaginatedResult<Withdrawal>> => {
        const response = await api.get<PaginatedResult<Withdrawal>>(ENDPOINTS.GET_PENDING());
        if (response == null || response.data == null || response.success != true)
            throw "Failed to fetch pending withdrawals";
        return response.data;
    },
    getUserPendingTotals: async (userId: string, assetTicker: string): Promise<number> => {
        const response = await api.get<number>(ENDPOINTS.GET_USER_PENDING_TOTALS_BY_ASSET(userId, assetTicker));
        if (response == null || response.data == null || response.success != true)
            throw "Failed to fetch pending withdrawal totals";
        return response.data;
    },
    getCurrentUserPendingTotals: async (assetTicker: string): Promise<number> => {
        const response = await api.get<number>(ENDPOINTS.GET_CURRENT_USER_PENDING_TOTALS_BY_ASSET(assetTicker));
        if (response == null || response.data == null || response.success != true)
            throw "Failed to fetch pending withdrawal totals";
        return response.data;
    },
    cancelWithdrawal: async (withdrawalId: string): Promise<boolean> => {
        try {
            const response = await api.put<boolean>(ENDPOINTS.CANCEL(withdrawalId));
            if (response == null || response.success !== true)
                throw new Error("Failed to cancel withdrawal request");
            return true;
        } catch (error) {
            console.error("Error cancelling withdrawal:", error);
            throw error; // Re-throw to allow proper error handling
        }
    },
    updateStatus: async (withdrawalId: string, payload: object): Promise<boolean> => {
        try {
            const response = await api.put<boolean>(ENDPOINTS.UPDATE_STATUS(withdrawalId),
            payload);
            if (response == null || response.success !== true)
                throw new Error("Failed to update withdrawal status");
            return true;
        } catch (error) {
            console.error("Error updates withdrawal status:", error);
            throw error; // Re-throw to allow proper error handling
        }
    }
};

export default withdrawalService;