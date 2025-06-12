namespace Domain.Constants.Asset
{
    public class AssetType
    {
        public const string Exchange = "EXCHANGE";
        public const string Platform = "PLATFORM";

        public static readonly List<string> AllValues = new List<string>()
        {
            Exchange, Platform
        };
    }
}
