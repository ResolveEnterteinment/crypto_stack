namespace Application.Contracts
{
    public class ApiResponse<T>
    {
        public T? Data { get; set; }
        public List<string> ErrorMessages { get; set; } = new List<string>();

        public ApiResponse(T data)
        {
            Data = data;
        }

        public ApiResponse(List<string> errorMessages)
        {
            ErrorMessages = errorMessages;
        }
    }
}
