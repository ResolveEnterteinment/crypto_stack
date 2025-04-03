namespace Application.Contracts.Requests.Asset
{
    public class AssetUpdateRequest
    {
        public string? Name { get; set; }
        public string? Ticker { get; set; }
        public string? Symbol { get; set; }
        public int? Precision { get; set; }
        public string? SubunitName { get; set; }
        public string? Exchange { get; set; }
        public string? Type { get; set; }
    }
}
