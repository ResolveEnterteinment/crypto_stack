import { Balance } from "../types/balanceTypes";
import api from "./api";

export const getBalance = async (ticker: string): Promise<Balance | null> => {
    if (!ticker) {
        console.error("getBalances called with undefined ticker");
        return null;
    }
    const response = await api.safeRequest('get', `/Balance/asset/${ticker}`);
    console.log("BalanceService::getBalance => response: ", response);
    if (response == null || response.data == null || response.success != true)
        throw `Failed to get balance for ${ticker}`;
    return response.data;
};

export const getBalances = async (userId: string): Promise<Balance[] | []>  => {
    if (!userId) {
        console.error("getBalances called with undefined userId");
        return [];
    }
    const { data } = await api.get(`/Balance/user/${userId}`);
    return data;
};

export const getTotalInvestments = async (userId: string) => {
    if (!userId) {
        console.error("getTotalInvestments called with undefined userId");
        return [];
    }
    const { data } = await api.get(`/Balance/totalInvestments/${userId}`);
    return data;
};

export const getPortfolioValue = async (userId: string) => {
    if (!userId) {
        console.error("getPortfolioValue called with undefined userId");
        return [];
    }
    const { data } = await api.get(`/Balance/portfolioValue/${userId}`);
    return data;
};

export const updateSubscription = async (subscriptionId: string, updateFields: object) => {
    await api.post(`/Subscription/update/${subscriptionId}`, updateFields)
        .then((response) => console.log(response))
        .catch((error) => console.error(error));
};