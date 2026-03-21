using System.Collections.Concurrent;

namespace HabitTrackerApp.Services
{
    public class OnlineUsersService
    {
        private static readonly ConcurrentDictionary<string, bool> _onlineUsers = new();

        public void SetOnline(string userId)
        {
            _onlineUsers[userId] = true;
        }

        public void SetOffline(string userId)
        {
            _onlineUsers.TryRemove(userId, out _);
        }

        public bool IsOnline(string userId)
        {
            return _onlineUsers.ContainsKey(userId);
        }

        public List<string> GetOnlineUsers()
        {
            return _onlineUsers.Keys.ToList();
        }
    }
}