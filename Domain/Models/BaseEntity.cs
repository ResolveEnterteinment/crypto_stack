namespace Domain.Models
{
    public class BaseEntity
    {
        public required Guid Id { get; set; }
        public required DateTime CreateTime { get; set; }
    }
}
