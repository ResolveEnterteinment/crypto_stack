import IAllocation from "./IAllocation";

export default interface ICreateSubscriptionRequest {
    userId: string;
    allocations: Omit<IAllocation, 'id'>[];
    interval: string;
    amount: number;
    currency: string;
    endDate?: Date | null;
}