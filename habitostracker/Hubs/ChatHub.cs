using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using HabitTrackerApp.Data;
using Microsoft.EntityFrameworkCore;
using HabitTrackerApp.Models;
using HabitTrackerApp.Services;
using System.Collections.Concurrent;

namespace HabitTrackerApp.Hubs
{
    public class ChatHub : Hub
    {
        private static HashSet<string> ConnectedUsers = new HashSet<string>();

        // 🔥 rastrear timers de desconexión pendientes
        private static ConcurrentDictionary<string, CancellationTokenSource> _disconnectTimers
            = new ConcurrentDictionary<string, CancellationTokenSource>();

        private readonly HabitDbContext _context;
        private readonly OnlineUsersService _onlineUsers;
        private readonly IHubContext<ChatHub> _hubContext;

        public ChatHub(HabitDbContext context, OnlineUsersService onlineUsers, IHubContext<ChatHub> hubContext)
        {
            _context = context;
            _onlineUsers = onlineUsers;
            _hubContext = hubContext;
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
            // 🔥 si había un timer de desconexión pendiente, cancelarlo
            if (_disconnectTimers.TryRemove(userId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }

            _onlineUsers.SetOnline(userId);
            await Groups.AddToGroupAsync(Context.ConnectionId, userId);

            // 🔥 notificar que está online (por si estaba en proceso de desconexión)
            await Clients.All.SendAsync("UserOnline", userId);
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
                // 🔥 cancelar timer de desconexión si existía
                if (_disconnectTimers.TryRemove(userId, out var cts))
                {
                    cts.Cancel();
                    cts.Dispose();
                }

                _onlineUsers.SetOnline(userId);
                await Groups.AddToGroupAsync(Context.ConnectionId, userId);
                await Clients.All.SendAsync("UserOnline", userId);

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
                // 🔥 esperar 30s antes de marcar offline
                // esto permite que el usuario vuelva de otra app sin marcarse offline
                var cts = new CancellationTokenSource();
                _disconnectTimers[userId] = cts;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(30000, cts.Token);

                        // si no se canceló, marcar offline
                        _onlineUsers.SetOffline(userId);
                        _disconnectTimers.TryRemove(userId, out _);

                        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userId));
                        if (user != null)
                        {
                            user.LastOnline = DateTime.UtcNow;
                            await _context.SaveChangesAsync();
                        }

                        await _hubContext.Clients.All.SendAsync("UserOffline", userId);
                        ConnectedUsers.Remove(userId);
                    }
                    catch (TaskCanceledException)
                    {
                        // cancelado porque reconectó — no hacer nada
                    }
                });
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