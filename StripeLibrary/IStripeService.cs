namespace StripeLibrary
{
    public interface IStripeService
    {
        public Task<decimal> GetStripeFeeAsync(string paymentIntentId);
    }
}
