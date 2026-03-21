using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
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

        // 🔔 CUANDO EL USUARIO VE LOS MENSAJES
        public async Task MessagesViewed(string senderId)
        {
            await Clients.Group(senderId).SendAsync("ForceSeenUpdate");
        }

        // 🟢 CUANDO EL USUARIO SE CONECTA
        public override async Task OnConnectedAsync()
        {
            var userId = Context.UserIdentifier;

            if (!string.IsNullOrEmpty(userId))
            {
                // marcar usuario online
                _onlineUsers.SetOnline(userId);

                // unir al grupo del usuario
                await Groups.AddToGroupAsync(Context.ConnectionId, userId);

                // avisar a todos que está online
                await Clients.All.SendAsync("UserOnline", userId);

                if (!ConnectedUsers.Contains(userId))
                {
                    ConnectedUsers.Add(userId);

                    var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userId));
                    var superAdmin = await _context.Users.FirstOrDefaultAsync(u => u.Role == "SuperAdmin");

                    if (superAdmin != null && user != null && user.Role != "SuperAdmin")
                    {
                        await Clients.User(superAdmin.Id.ToString())
                            .SendAsync("UserConnectedNotification", user.Username);
                    }
                }
            }

            await base.OnConnectedAsync();
        }

        // ⚫ CUANDO EL USUARIO SE DESCONECTA
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.UserIdentifier;

            if (!string.IsNullOrEmpty(userId))
            {
                _onlineUsers.SetOffline(userId);

                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == int.Parse(userId));

                if (user != null)
                {
                    user.LastOnline = DateTime.Now;
                    await _context.SaveChangesAsync();
                }



                // avisar a todos que está offline
                await Clients.All.SendAsync("UserOffline", userId);
            }

            await base.OnDisconnectedAsync(exception);
        }

        public async Task CallUser(string receiverId)
        {
            await Clients.Group(receiverId)
                .SendAsync("IncomingCall", Context.UserIdentifier);
        }
        public async Task SendOffer(string receiverId, string offer)
        {
            await Clients.Group(receiverId)
                .SendAsync("ReceiveOffer", offer);
        }
        public async Task SendIceCandidate(string receiverId, string candidate)
        {
            await Clients.Group(receiverId)
                .SendAsync("ReceiveIceCandidate", candidate);
        }
    }
}