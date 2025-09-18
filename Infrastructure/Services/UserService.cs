using Application.Interfaces;
using Domain.DTOs.User;
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
            IServiceProvider serviceProvider
        ) : base(
            serviceProvider,
            new()
            {
                IndexModels = [
                    new CreateIndexModel<UserData>(
                        Builders<UserData>.IndexKeys.Ascending(u => u.Email),
                        new CreateIndexOptions { Name = "Email_1", Unique = true })
                    ]
            })
        { }

        public async Task<bool> CheckUserExists(Guid userId)
        {
            if (userId == Guid.Empty)
                return false;

            var checkExists = await _repository.ExistsAsync(userId);
            return checkExists;
        }

        public async Task<UserData?> GetByIdAsync(Guid id)
        {
            var result = await base.GetByIdAsync(id);
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
                _loggingService.LogWarning("Attempt to create user with existing email: {Email}", newUserData.Email);
                throw new InvalidOperationException($"A user with email {newUserData.Email} already exists.");
            }

            // Insert
            var insertResult = await InsertAsync(newUserData);
            if (!insertResult.IsSuccess)
                throw new DatabaseException("Failed to insert new user.");

            _loggingService.LogInformation("Created user {UserId} with email {Email}", newUserData.Id, newUserData.Email);
            return newUserData;
        }

        public async Task UpdateAsync(Guid id, UserUpdateDTO updatedUserData)
        {
            if (id == Guid.Empty)
                throw new ArgumentException("Invalid user ID", nameof(id));
            if (updatedUserData == null)
                throw new ArgumentNullException(nameof(updatedUserData));

            var updateResult = await base.UpdateAsync(id, updatedUserData);
            if (!updateResult.IsSuccess)
                throw new DatabaseException($"Failed to update user {id}: {updateResult.ErrorMessage}");

            _loggingService.LogInformation("Updated user {UserId}", id);
        }

        public async Task RemoveAsync(Guid id)
        {
            if (id == Guid.Empty)
                throw new ArgumentException("Invalid user ID", nameof(id));

            var deleteResult = await DeleteAsync(id);
            if (!deleteResult.IsSuccess)
                throw new DatabaseException($"Failed to remove user {id}: {deleteResult.ErrorMessage}");

            _loggingService.LogInformation("Removed user {UserId}", id);
        }
    }
}
