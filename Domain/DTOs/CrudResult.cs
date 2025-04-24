namespace Domain.DTOs
{
    public class CrudResult
    {
        public bool IsSuccess { get; init; }
        public long MatchedCount { get; init; }
        public long ModifiedCount { get; init; }
        public IEnumerable<Guid> AffectedIds { get; init; } = Array.Empty<Guid>();
        public string ErrorMessage { get; init; }
    }

    public class CrudResult<T> : CrudResult
    {
        public IEnumerable<T> Documents { get; init; } = Array.Empty<T>();
    }

}
