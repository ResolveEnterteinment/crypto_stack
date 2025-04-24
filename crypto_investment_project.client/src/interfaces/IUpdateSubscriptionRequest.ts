import IAllocation from "./IAllocation";

export default interface IUpdateSubscriptionRequest {
    allocations?: IAllocation[];
    interval?: string;
    amount?: number;
    currency?: string;
    endDate?: Date;
    isCancelled?: boolean;
}