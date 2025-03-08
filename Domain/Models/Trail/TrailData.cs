namespace Domain.Models.Trail
{
    public class TrailData : BaseEntity
    {
        public IEnumerable<TrailEntry> Entries { get; set; } = Enumerable.Empty<TrailEntry>();
    }
}
