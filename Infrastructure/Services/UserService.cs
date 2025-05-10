using Application.Interfaces;
using Application.Interfaces.Base;
using Application.Interfaces.Logging;
using Domain.Exceptions;
using Domain.Models.User;
using Infrastructure.Services.Base;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class UserService : BaseService<UserData>, IUserService
    {
        private const string USER_EXISTS_CACHE_PREFIX = "user:exists:";
        private static readonly TimeSpan USER_CACHE_DURATION = TimeSpan.FromMinutes(15);

        public UserService(
            ICrudRepository<UserData> repository,
            ICacheService<UserData> cacheService,
            IMongoIndexService<UserData> indexService,
            ILoggingService logger,
            IEventService eventService
        ) : base(
            repository,
            cacheService,
            indexService,
            logger,
            eventService,
            new[]
            {
                new CreateIndexModel<UserData>(
                    Builders<UserData>.IndexKeys.Ascending(u => u.Email),
                    new CreateIndexOptions { Name = "Email_1", Unique = true }
                )
            }
        )
        { }

        public async Task<bool> CheckUserExists(Guid userId)
        {
            if (userId == Guid.Empty)
                return false;

            var cacheKey = USER_EXISTS_CACHE_PREFIX + userId;
            if (CacheService.TryGetValue<UserData>(cacheKey, out _))
            {
                return true;
            }

            var checkExists = await _repository.CheckExistsAsync(userId);
            return checkExists;
        }

        public async Task<UserData?> GetAsync(Guid id)
        {
            var result = await GetByIdAsync(id);
            return result.IsSuccess ? result.Data : null;
        }

        public async Task<UserData?> CreateAsync(UserData newUserData)
        {
            if (newUserData == null)
                throw new ArgumentNullException(nameof(newUserData));
            if (string.IsNullOrWhiteSpace(newUserData.Email))
                throw new ArgumentException("Email is required", nameof(newUserData));
            if (string.IsNullOrWhiteSpace(newUserData.FullName))
                throw new ArgumentException("Full name is required", nameof(newUserData));

            // Check duplicate email
            var filter = Builders<UserData>.Filter.Eq(u => u.Email, newUserData.Email);
            var existing = await GetOneAsync(filter);
            if (existing == null || !existing.IsSuccess)
                throw new DatabaseException("Error checking existing user by email.");
            if (existing.Data != null)
            {
                Logger.LogWarning("Attempt to create user with existing email: {Email}", newUserData.Email);
                throw new InvalidOperationException($"A user with email {newUserData.Email} already exists.");
            }

            // Insert
            var insertResult = await InsertAsync(newUserData);
            if (!insertResult.IsSuccess)
                throw new DatabaseException("Failed to insert new user.");

            // Invalidate exists cache
            var cacheKey = USER_EXISTS_CACHE_PREFIX + newUserData.Id;
            CacheService.Invalidate(cacheKey);
            Logger.LogInformation("Created user {UserId} with email {Email}", newUserData.Id, newUserData.Email);
            return newUserData;
        }

        public async Task UpdateAsync(Guid id, UserData updatedUserData)
        {
            if (id == Guid.Empty)
                throw new ArgumentException("Invalid user ID", nameof(id));
            if (updatedUserData == null)
                throw new ArgumentNullException(nameof(updatedUserData));

            var updateResult = await base.UpdateAsync(id, updatedUserData);
            if (!updateResult.IsSuccess)
                throw new DatabaseException($"Failed to update user {id}: {updateResult.ErrorMessage}");

            // Invalidate exists cache
            CacheService.Invalidate(USER_EXISTS_CACHE_PREFIX + id);
            Logger.LogInformation("Updated user {UserId}", id);
        }

        public async Task RemoveAsync(Guid id)
        {
            if (id == Guid.Empty)
                throw new ArgumentException("Invalid user ID", nameof(id));

            var deleteResult = await DeleteAsync(id);
            if (!deleteResult.IsSuccess)
                throw new DatabaseException($"Failed to remove user {id}: {deleteResult.ErrorMessage}");

            // Invalidate caches
            CacheService.Invalidate(USER_EXISTS_CACHE_PREFIX + id);
            Logger.LogInformation("Removed user {UserId}", id);
        }
    }
}
