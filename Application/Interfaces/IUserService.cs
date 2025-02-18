using Domain.Models.User;
using MongoDB.Bson;

namespace Application.Interfaces
{
    public interface IUserService
    {
        Task<UserData?> GetAsync(ObjectId id);
        Task<UserData?> CreateAsync(UserData newUserData);
        Task UpdateAsync(ObjectId id, UserData updatedUserData);

        Task RemoveAsync(ObjectId id);
    }
}
