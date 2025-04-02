import ISubscription from "./ISubscription";

export default interface SubscriptionCardProps {
    subscription: ISubscription;
    onEdit: (id: string) => void;
    onCancel: (id: string) => void;
    onViewHistory: (id: string) => void;
}