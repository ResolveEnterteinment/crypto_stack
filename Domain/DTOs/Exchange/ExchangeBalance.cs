namespace Domain.DTOs.Exchange
{
    public class ExchangeBalance
    {
        public string Ticker { get; set; }
        public decimal Available { get; set; } = decimal.Zero;
        public decimal Locked { get; set; } = decimal.Zero;
    }
}
