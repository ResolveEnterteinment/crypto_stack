using Application.Interfaces;
using AspNetCore.Identity.MongoDbCore.Infrastructure;
using Domain.Models.User;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Infrastructure.Services
{
    public class UserService : IUserService
    {
        private readonly IMongoCollection<UserData> _usersCollection;
        private readonly ILogger _logger;

        public UserService(IMongoClient mongoClient, IOptions<MongoDbSettings> mongoDbSettings, ILogger<UserService> logger)
        {
            var database = mongoClient.GetDatabase(mongoDbSettings.Value.DatabaseName);
            _usersCollection = database.GetCollection<UserData>("userDatas");
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<bool> CheckUserExists(Guid userId)
        {
            var user = await GetAsync(userId);
            if (user == null)
            {
                return false;
            }
            return true;
        }

        #region CRUD
        /*public async Task<FetchUsersResponse> GetPaginatedUsers(int startIndex, int fetchCount)
        {
            var options = new FindOptions<UserData>
            {
                Skip = startIndex,
                Limit = fetchCount,
                Sort = Builders<UserData>.Sort.Descending(history => history.CreateTime)
            };

            var result = await _usersCollection.FindAsync(new BsonDocument(), options);
            long totalCount = await _usersCollection.EstimatedDocumentCountAsync();

            return new FetchUsersResponse
            {
                TotalCount = totalCount,
                Success = true,
                Users = result.ToEnumerable().Select(user => new UserDTO
                {
                    Id = user.Id,
                    Email = user.Email,
                    Name = user.FullName,
                    HasSubscribed = false
                }).ToList()
            };
        }*/

        public async Task<UserData?> GetAsync(Guid id) =>
            await _usersCollection.Find(x => x.Id == id).FirstOrDefaultAsync();

        public async Task<UserData?> CreateAsync(UserData newUserData)
        {
            try
            {
                await _usersCollection.InsertOneAsync(newUserData);

                return newUserData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
                return null;
            }
        }

        public async Task UpdateAsync(Guid id, UserData updatedUserData) =>
            await _usersCollection.ReplaceOneAsync(x => x.Id == id, updatedUserData);

        public async Task RemoveAsync(Guid id) =>
            await _usersCollection.DeleteOneAsync(x => x.Id == id);
        #endregion CRUD END
    }
}
