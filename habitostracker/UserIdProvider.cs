using Microsoft.AspNetCore.SignalR;

namespace HabitTrackerApp
{
    public class UserIdProvider : IUserIdProvider
    {
        public string GetUserId(HubConnectionContext connection)
        {
            return connection.User?.FindFirst("UserId")?.Value;
        }
    }
}