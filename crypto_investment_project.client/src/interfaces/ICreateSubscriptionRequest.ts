import IAllocationRequest from "./IAllocationRequest";

export default interface ICreateSubscriptionRequest {
    userId: string;
    allocations: IAllocationRequest[];
    interval: string;
    amount: number;
    currency: string;
    endDate?: Date | null;
}