// Domain/DTOs/Settings/KycServiceSettings.cs
namespace Domain.DTOs.Settings
{
    public class KycServiceSettings
    {
        public string DefaultProvider { get; set; } = "Onfido";
        public bool EnableRoundRobin { get; set; } = false;
        public Dictionary<string, double> ProviderWeights { get; set; } = new Dictionary<string, double>
        {
            { "Onfido", 0.5 },
            { "SumSub", 0.5 }
        };
    }
}