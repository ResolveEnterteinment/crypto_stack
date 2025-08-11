using Application.Interfaces.Base;
using Domain.DTOs;

public interface INotificationService
{
    Task<ResultWrapper<IEnumerable<NotificationData>>> GetUserNotificationsAsync(string userId);
    Task<ResultWrapper<bool>> CreateAndSendNotificationAsync(NotificationData notification);
    Task<ResultWrapper<bool>> MarkAsReadAsync(Guid notificationId);
    Task<ResultWrapper<bool>> MarkAllAsReadAsync(Guid userId);
    bool AddUserToList(string userToAdd);
    void RemoveUserFromList(string user);
    void AddUserConnectionId(string user, string connectionId);
    string GetById(string connectionId);
    string GetByUser(string user);
}