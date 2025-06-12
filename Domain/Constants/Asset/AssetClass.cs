namespace Domain.Constants
{
    public class AssetClass
    {
        public const string Crypto = "CRYPTO";
        public const string Stablecoin = "STABLECOIN";

        public static readonly List<string> AllValues = new List<string>()
        {
            Crypto, Stablecoin
        };
    }
}
