using Microsoft.AspNetCore.SignalR;

namespace HabitTrackerApp.Services
{
    public class CustomUserIdProvider : IUserIdProvider
    {
        public string GetUserId(HubConnectionContext connection)
        {
            return connection.User?.FindFirst("UserId")?.Value;
        }
    }
}