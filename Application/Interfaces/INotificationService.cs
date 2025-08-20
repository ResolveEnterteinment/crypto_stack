using Application.Interfaces.Base;
using Domain.DTOs;

public interface INotificationService
{
    Task<ResultWrapper<IEnumerable<NotificationData>>> GetUserNotificationsAsync(Guid userId);
    Task<ResultWrapper<bool>> CreateAndSendNotificationAsync(NotificationData notification);
    Task<ResultWrapper> MarkAsReadAsync(Guid notificationId);
    Task<ResultWrapper> MarkAllAsReadAsync(Guid userId);
    bool AddUserToList(string userToAdd);
    void RemoveUserFromList(string user);
    void AddUserConnectionId(string user, string connectionId);
    string GetById(string connectionId);
    string GetByUser(string user);
}