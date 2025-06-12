using Application.Interfaces.Base;
using Domain.DTOs.User;
using Domain.Models.User;

namespace Application.Interfaces
{
    public interface IUserService : IBaseService<UserData>
    {
        public Task<bool> CheckUserExists(Guid userId);
        Task<UserData?> GetAsync(Guid id);
        Task<UserData?> CreateAsync(UserData newUserData);
        Task UpdateAsync(Guid id, UserUpdateDTO updatedUserData);
        Task RemoveAsync(Guid id);
    }
}
