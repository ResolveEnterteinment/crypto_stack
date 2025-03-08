using Domain.Models.Trail;
using MongoDB.Bson;

namespace Application.Interfaces
{
    public interface ITrailService
    {
        public Task<ObjectId?> StartTrailAsync(object entity, string action);
        public Task AddStepAsync(ObjectId trailId, TrailEntry entry);
    }
}
