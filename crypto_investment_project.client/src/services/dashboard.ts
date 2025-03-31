import api from "./api";

export interface IBalance {
    assetName: string;
    ticker: string;
    symbol: string;
    available: number;
    locked: number;
    total: number;
}
export interface IDashboardData {
    assetHoldings: IBalance[];
    portfolioValue: number;
    totalInvestments: number;
}

export const getDashboardData = async (userId: string) : Promise<IDashboardData> => {
    if (!userId) {
        console.error("getDashboardData called with undefined userId");
        return Promise.reject();
    }
    const { data } = await api.post(`/Dashboard/user/${userId}`);
    console.log("getDashboardData => data.data: ", data.data);
    return data.data;
};