import api from "./api";

export interface IAllocation {
    id: string;
    ticker: string;
    percentAmount: number;
}
export interface ISubscription {
    id: string;
    createdAt: string;
    userId: string;
    allocations: IAllocation[];
    interval: string;
    amount: number;
    currency: string;
    nextDueDate: Date;
    endDate: Date;
    totalInvestments: number;
    isCancelled: boolean;
    isRead: boolean;
}

export const getSubscriptions = async (userId: string) => {
    if (!userId) {
        console.error("getSubscriptions called with undefined userId");
        return [];
    }
    const { data } = await api.post(`/Subscription/user/${userId}`);
    return data;
};

export const updateSubscription = async (subscriptionId: string, updateFields: object) => {
    await api.post(`/Subscription/update/${subscriptionId}`, updateFields)
        .then((response) => console.log(response))
        .catch((error) => console.error(error));
};