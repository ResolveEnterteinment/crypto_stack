import IAllocation from "./IAllocation";

export default interface IUpdateSubscriptionRequest {
    allocations?: Omit<IAllocation, 'id'>[];
    interval?: string;
    amount?: number;
    currency?: string;
    endDate?: Date;
    isCancelled?: boolean;
}