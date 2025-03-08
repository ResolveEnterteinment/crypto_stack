using Application.Interfaces;
using AspNetCore.Identity.MongoDbCore.Infrastructure;
using Domain.Models.User;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Domain.Services
{
    public class UserService : IUserService
    {
        private readonly IMongoCollection<UserData> _usersCollection;
        private readonly ILogger _logger;

        public UserService(IOptions<MongoDbSettings> mongoDbSettings, ILogger<UserService> logger)
        {
            var mongoClient = new MongoClient(mongoDbSettings.Value.ConnectionString);

            var mongoDatabase = mongoClient.GetDatabase(
                mongoDbSettings.Value.DatabaseName);

            _usersCollection = mongoDatabase.GetCollection<UserData>("userDatas");
            _logger = logger;
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

        public async Task<UserData?> GetAsync(ObjectId id) =>
            await _usersCollection.Find(x => x._id == id).FirstOrDefaultAsync();

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

        public async Task UpdateAsync(ObjectId id, UserData updatedUserData) =>
            await _usersCollection.ReplaceOneAsync(x => x._id == id, updatedUserData);

        public async Task RemoveAsync(ObjectId id) =>
            await _usersCollection.DeleteOneAsync(x => x._id == id);
        #endregion CRUD END
    }
}
