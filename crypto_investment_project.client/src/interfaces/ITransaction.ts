export default interface ITransaction {
    action: string;
    assetName: string;
    assetTicker: string;
    quantity: number;
    createdAt: Date;
    quoteQuantity: number;
    quoteCurrency: string;
}