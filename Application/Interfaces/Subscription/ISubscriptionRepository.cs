using Application.Interfaces.Base;
using Domain.Models.Subscription;

namespace Application.Interfaces.Subscription
{
    public interface ISubscriptionRepository : ICrudRepository<SubscriptionData>
    {
    }
}
