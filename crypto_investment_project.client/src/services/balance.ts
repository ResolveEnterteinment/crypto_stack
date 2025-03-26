import api from "./api";

export interface IBalance {
    userId: string;
    assetId: string;
    ticker: string;
    available: number;
    locked: number;
    total: number;
}

export const getBalances = async (userId: string) => {
    if (!userId) {
        console.error("getBalances called with undefined userId");
        return [];
    }
    const { data } = await api.post(`/Balance/user/${userId}`);
    return data;
};

export const getTotalInvestments = async (userId: string) => {
    if (!userId) {
        console.error("getTotalInvestments called with undefined userId");
        return [];
    }
    const { data } = await api.post(`/Balance/totalInvestments/${userId}`);
    return data;
};

export const getPortfolioValue = async (userId: string) => {
    if (!userId) {
        console.error("getPortfolioValue called with undefined userId");
        return [];
    }
    const { data } = await api.post(`/Balance/portfolioValue/${userId}`);
    return data;
};

export const updateSubscription = async (subscriptionId: string, updateFields: object) => {
    await api.post(`/Subscription/update/${subscriptionId}`, updateFields)
        .then((response) => console.log(response))
        .catch((error) => console.error(error));
};