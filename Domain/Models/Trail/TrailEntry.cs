namespace Domain.Models.Trail
{
    public class TrailEntry : BaseEntity
    {
        public required string Entity { get; set; }
        public required string Action { get; set; }
        public required bool IsSuccess { get; set; }
        public string? Message { get; set; }
    }
}
