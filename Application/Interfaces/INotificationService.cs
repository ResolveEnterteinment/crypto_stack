using Domain.DTOs;
using MongoDB.Driver;

public interface INotificationService
{
    public Task<IEnumerable<NotificationData>> GetUserNotificationsAsync(string userId);
    public Task<ResultWrapper<InsertResult>> CreateNotificationAsync(NotificationData notification);
    public Task<ResultWrapper<UpdateResult>> MarkAsReadAsync(Guid notificationId);
}