namespace Application.Interfaces.Exchange
{
    public interface IOrderReconciliationService
    {
        public Task ReconcilePendingOrdersAsync();
    }
}
