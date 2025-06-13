// Interfaces for the component
export default interface Transaction {
    action: string;
    assetName: string;
    assetTicker: string;
    quantity: number;
    createdAt: Date;
    quoteQuantity: number;
    quoteCurrency: string;
}