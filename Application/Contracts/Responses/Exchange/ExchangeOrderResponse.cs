namespace Application.Contracts.Responses.Exchange
{
    public class ExchangeOrderResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;

        public ExchangeOrderResponse(bool success, string message)
        {
            Success = success;

            Message = message;
        }
        public ExchangeOrderResponse()
        {

        }
    }
}
