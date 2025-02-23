using Domain.DTOs;
using Domain.Interfaces;
using Domain.Models.Subscription;
using MongoDB.Bson;

namespace Infrastructure.Services
{
    public interface ISubscriptionService : IRepository<SubscriptionData>
    {
        public Task<FetchAllocationsResult> GetAllocationsAsync(ObjectId subscriptionId);
    }
}