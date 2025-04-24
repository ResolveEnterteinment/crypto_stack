using Application.Interfaces.Base;
using Domain.DTOs;

public interface INotificationService : IBaseService<NotificationData>
{
    public Task<ResultWrapper<IEnumerable<NotificationData>>> GetUserNotificationsAsync(string userId);
    public Task<ResultWrapper> CreateAndSendNotificationAsync(NotificationData notification);
    public Task<ResultWrapper> MarkAsReadAsync(Guid notificationId);
    public bool AddUserToList(string userToAdd);
    public void RemoveUserFromList(string user);
    public void AddUserConnectionId(string user, string connectionId);
    public string GetById(string connectionId);
    public string GetByUser(string user);
}