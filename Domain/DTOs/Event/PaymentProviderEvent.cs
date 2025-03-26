namespace Domain.DTOs.Event
{
    public class PaymentProviderEvent
    {
        public string Provider { get; set; }
        public string Type { get; set; }
        public object Object { get; set; }
        public object Data { get; set; }
    }
}
