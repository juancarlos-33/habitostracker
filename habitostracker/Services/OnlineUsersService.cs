using System.Collections.Concurrent;

namespace HabitTrackerApp.Services
{
    public class OnlineUsersService
    {
        private static readonly ConcurrentDictionary<string, bool> _onlineUsers = new();
        // 🔥 userId → con quién está chateando
        private static readonly ConcurrentDictionary<string, string> _activeChats = new();

        public void SetOnline(string userId) => _onlineUsers[userId] = true;

        public void SetOffline(string userId)
        {
            _onlineUsers.TryRemove(userId, out _);
            _activeChats.TryRemove(userId, out _);
        }

        public bool IsOnline(string userId) => _onlineUsers.ContainsKey(userId);

        public void SetActiveChat(string userId, string withUserId) => _activeChats[userId] = withUserId;

        public void ClearActiveChat(string userId) => _activeChats.TryRemove(userId, out _);

        public bool IsInChatWith(string userId, string withUserId)
        {
            return _activeChats.TryGetValue(userId, out var chatWith) && chatWith == withUserId;
        }

        public List<string> GetOnlineUsers() => _onlineUsers.Keys.ToList();
    }
}