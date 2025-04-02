import IAllocation from "./IAllocation";

export default interface ISubscription {
    id: string;
    createdAt: string;
    userId: string;
    allocations: IAllocation[];
    interval: string;
    amount: number;
    currency: string;
    nextDueDate: Date | string;
    endDate: Date | string;
    totalInvestments: number;
    isCancelled: boolean;
}