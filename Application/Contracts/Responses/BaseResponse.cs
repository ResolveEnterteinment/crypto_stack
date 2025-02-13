namespace Application.Contracts.Responses
{
    public class BaseResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;

        public BaseResponse(bool success, string message)
        {
            Success = success;
            Message = message;
        }
        public BaseResponse()
        {

        }
    }
}
