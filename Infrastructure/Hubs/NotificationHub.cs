using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Hubs
{
    public class NotificationHub : Hub
    {
        //private readonly IHubContext<NotificationHub> _hubContext;
        private readonly INotificationService _notificationService;
        private readonly ILogger<NotificationHub> _logger;
        public NotificationHub(INotificationService notificationService, ILogger<NotificationHub> logger)
        {
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _logger = logger;
        }
        public async Task SendNotification(string userId, string message)
        {
            await Clients.User(userId).SendAsync("ReceiveNotification", message);

        }

        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation("Connection attempt: {ConnectionId}", Context.ConnectionId);
            Console.Write("Context : ", Context.ToString());
            if (Context.User?.Identity?.IsAuthenticated == false)
            {
                _logger.LogWarning("User not authenticated for {ConnectionId}. Proceeding with connection", Context.ConnectionId);
                await Clients.Caller.SendAsync("UserFailedAuthentication", Context.ConnectionId);
                // Don’t return; let connection proceed for debugging
            }
            else
            {
                _logger.LogInformation("User authenticated.");
                string userId = Context.UserIdentifier ?? Context.User?.FindFirst("sub")?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogError("No userId found despite authentication");
                    return;
                }
                _notificationService.AddUserToList(userId);
                _notificationService.AddUserConnectionId(userId, Context.ConnectionId);
                _logger.LogInformation("User {UserId} connected", userId);
            }
            await base.OnConnectedAsync();
            await Clients.Caller.SendAsync("UserConnected", Context.ConnectionId);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var user = _notificationService.GetUserConnectionById(Context.ConnectionId);
            if (user != null)
            {
                _notificationService.RemoveUserFromList(user);
            }
            await base.OnDisconnectedAsync(exception);
        }

        public async Task AddUserConnectionId(string name)
        {
            _notificationService.AddUserConnectionId(name, Context.ConnectionId);
        }
    }
}
