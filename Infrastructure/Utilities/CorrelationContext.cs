namespace Infrastructure.Utilities
{
    public static class CorrelationContext
    {
        private static readonly AsyncLocal<CorrelationInfo?> _current = new();

        public static CorrelationInfo? Current
        {
            get => _current.Value;
            set => _current.Value = value;
        }

        public static void Set(Guid correlationId, Guid? parentCorrelationId = null)
        {
            Current = new CorrelationInfo
            {
                CorrelationId = correlationId,
                ParentCorrelationId = parentCorrelationId
            };
        }

        public static void Clear() => Current = null;
    }

    public class CorrelationInfo
    {
        public Guid CorrelationId { get; set; }
        public Guid? ParentCorrelationId { get; set; }
    }
}
