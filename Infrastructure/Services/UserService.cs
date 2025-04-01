using Application.Interfaces;
using Domain.DTOs.Settings;
using Domain.Models.User;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class UserService : BaseService<UserData>, IUserService
    {
        private static readonly TimeSpan USER_CACHE_DURATION = TimeSpan.FromMinutes(15);
        private const string USER_EXISTS_CACHE_PREFIX = "user:exists:";

        public UserService(
            IMongoClient mongoClient,
            IOptions<MongoDbSettings> mongoDbSettings,
            ILogger<UserService> logger,
            IMemoryCache cache
        ) : base(
            mongoClient,
            mongoDbSettings,
            "userDatas",
            logger,
            cache,
            new List<CreateIndexModel<UserData>>
            {
                new CreateIndexModel<UserData>(
                    Builders<UserData>.IndexKeys.Ascending(u => u.Email),
                    new CreateIndexOptions { Name = "Email_1", Unique = true }
                )
            }
        )
        {
        }

        /// <summary>
        /// Checks if a user exists with caching
        /// </summary>
        public async Task<bool> CheckUserExists(Guid userId)
        {
            if (userId == Guid.Empty)
            {
                return false;
            }

            // Check cache first
            string cacheKey = $"{USER_EXISTS_CACHE_PREFIX}{userId}";

            if (_cache.TryGetValue(cacheKey, out bool exists))
            {
                return exists;
            }

            // Check database
            exists = await ExistsAsync(userId);

            // Cache the result
            _cache.Set(cacheKey, exists, USER_CACHE_DURATION);

            return exists;
        }

        /// <summary>
        /// Gets a user by ID with caching
        /// </summary>
        public new async Task<UserData> GetAsync(Guid id)
        {
            return await GetByIdAsync(id);
        }

        /// <summary>
        /// Creates a new user
        /// </summary>
        public async Task<UserData> CreateAsync(UserData newUserData)
        {
            try
            {
                // Validate user data
                if (newUserData == null)
                {
                    throw new ArgumentNullException(nameof(newUserData));
                }

                if (string.IsNullOrEmpty(newUserData.Email))
                {
                    throw new ArgumentException("Email is required", nameof(newUserData));
                }

                if (string.IsNullOrEmpty(newUserData.FullName))
                {
                    throw new ArgumentException("Full name is required", nameof(newUserData));
                }

                // Ensure ID is assigned
                if (newUserData.Id == Guid.Empty)
                {
                    newUserData.Id = Guid.NewGuid();
                }

                // Check if email is already in use
                var existingUserFilter = Builders<UserData>.Filter.Eq(u => u.Email, newUserData.Email);
                var existingUser = await GetOneAsync(existingUserFilter);

                if (existingUser != null)
                {
                    _logger.LogWarning("Attempted to create user with existing email: {Email}", newUserData.Email);
                    throw new InvalidOperationException($"A user with email {newUserData.Email} already exists");
                }

                // Insert user
                var result = await InsertOneAsync(newUserData);

                if (!result.IsAcknowledged || result.InsertedId == null)
                {
                    throw new Exception("Failed to create user");
                }

                // Update user exists cache
                string cacheKey = $"{USER_EXISTS_CACHE_PREFIX}{newUserData.Id}";
                _cache.Set(cacheKey, true, USER_CACHE_DURATION);

                _logger.LogInformation("Created user {UserId} with email {Email}", newUserData.Id, newUserData.Email);

                return newUserData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create user: {Message}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Updates a user
        /// </summary>
        public async Task UpdateAsync(Guid id, UserData updatedUserData)
        {
            try
            {
                // Validate
                if (id == Guid.Empty)
                {
                    throw new ArgumentException("Invalid user ID", nameof(id));
                }

                if (updatedUserData == null)
                {
                    throw new ArgumentNullException(nameof(updatedUserData));
                }

                // Update user
                await UpdateOneAsync(id, updatedUserData);

                // Invalidate cache
                InvalidateEntityCache(id);

                _logger.LogInformation("Updated user {UserId}", id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update user {UserId}: {Message}", id, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Deletes a user
        /// </summary>
        public async Task RemoveAsync(Guid id)
        {
            try
            {
                // Validate
                if (id == Guid.Empty)
                {
                    throw new ArgumentException("Invalid user ID", nameof(id));
                }

                // Delete user
                await DeleteAsync(id);

                // Invalidate cache
                InvalidateEntityCache(id);

                string cacheKey = $"{USER_EXISTS_CACHE_PREFIX}{id}";
                _cache.Remove(cacheKey);

                _logger.LogInformation("Removed user {UserId}", id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove user {UserId}: {Message}", id, ex.Message);
                throw;
            }
        }
    }
}