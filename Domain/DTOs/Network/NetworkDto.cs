namespace Domain.DTOs.Network
{
    public class NetworkDto
    {
        public required string Name { get; set; }
        public required string TokenStandard { get; set; }
        public required bool RequiresMemo { get; set; }
    }
}
