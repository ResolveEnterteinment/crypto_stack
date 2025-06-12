using Domain.Attributes;

namespace Domain.Models.Network
{
    [BsonCollection("networks")]
    public class NetworkData : BaseEntity
    {
        public required string Name { get; set; }
        public required string TokenStandard { get; set; }
        public bool RequiresMemo { get; set; }
        public required string AddressRegex { get; set; }
        public int AddressMinLength { get; set; }
        public int AddressMaxLength { get; set; }
        public string? Icon { get; set; }
        public bool IsActive { get; set; } = true;
        public List<string> SupportedAssets { get; set; } = [];
        public Dictionary<string, object> AdditionalProperties { get; set; } = [];
    }
}
