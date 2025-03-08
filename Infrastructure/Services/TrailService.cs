using Application.Interfaces;
using Domain.Models.Trail;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Newtonsoft.Json;

namespace Domain.Services
{
    public class TrailService : ITrailService
    {
        private readonly string _localStoragePath;
        private readonly ILogger _logger;
        public TrailService(string localStoragePath, ILogger logger)
        {

            _localStoragePath = localStoragePath;
            _logger = logger;

        }
        public async Task<ObjectId?> StartTrailAsync(object entity, string action)
        {
            try
            {
                var trail = new TrailData { };
                trail.Entries.Append(new TrailEntry()
                {
                    Entity = nameof(entity),
                    Action = action,
                    IsSuccess = true,
                    Message = null
                });
                await SaveLocallyAsync(trail);
                return trail._id;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public async Task AddStepAsync(ObjectId trailId, TrailEntry entry)
        {
            try
            {
                var trail = await LoadFromLocalAsync(trailId);
                trail.Entries.Append(entry);
                await SaveLocallyAsync(trail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add trail step to {TrailId}", trailId);
                throw;
            }
        }

        public async Task<TrailData> GetTrailByIdAsync(ObjectId trailId)
        {
            return await LoadFromLocalAsync(trailId);
        }

        private async Task SaveLocallyAsync(TrailData trail)
        {
            var filePath = Path.Combine(_localStoragePath, $"{trail._id}.json");
            try
            {
                var json = JsonConvert.SerializeObject(trail);
                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save trail {TrailId} locally", trail._id);
                throw; // Or handle as needed
            }
        }

        private async Task<TrailData> LoadFromLocalAsync(ObjectId trailId)
        {
            var filePath = Path.Combine(_localStoragePath, $"{trailId}.json");
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException();
            }
            var json = await File.ReadAllTextAsync(filePath);
            var data = JsonConvert.DeserializeObject<TrailData>(json);
            if (data is null)
            {
                throw new Exception($"Unable to desrialize JSON to {typeof(TrailData).Name}. Returned null.");
            }
            return data;
        }
    }
}
