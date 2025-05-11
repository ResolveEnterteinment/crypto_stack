import { Subscription } from "../types/subscription";

export default interface SubscriptionCardProps {
    subscription: Subscription;
    onEdit: (id: string) => void;
    onCancel: (id: string) => void;
    onViewHistory: (id: string) => void;
}