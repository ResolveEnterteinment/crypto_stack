import { Allocation } from "../types/subscription";

export default interface IUpdateSubscriptionRequest {
    allocations?: Allocation[];
    interval?: string;
    amount?: number;
    currency?: string;
    endDate?: Date;
    isCancelled?: boolean;
}