namespace Application.Interfaces
{
    public interface ICacheWarmupService
    {
        void QueueUserCacheWarmup(Guid userId);
    }
}
