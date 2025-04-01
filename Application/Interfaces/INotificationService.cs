using Domain.DTOs;
using MongoDB.Driver;

public interface INotificationService
{
    public Task<IEnumerable<NotificationData>> GetUserNotificationsAsync(string userId);
    public Task<ResultWrapper<InsertResult>> CreateNotificationAsync(NotificationData notification);
    public Task<ResultWrapper<UpdateResult>> MarkAsReadAsync(Guid notificationId);
    public bool AddUserToList(string userToAdd);
    public void RemoveUserFromList(string user);
    public void AddUserConnectionId(string user, string connectionId);
    public string GetById(string connectionId);
    public string GetByUser(string user);
}