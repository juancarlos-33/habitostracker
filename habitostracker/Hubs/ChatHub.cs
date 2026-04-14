using Microsoft.AspNetCore.SignalR;
using HabitTrackerApp.Data;
using Microsoft.EntityFrameworkCore;
using HabitTrackerApp.Models;
using HabitTrackerApp.Services;

namespace HabitTrackerApp.Hubs
{
    public class ChatHub : Hub
    {
        private static HashSet<string> ConnectedUsers = new HashSet<string>();
        private readonly HabitDbContext _context;
        private readonly OnlineUsersService _onlineUsers;

        public ChatHub(HabitDbContext context, OnlineUsersService onlineUsers)
        {
            _context = context;
            _onlineUsers = onlineUsers;
        }

        public async Task KickBlockedIP(string ip)
        {
            await Clients.All.SendAsync("IPBlocked", ip);
        }

        public async Task SendNotification(string userId, string message)
        {
            await Clients.User(userId).SendAsync("ReceiveNotification", message);
        }

        public async Task ForceLogout(string userId)
        {
            await Clients.User(userId).SendAsync("ForceLogout");
        }

        public async Task JoinUserGroup(string userId)
        {
            _onlineUsers.SetOnline(userId);
            await Groups.AddToGroupAsync(Context.ConnectionId, userId);
        }

        public async Task UserTyping(string receiverId, string username)
        {
            await Clients.Group(receiverId).SendAsync("ShowTyping", username);
        }

        public async Task StopTyping(string receiverId)
        {
            await Clients.Group(receiverId).SendAsync("HideTyping");
        }

        public async Task MessagesViewed(string senderId)
        {
            await Clients.Group(senderId).SendAsync("ForceSeenUpdate");
        }

        public override async Task OnConnectedAsync()
        {
            var userId = Context.UserIdentifier;
            if (!string.IsNullOrEmpty(userId))
            {
                _onlineUsers.SetOnline(userId);
                await Groups.AddToGroupAsync(Context.ConnectionId, userId);
                await Clients.All.SendAsync("UserOnline", userId);

                // 🔥 notificar a quienes le enviaron mensajes mientras estaba offline
                // para que cambien sus chulos de ✔ a ✔✔ grises
                var pendingSenders = await _context.Messages
                    .Where(m => m.ReceiverId == int.Parse(userId) && !m.IsRead)
                    .Select(m => m.SenderId)
                    .Distinct()
                    .ToListAsync();

                foreach (var senderId in pendingSenders)
                {
                    var pendingMsgIds = await _context.Messages
                        .Where(m => m.SenderId == senderId &&
                                    m.ReceiverId == int.Parse(userId) &&
                                    !m.IsRead)
                        .Select(m => m.Id)
                        .ToListAsync();

                    foreach (var msgId in pendingMsgIds)
                    {
                        // 🔥 decirle al emisor que ese mensaje llegó → ✔✔ grises
                        await Clients.Group(senderId.ToString())
                            .SendAsync("MessageSentConfirm", msgId, "");
                    }
                }

                if (!ConnectedUsers.Contains(userId))
                {
                    ConnectedUsers.Add(userId);
                    var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userId));
                    var superAdmin = await _context.Users.FirstOrDefaultAsync(u => u.Role == "SuperAdmin");
                    if (superAdmin != null && user != null && user.Role != "SuperAdmin")
                        await Clients.User(superAdmin.Id.ToString()).SendAsync("UserConnectedNotification", user.Username);
                }
            }
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.UserIdentifier;
            if (!string.IsNullOrEmpty(userId))
            {
                _onlineUsers.SetOffline(userId);
                ConnectedUsers.Remove(userId);
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userId));
                if (user != null)
                {
                    user.LastOnline = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }
                await Clients.All.SendAsync("UserOffline", userId);
            }
            await base.OnDisconnectedAsync(exception);
        }

        public async Task CallUser(string receiverId)
        {
            await Clients.Group(receiverId).SendAsync("IncomingCall", Context.UserIdentifier);
        }

        public async Task SendOffer(string receiverId, string offer)
        {
            await Clients.Group(receiverId).SendAsync("ReceiveOffer", offer);
        }

        public async Task SendIceCandidate(string receiverId, string candidate)
        {
            await Clients.Group(receiverId).SendAsync("ReceiveIceCandidate", candidate);
        }

        public async Task SendAnswer(string receiverId, string answer)
        {
            await Clients.Group(receiverId).SendAsync("ReceiveAnswer", answer);
        }

        public async Task SendReaction(string receiverId, int messageId, string reaction)
        {
            await Clients.Group(receiverId).SendAsync("ReceiveReaction", messageId, reaction);
        }

        public async Task CallReady(string receiverId)
        {
            await Clients.Group(receiverId).SendAsync("PeerReady");
        }

        public async Task HangUp(string receiverId)
        {
            await Clients.Group(receiverId).SendAsync("CallEnded");
        }

        public async Task StartRecording(string receiverId)
        {
            await Clients.Group(receiverId).SendAsync("UserRecording");
        }

        public async Task StopRecording(string receiverId)
        {
            await Clients.Group(receiverId).SendAsync("UserStoppedRecording");
        }
    }
}